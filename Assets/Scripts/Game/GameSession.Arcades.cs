using System.Collections.Generic;
using System.Text;
using UnityEngine;

// V0.6.1 (GDD 10.2) : Jeton WOOF donné par Pogo, bornes d'arcade (charge par les Balises, parties, lots) et boules à
// facettes (musique disco). Aucun message n'est envoyé en continu : seulement des événements (charge, début et fin de
// partie, flèches de la danse, descente de la pince).
// Messages : JT                                   hôte -> joueur : Pogo lui donne un Jeton WOOF
//            AY|borne|source ("x" : aucune)       hôte -> tous : Balise qui charge la borne
//            AU|borne|case -> AK|borne|case|ok    joueur -> hôte : Jeton inséré (ok : 1 accepté, 0 refusé)
//            AS|borne|joueur|donnée|écoulé        hôte -> tous : partie commencée (Roulette : section tirée)
//            AF|borne                             hôte -> tous : partie terminée
//            AP|borne|manche|t0,t1,t2,t3,t4       hôte -> tous : peluches de la Machine à pince (-1 : vide)
//            AW|borne|x|z -> AM|borne|x|z         joueur -> hôte -> tous : la pince descend à cet endroit
//            AC|borne|emplacement                 joueur -> hôte : fin de la pince (-1 : rien attrapé)
//            AD|... -> AA|borne|flèche|n|erreurs|réussi   joueur -> hôte -> tous : flèche de l'Arcade Dance (réussi : -1
//                                                 affichée, 0 manquée, 1 réussie, -2 fin de la partie)
//            AE|borne|erreurs                     joueur -> hôte : fin de la danse
//            AG|0/1                               hôte -> tous : bornes du Hub 1 (godmod)
public partial class GameSession
{
    // Bornes connues (zones générées et Hub 1 en godmod), par identifiant.
    readonly Dictionary<int, ArcadeMachine> arcades = new Dictionary<int, ArcadeMachine>();
    readonly List<ArcadeMachine> hubArcades = new List<ArcadeMachine>();
    // Source de charge de chaque borne ("x" : aucune), peluches retirées des Machines à pince (par manche).
    readonly Dictionary<int, string> arcadeSources = new Dictionary<int, string>();
    readonly Dictionary<int, (int round, int[] types)> clawState = new Dictionary<int, (int, int[])>();
    readonly Dictionary<int, LineRenderer> arcadeBeams = new Dictionary<int, LineRenderer>();
    // Hôte : parties en cours (joueur, début, donnée).
    class ArcadeSession
    {
        public string user;
        public float start;
        public int data;
    }
    readonly Dictionary<int, ArcadeSession> arcadeSessions = new Dictionary<int, ArcadeSession>();
    bool hubArcadesOn;
    float arcadeTimer;
    int arcadeRound = -1;
    const string NoSource = "x";
    // Identifiants des bornes du Hub 1 (godmod).
    const int HubArcadeBase = 9000;
    // Sans nouvelles du joueur (déconnexion), la borne se libère au bout de ce délai.
    const float ArcadeTimeout = 120f;

    // Joueur local : borne en cours d'utilisation (pince ou danse).
    ArcadeMachine clawMachine, danceMachine;
    public static bool ClawActive { get; private set; }

    // Musique disco : une seule à la fois, depuis la boule la plus proche (à moins de 10 m).
    AudioSource discoMusic;
    Beacon discoSource;

    // ================= Bornes : enregistrement =================

    void RegisterArcades(IEnumerable<ArcadeMachine> list)
    {
        foreach (var m in list)
        {
            if (m == null) continue;
            arcades[m.Id] = m;
            ApplyArcadeState(m);
        }
    }

    void UnregisterArcade(ArcadeMachine m)
    {
        if (m == null) return;
        arcades.Remove(m.Id);
        if (arcadeBeams.TryGetValue(m.Id, out var beam) && beam != null) Destroy(beam.gameObject);
        arcadeBeams.Remove(m.Id);
        if (clawMachine == m) EndClaw(false);
        if (danceMachine == m) danceMachine = null;
    }

    // État déjà connu d'une borne qui vient d'être construite (charge, partie en cours, peluches).
    void ApplyArcadeState(ArcadeMachine m)
    {
        m.SetCharged(arcadeSources.TryGetValue(m.Id, out string src) && src != NoSource);
        if (m.Kind == ArcadeKind.Claw) m.SetContents(ClawContents(m.Id));
    }

    // Peluches de la Machine à pince pour la manche en cours : tirées au hasard (50 % lapin, 25 % pingouin, 15 % singe,
    // 10 % chat), le même tirage chez tous ; l'hôte annonce celles qui ont été gagnées.
    int[] ClawContents(int id)
    {
        if (clawState.TryGetValue(id, out var state) && state.round == round) return state.types;
        var rnd = new System.Random(seed * 41 + round * 977 + id * 131);
        var types = new int[GameRules.ClawPlushes];
        for (int i = 0; i < types.Length; i++)
        {
            double roll = rnd.NextDouble();
            int t = 0;
            for (; t < GameRules.PlushChances.Length - 1; t++)
            {
                if (roll < GameRules.PlushChances[t]) break;
                roll -= GameRules.PlushChances[t];
            }
            types[i] = t;
        }
        return types;
    }

    // Godmod (hôte) : une borne de chaque type dans le Hub 1 ; chez tous les joueurs.
    void SetHubArcades(bool on)
    {
        if (on == hubArcadesOn) return;
        hubArcadesOn = on;
        foreach (var m in hubArcades)
        {
            if (m == null) continue;
            UnregisterArcade(m);
            Destroy(m.gameObject);
        }
        hubArcades.Clear();
        if (!on || hub == null) return;
        for (int k = 0; k < 3; k++)
        {
            var (pos, yaw) = WorldBuilder.HubArcadeSpot(hub, k);
            hubArcades.Add(ArcadeMachine.Create(hub.root, HubArcadeBase + k, (ArcadeKind)k, 0, pos, yaw));
        }
        RegisterArcades(hubArcades);
    }

    // ================= Bornes : hôte =================

    // Charge (Balise chargée la plus proche qui voit la borne, Balises des Hubs comprises), fin des parties.
    void UpdateHostArcades()
    {
        if (arcadeSessions.Count == 0) return;
        foreach (var kv in new List<KeyValuePair<int, ArcadeSession>>(arcadeSessions))
        {
            if (!arcades.TryGetValue(kv.Key, out var m) || m == null) { EndArcade(kv.Key); continue; }
            float age = Time.time - kv.Value.start;
            if (m.Kind == ArcadeKind.Roulette && age >= GameRules.RouletteSpin) FinishRoulette(m, kv.Value);
            else if (age >= ArcadeTimeout || !net.Roster.ContainsKey(kv.Value.user)) EndArcade(kv.Key);
        }
        arcadeTimer -= Time.unscaledDeltaTime;
        if (arcadeTimer > 0f) return;
        arcadeTimer = 0.4f;
        foreach (var m in arcades.Values)
        {
            if (m == null || m.Zone > 0 && !ZoneActive(m.Zone)) continue;
            string source = NoSource;
            float best = float.MaxValue;
            foreach (var b in beacons.Placed)
            {
                if (b == null || !b.Relays || !b.Charged) continue;
                float d = (b.Top - m.Anchor).sqrMagnitude;
                if (d < best && beacons.InSight(b.Top, m.Anchor)) { best = d; source = b.Id.ToString(); }
            }
            if (beacons.HubCharged)
                foreach (var h in beacons.Hubs)
                {
                    if (h == null) continue;
                    float d = (h.Top - m.Anchor).sqrMagnitude;
                    if (d < best && beacons.InSight(h.Top, m.Anchor)) { best = d; source = h.Id.ToString(); }
                }
            arcadeSources.TryGetValue(m.Id, out string before);
            if (source == (before ?? NoSource)) continue;
            net.ToAll("AY|" + m.Id + "|" + source);
        }
    }

    void FinishRoulette(ArcadeMachine m, ArcadeSession s)
    {
        ItemType prize = ArcadeMachine.SectionPrize(s.data);
        Vector3 spot = PrizeGround(m);
        if (prize == ItemType.Reveil)
        {
            SpawnReveil(spot + Vector3.up * 0.1f);
            Log("La Roulette réveille un Réveil pour le joueur {0} !", NameOf(s.user));
        }
        else
        {
            AddDropped(prize, spot, -1);
            Log("Le joueur {0} gagne {1} à la Roulette.", NameOf(s.user), Loc.Tr(Items.Get(prize).frName));
        }
        EndArcade(m.Id);
    }

    void EndArcade(int id)
    {
        arcadeSessions.Remove(id);
        net.ToAll("AF|" + id);
    }

    // Lot posé à terre devant la borne (sur le sol, sous l'emplacement prévu).
    static Vector3 PrizeGround(ArcadeMachine m)
    {
        Vector3 p = m.PrizeSpot;
        return Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point + Vector3.up * 0.05f : p;
    }

    // ================= Bornes : joueur local =================

    // Jeton WOOF sélectionné : il est inséré (l'hôte confirme) et la borne s'active. Borne éteinte, occupée ou (pince)
    // vide, ou sans jeton : elle ne réagit pas, sans message.
    public void UseArcade(ArcadeMachine m)
    {
        if (m == null || status.Dead || ClawActive || danceMachine != null || !m.Charged || m.Busy) return;
        int slot = status.Selected;
        if (slot < 0 || status[slot] != ItemType.JetonWoof) return;
        if (m.Kind == ArcadeKind.Claw && !m.HasPlush) return;
        Gesture(PlayerGesture.Use);
        net.ToHost("AU|" + m.Id + "|" + slot);
    }

    // Partie du joueur local : pince ou danse, image par image.
    void UpdateArcadePlay()
    {
        if (clawMachine != null)
        {
            if (status.Dead || clawMachine == null) { EndClaw(true); return; }
            Vector2 move = Vector2.zero;
            if (Controls.Held(GameAction.Forward) || Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
            if (Controls.Held(GameAction.Back) || Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
            if (Controls.Held(GameAction.Right) || Input.GetKey(KeyCode.RightArrow)) move.x += 1f;
            if (Controls.Held(GameAction.Left) || Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
            bool drop = Input.GetKeyDown(KeyCode.Space) || Controls.Down(GameAction.Jump);
            bool wasMoving = clawMoving;
            if (clawMachine.UpdateClaw(Time.deltaTime, move, drop && clawMoving, out int slot))
            {
                net.ToHost("AC|" + clawMachine.Id + "|" + slot);
                EndClaw(false);
                return;
            }
            if (wasMoving && drop)
            {
                clawMoving = false;
                Vector2 at = clawMachine.ClawPosition;
                net.ToHost("AW|" + clawMachine.Id + "|" + NetworkGame.F(at.x) + "|" + NetworkGame.F(at.y));
            }
            OrbitCamera.FixedView = clawMachine.ClawView;
        }
        if (danceMachine != null)
        {
            if (status.Dead) { net.ToHost("AE|" + danceMachine.Id + "|99"); danceMachine = null; return; }
            int pad = danceMachine.PadAt(player.transform.position + Vector3.down * 0.95f);
            var m = danceMachine;
            if (m.UpdateDance(pad, (arrow, step, errors, good) =>
                    net.ToHost("AD|" + m.Id + "|" + arrow + "|" + step + "|" + errors + "|" + good), out int finalErrors))
            {
                net.ToHost("AE|" + m.Id + "|" + finalErrors);
                danceMachine = null;
            }
        }
    }

    bool clawMoving;

    void BeginClaw(ArcadeMachine m)
    {
        clawMachine = m;
        clawMoving = true;
        ClawActive = true;
        m.BeginClaw();
        player.SetFrozen(true);
        OrbitCamera.FixedView = m.ClawView;
        hud.SetUsePrompt(true);
    }

    // 'abort' : partie interrompue (mort, borne disparue) : rien n'est gagné.
    void EndClaw(bool abort)
    {
        if (clawMachine == null) return;
        if (abort) net.ToHost("AC|" + clawMachine.Id + "|-1");
        clawMachine = null;
        ClawActive = false;
        OrbitCamera.FixedView = null;
        hud.SetUsePrompt(false);
        if (player != null && !status.Dead) player.SetFrozen(false);
    }

    // ================= Boules à facettes : musique disco =================

    // À 10 m ou moins d'une boule posée, la musique disco (spatialisée) remplace la musique d'ambiance ; une seule fois,
    // même près de plusieurs boules (la plus proche).
    void UpdateDisco()
    {
        Vector3 me = player.transform.position;
        Beacon nearest = null;
        float best = GameRules.DiscoMusicRange;
        foreach (var b in beacons.Placed)
        {
            if (b == null || !b.IsDisco) continue;
            float d = Vector3.Distance(b.DiscoCenter, me);
            if (d < best) { best = d; nearest = b; }
        }
        GameAudio.DiscoNear = nearest != null && net.InGame;
        if (nearest == discoSource && (discoMusic != null || nearest == null)) return;
        if (discoMusic != null) Destroy(discoMusic.gameObject);
        discoMusic = null;
        discoSource = nearest;
        if (nearest == null) return;
        discoMusic = GameAudio.Loop3D("disco_music", nearest.transform, nearest.DiscoCenter - nearest.transform.position, 0.6f, GameRules.DiscoMusicRange, true);
        if (discoMusic != null) discoMusic.Play();
    }

    // ================= Bornes : rayons et état local =================

    void UpdateArcades()
    {
        // Nouvelle manche : les Machines à pince se remplissent.
        if (arcadeRound != round)
        {
            arcadeRound = round;
            foreach (var m in arcades.Values) if (m != null && m.Kind == ArcadeKind.Claw) m.SetContents(ClawContents(m.Id));
        }
        foreach (var m in arcades.Values)
        {
            if (m == null) continue;
            arcadeBeams.TryGetValue(m.Id, out var line);
            Beacon b = arcadeSources.TryGetValue(m.Id, out string src) && src != NoSource ? SourceBeacon(src) : null;
            if (b == null)
            {
                if (line != null && line.gameObject.activeSelf) line.gameObject.SetActive(false);
                continue;
            }
            if (line == null) arcadeBeams[m.Id] = line = beacons.NewBeam();
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            line.SetPosition(0, b.Top);
            line.SetPosition(1, m.Anchor);
        }
        UpdateArcadePlay();
        UpdateDisco();
    }

    Beacon SourceBeacon(string src)
    {
        int id = NetworkGame.I(src);
        return id < 0 ? beacons.GetHub(-id) : beacons.Get(id);
    }

    void ClearArcadeState()
    {
        if (clawMachine != null) EndClaw(false);
        danceMachine = null;
        arcadeSources.Clear();
        clawState.Clear();
        arcadeSessions.Clear();
        foreach (var beam in arcadeBeams.Values) if (beam != null) Destroy(beam.gameObject);
        arcadeBeams.Clear();
        foreach (var m in arcades.Values) if (m != null) { m.EndSession(); m.SetCharged(false); }
    }

    void SendArcadeSnapshot(string to)
    {
        if (hubArcadesOn) net.ToPeer(to, "AG|1");
        foreach (var kv in arcadeSources) if (kv.Value != NoSource) net.ToPeer(to, "AY|" + kv.Key + "|" + kv.Value);
        foreach (var kv in clawState) if (kv.Value.round == round) net.ToPeer(to, ClawMsg(kv.Key, kv.Value.types));
        foreach (var kv in arcadeSessions)
            net.ToPeer(to, "AS|" + kv.Key + "|" + kv.Value.user + "|" + kv.Value.data + "|" + NetworkGame.F(Time.time - kv.Value.start));
    }

    string ClawMsg(int id, int[] types)
    {
        var sb = new StringBuilder("AP|" + id + "|" + round + "|");
        for (int i = 0; i < types.Length; i++) { if (i > 0) sb.Append(','); sb.Append(types[i]); }
        return sb.ToString();
    }

    // ================= Réception =================

    bool HandleArcadeMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "JT":
                GameAudio.Play2D("token", 0.9f);
                if (HasFreeSlot) status.Add(ItemType.JetonWoof);
                else DropNear(ItemType.JetonWoof, player.transform.position);
                hud.Toast(Loc.T("Pogo vous donne un Jeton WOOF !"));
                return true;
            case "AY" when f.Length >= 3:
            {
                int id = NetworkGame.I(f[1]);
                arcadeSources[id] = f[2];
                if (arcades.TryGetValue(id, out var m) && m != null) m.SetCharged(f[2] != NoSource);
                return true;
            }
            case "AU" when host && f.Length >= 3:
            {
                int id = NetworkGame.I(f[1]);
                bool ok = arcades.TryGetValue(id, out var m) && m != null && m.Charged && !arcadeSessions.ContainsKey(id) && phase != Phase.GameOver
                    && (m.Kind != ArcadeKind.Claw || m.HasPlush);
                net.ToPeer(from, "AK|" + id + "|" + f[2] + "|" + (ok ? 1 : 0));
                if (!ok) return true;
                var s = new ArcadeSession { user = from, start = Time.time, data = m.Kind == ArcadeKind.Roulette ? Random.Range(0, GameRules.RouletteSections) : 0 };
                arcadeSessions[id] = s;
                noiseAt[from] = Time.time;
                net.ToAll("AS|" + id + "|" + from + "|" + s.data + "|0");
                return true;
            }
            case "AK" when f.Length >= 4:
            {
                int id = NetworkGame.I(f[1]), slot = NetworkGame.I(f[2]);
                // Refusé (borne occupée ou éteinte entre-temps) : le joueur garde son jeton.
                if (f[3] != "1") return true;
                // Le jeton est avalé par la machine.
                if (slot >= 0 && slot < GameRules.InventorySize && status[slot] == ItemType.JetonWoof) status.RemoveAt(slot);
                else
                {
                    int any = status.IndexOf(ItemType.JetonWoof);
                    if (any >= 0) status.RemoveAt(any);
                }
                if (arcades.TryGetValue(id, out var m) && m != null)
                {
                    GameAudio.Play3D("token", m.transform.position + Vector3.up, 0.9f, 10f, 0.05f);
                    if (m.Kind == ArcadeKind.Claw) BeginClaw(m);
                    else if (m.Kind == ArcadeKind.Dance)
                    {
                        danceMachine = m;
                        m.BeginDance(m.PadAt(player.transform.position + Vector3.down * 0.95f));
                    }
                }
                return true;
            }
            case "AS" when f.Length >= 5:
            {
                if (arcades.TryGetValue(NetworkGame.I(f[1]), out var m) && m != null) m.StartSession(f[2], NetworkGame.I(f[3]), NetworkGame.P(f[4]));
                return true;
            }
            case "AF" when f.Length >= 2:
            {
                if (arcades.TryGetValue(NetworkGame.I(f[1]), out var m) && m != null) m.EndSession();
                return true;
            }
            case "AP" when f.Length >= 4:
            {
                int id = NetworkGame.I(f[1]);
                var types = new int[GameRules.ClawPlushes];
                string[] parts = f[3].Split(',');
                for (int i = 0; i < types.Length; i++) types[i] = i < parts.Length ? Mathf.Clamp(NetworkGame.I(parts[i]), -1, 3) : -1;
                clawState[id] = (NetworkGame.I(f[2]), types);
                if (arcades.TryGetValue(id, out var m) && m != null && m.Kind == ArcadeKind.Claw) m.SetContents(ClawContents(id));
                return true;
            }
            case "AW" when host && f.Length >= 4:
            {
                int id = NetworkGame.I(f[1]);
                if (arcadeSessions.TryGetValue(id, out var s) && s.user == from) net.ToAll("AM|" + id + "|" + f[2] + "|" + f[3]);
                return true;
            }
            case "AM" when f.Length >= 4:
            {
                int id = NetworkGame.I(f[1]);
                if (arcades.TryGetValue(id, out var m) && m != null && m != clawMachine)
                    m.PlayRemoteDrop(new Vector2(NetworkGame.P(f[2]), NetworkGame.P(f[3])));
                return true;
            }
            case "AC" when host && f.Length >= 3:
            {
                int id = NetworkGame.I(f[1]), slot = NetworkGame.I(f[2]);
                if (!arcadeSessions.TryGetValue(id, out var s) || s.user != from || !arcades.TryGetValue(id, out var m) || m == null) return true;
                int[] types = (int[])ClawContents(id).Clone();
                if (slot >= 0 && slot < types.Length && types[slot] >= 0)
                {
                    ItemType prize = ArcadeMachine.Plushes[types[slot]];
                    types[slot] = -1;
                    clawState[id] = (round, types);
                    net.ToAll(ClawMsg(id, types));
                    AddDropped(prize, PrizeGround(m), -1);
                    Log("Le joueur {0} gagne {1} à la Machine à pince.", NameOf(from), Loc.Tr(Items.Get(prize).frName));
                }
                EndArcade(id);
                return true;
            }
            case "AD" when host && f.Length >= 6:
            {
                int id = NetworkGame.I(f[1]);
                if (arcadeSessions.TryGetValue(id, out var s) && s.user == from) net.ToAll("AA" + string.Join("|", f).Substring(2));
                return true;
            }
            case "AA" when f.Length >= 6:
            {
                int id = NetworkGame.I(f[1]);
                // Le danseur voit déjà sa partie chez lui.
                if (arcades.TryGetValue(id, out var m) && m != null && m != danceMachine)
                {
                    int arrow = NetworkGame.I(f[2]), good = NetworkGame.I(f[5]);
                    if (good == -2) m.ShowDanceEnd(NetworkGame.I(f[4]));
                    else if (good < 0) m.ShowDance(arrow, NetworkGame.I(f[3]), NetworkGame.I(f[4]));
                    else m.FlashPad(arrow, good == 1);
                }
                return true;
            }
            case "AE" when host && f.Length >= 3:
            {
                int id = NetworkGame.I(f[1]), errors = NetworkGame.I(f[2]);
                if (!arcadeSessions.TryGetValue(id, out var s) || s.user != from || !arcades.TryGetValue(id, out var m) || m == null) return true;
                // Sans erreur : diamant ; une erreur : or ; deux : argent ; trois et plus : rien.
                ItemType prize = errors <= 0 ? ItemType.BouleDiamant : errors == 1 ? ItemType.BouleOr : errors == 2 ? ItemType.BouleArgent : ItemType.None;
                if (prize != ItemType.None)
                {
                    AddDropped(prize, PrizeGround(m), -1);
                    Log("Le joueur {0} gagne {1} à l'Arcade Dance.", NameOf(from), Loc.Tr(Items.Get(prize).frName));
                }
                if (errors < 99) net.ToAll("AA|" + id + "|-1|" + GameRules.DanceArrows + "|" + errors + "|-2");
                EndArcade(id);
                return true;
            }
            case "AG" when f.Length >= 2:
                if (!host) SetHubArcades(f[1] == "1");
                return true;
        }
        return false;
    }
}
