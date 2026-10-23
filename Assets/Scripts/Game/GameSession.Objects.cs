using System.Collections.Generic;
using System.Text;
using UnityEngine;

// V0.4.7 : objets uniques (Télévision, Trampoline, Cloche Décorative, GDD 7.6 à 7.8), sujets de test du Terminal
// (/info, /hello, /step1), commande /give, Le Réveil (explosion) et lumières affolées par Le Roi.
// Messages : TQ|télé|vidéo -> TV|télé|vidéo|écoulé (vidéo YouTube d'une Télévision, "" : aucune ; écoulé : secondes depuis
// le début du décompte), QF|critère (joueur -> hôte : critère d'un sujet de test rempli), BV|... (comme BW, sans animation :
// appareils déjà posés, envoyés à un joueur qui arrive).
public partial class GameSession
{
    // ================= Objets uniques =================

    // Hôte : à la génération du terrain, la Télévision, le Trampoline et la Cloche Décorative apparaissent au hasard dans la
    // Zone 1, à l'écart des monticules et les uns des autres.
    const float UniqueSpacing = 12f;

    void PlaceUniqueObjects()
    {
        if (!net.IsHost || maze == null) return;
        var rnd = new System.Random(seed * 53 + 7);
        var spots = new List<Vector3>(maze.itemSpots[0]);
        for (int i = spots.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (spots[i], spots[j]) = (spots[j], spots[i]);
        }
        DeviceKind[] kinds = { DeviceKind.Television, DeviceKind.Trampoline, DeviceKind.Bell };
        var placedAt = new List<Vector3>();
        foreach (float spacing in new[] { UniqueSpacing, 0f })
            foreach (Vector3 spot in spots)
            {
                if (placedAt.Count >= kinds.Length) return;
                if (placedAt.Contains(spot) || NearMound(spot, 1.5f)) continue;
                bool crowded = false;
                foreach (Vector3 other in placedAt) if (Vector3.Distance(other, spot) < spacing) crowded = true;
                if (crowded) continue;
                placedAt.Add(spot);
                PlaceDevice(kinds[placedAt.Count - 1], spot, (float)rnd.NextDouble() * 360f, "");
            }
    }

    bool NearMound(Vector3 p, float distance)
    {
        foreach (var loot in moundLoot.Values) if (Vector3.Distance(loot.pos, p) < distance) return true;
        return false;
    }

    // Hôte : pose un appareil, annoncé à tous et gardé pour les joueurs qui arrivent ; 'placer' : joueur qui l'a posé
    // ("" : génération du terrain, sans animation ni tintement).
    void PlaceDevice(DeviceKind kind, Vector3 pos, float yaw, string placer)
    {
        int id = nextBeaconId++;
        string msg = "BW|" + id + "|" + NetworkGame.F(pos.x) + "|" + NetworkGame.F(pos.y) + "|" + NetworkGame.F(pos.z) + "|" + (int)kind
            + "|" + NetworkGame.F(yaw) + "|" + placer;
        beaconMessages[id] = msg;
        net.ToAll(msg);
    }

    // Appareil posé (live : à l'instant, par un joueur ; sinon déjà là quand on arrive dans la partie).
    void OnDevicePlaced(Beacon b, string placer, bool live)
    {
        if (b == null) return;
        if (live && placer.Length > 0)
        {
            b.PlayPlaced();
            if (b.Kind == DeviceKind.Bell) RingBell(b);
        }
        if (b.Kind == DeviceKind.Television && placer == net.MyId) myTvs.Add(b.Id);
        if (!net.IsHost || !live || (b.Kind != DeviceKind.Beacon && b.Kind != DeviceKind.Enhanced)) return;
        // Sujets de test : une Balise posée en vision directe de la Balise du Hub (hors du Hub : y poser une Balise juste à
        // côté ne compte pas) ; 20 Balises posées.
        if (!WorldBuilder.IsInHub(b.transform.position) && beacons.SeesHub(b)) SetTestFlag(TestBeaconInSight);
        int count = 0;
        foreach (var other in beacons.Placed)
            if (other != null && (other.Kind == DeviceKind.Beacon || other.Kind == DeviceKind.Enhanced)) count++;
        if (count >= TwentyBeacons) SetTestFlag(TestTwentyBeacons);
    }

    // Mort du joueur : ses objets uniques retombent au sol près du corps, posés comme avant.
    void PlaceNear(ItemType type, Vector3 center)
    {
        Vector2 o = Random.insideUnitCircle * 0.9f;
        Vector3 probe = center + new Vector3(o.x, 0.5f, o.y);
        Vector3 pos = Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point : center + Vector3.down * 0.9f;
        net.ToHost("BN|" + NetworkGame.F(pos.x) + "|" + NetworkGame.F(pos.y) + "|" + NetworkGame.F(pos.z) + "|" + (int)Beacon.KindOf(type)
            + "|" + NetworkGame.F(Random.Range(0f, 360f)));
    }

    void UpdateObjects()
    {
        UpdateTelevisions();
        UpdateTrampolines();
        UpdateTestSubjects();
    }

    // ---------- Télévision ----------

    // Hôte : vidéo de chaque Télévision (identifiant YouTube, début du décompte).
    readonly Dictionary<int, (string video, float start)> tvVideos = new Dictionary<int, (string, float)>();
    // Joueur local : Télévisions qu'il a posées, et celles pour lesquelles le lien a déjà été demandé.
    readonly HashSet<int> myTvs = new HashSet<int>();
    readonly HashSet<int> tvAsked = new HashSet<int>();
    const float TvAskRange = 8f;
    const int TvLinkMaxChars = 200;

    // Reliée à une Balise chargée, la Télévision posée par le joueur lui demande un lien (une fois par liaison) ; le signal
    // perdu, l'hôte coupe la vidéo.
    void UpdateTelevisions()
    {
        foreach (var b in beacons.Placed)
        {
            if (b == null || b.Kind != DeviceKind.Television || b.Screen == null) continue;
            if (!b.Charged)
            {
                tvAsked.Remove(b.Id);
                if (net.IsHost && tvVideos.Remove(b.Id)) net.ToAll("TV|" + b.Id + "||0");
                continue;
            }
            if (!myTvs.Contains(b.Id) || b.Screen.HasVideo || tvAsked.Contains(b.Id) || status.Dead || PostItUI.IsOpen || TerminalUI.IsOpen) continue;
            if (Vector3.Distance(player.transform.position, b.transform.position) > TvAskRange) continue;
            tvAsked.Add(b.Id);
            int id = b.Id;
            postItUI.Open("Télévision", "La Télévision capte le signal d'une Balise.\nCollez le lien d'une vidéo YouTube.",
                "https://www.youtube.com/watch?v=…", "Entrée : diffuser · Échap : annuler", TvLinkMaxChars, text => OnTvLink(id, text));
        }
    }

    void OnTvLink(int id, string text)
    {
        string video = YouTubeTv.VideoId(text);
        if (video == null)
        {
            hud.Toast("Ce lien ne mène pas à une vidéo YouTube.");
            return;
        }
        net.ToHost("TQ|" + id + "|" + video);
    }

    string TvMsg(int id) => tvVideos.TryGetValue(id, out var v)
        ? "TV|" + id + "|" + v.video + "|" + NetworkGame.F(Time.time - v.start) : "TV|" + id + "||0";

    // ---------- Trampoline ----------

    // Un peu moins que la hauteur du sol du Hub au sommet du Chapiteau ; un plafond l'arrête (collision normale).
    static float TrampolineSpeed => Mathf.Sqrt(2f * -GameRules.Gravity * GameRules.AirGravityMultiplier(false)
        * WorldBuilder.ChapiteauTop * GameRules.TrampolineJumpFraction);
    const float TrampolineRadius = 0.6f, BounceCooldown = 0.35f;
    float bounceAt = -10f;
    readonly Dictionary<string, float> remoteBounceAt = new Dictionary<string, float>();
    readonly HashSet<string> remoteOnTrampoline = new HashSet<string>();

    void UpdateTrampolines()
    {
        Beacon pad = TrampolineUnder(player.transform.position);
        if (pad != null && !status.Dead && !Teleporting && !InCannon && !CannonFlying && localDig < 0
            && Time.time - bounceAt > BounceCooldown && player.VerticalSpeed <= 0.5f)
        {
            bounceAt = Time.time;
            player.Launch(TrampolineSpeed);
            Bounce(pad);
            net.ToHost("NZ");
        }
        // Les autres personnages qui rebondissent : même son, même animation (sans message réseau).
        foreach (var rp in net.Remotes.Values)
        {
            if (rp == null) continue;
            Beacon under = rp.Dead ? null : TrampolineUnder(rp.transform.position);
            bool on = under != null;
            bool was = remoteOnTrampoline.Contains(rp.PlayerId);
            if (on && !was && (!remoteBounceAt.TryGetValue(rp.PlayerId, out float t) || Time.time - t > BounceCooldown))
            {
                remoteBounceAt[rp.PlayerId] = Time.time;
                Bounce(under);
                rp.PlayBounce();
            }
            if (on) remoteOnTrampoline.Add(rp.PlayerId);
            else remoteOnTrampoline.Remove(rp.PlayerId);
        }
    }

    static void Bounce(Beacon pad)
    {
        pad.PlayPlaced();
        GameAudio.Play3D("spring", pad.transform.position + Vector3.up * GameRules.TrampolineHeight, 1f, 25f, 0.1f);
    }

    Beacon TrampolineUnder(Vector3 center)
    {
        Vector3 feet = center + Vector3.down * 0.9f;
        foreach (var b in beacons.Placed)
        {
            if (b == null || b.Kind != DeviceKind.Trampoline) continue;
            Vector3 d = feet - b.transform.position;
            if (new Vector2(d.x, d.z).magnitude < TrampolineRadius && d.y > -0.25f && d.y < GameRules.TrampolineHeight + 0.3f) return b;
        }
        return null;
    }

    // ---------- Cloche Décorative ----------

    // Posée, elle tinte : chaque joueur qui la voit (aucun mur entre eux) l'entend et en est obnubilé, la caméra forcée de la
    // fixer, jusqu'à ce qu'un mur les sépare.
    int bellObsession = -1;

    void RingBell(Beacon bell)
    {
        if (!net.InGame || status.Dead || !BellInSight(bell)) return;
        GameAudio.Play3D("bell_deco", BellPoint(bell), 1f, GameRules.BellSightRange, 0f);
        bellObsession = bell.Id;
    }

    static Vector3 BellPoint(Beacon bell) => bell.transform.position + Vector3.up * GameRules.BellHeight * 0.55f;

    bool BellInSight(Beacon bell)
    {
        Vector3 head = player.transform.position + Vector3.up * 0.6f;
        Vector3 point = BellPoint(bell);
        return Vector3.Distance(head, point) < GameRules.BellSightRange
            && !Physics.Linecast(head, point, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    Vector3? BellLook()
    {
        if (bellObsession < 0) return null;
        Beacon bell = beacons.Get(bellObsession);
        if (bell == null || status.Dead || Teleporting || !BellInSight(bell))
        {
            bellObsession = -1;
            return null;
        }
        return BellPoint(bell);
    }

    // ================= /give (hôte) =================

    // "/give 1" : une Croquette (ID du Terminal) ; "/give O4" : un Trampoline (autres objets). La Boussole s'active pour tous.
    public string Give(string arg)
    {
        if (!net.IsHost) return null;
        ItemDef def = Items.ByAnyId(arg);
        if (def == null) return "Aucun objet n'a l'ID " + arg.ToUpperInvariant() + ".";
        if (!def.inventory)
        {
            compass = true;
            BroadcastState();
            return "La Boussole s'affiche désormais chez tous les joueurs.";
        }
        if (!HasFreeSlot) return "Votre inventaire est plein ! Impossible de recevoir " + def.name + ".";
        status.Add(def.type);
        return "Vous avez reçu : " + def.name + ".";
    }

    // ================= Sujets de test (/info) =================

    // Critères (hôte, envoyés avec l'état de la partie) : /hello puis /step1. Non précisé dans le GDD : ils valent pour la
    // partie en cours (remis à zéro à chaque nouvelle partie) ; "20 Balises posées" compte celles posées en même temps.
    const int TestPogoSale = 1, TestHurt = 2, TestBeaconInSight = 4, TestTwentyBeacons = 8, TestNightmareSurvived = 16;
    const int TwentyBeacons = 20;
    const float NightmareApproach = 4f, NightmareSurvival = 10f;
    const int ThirdCycleRound = 4;
    int testFlags, sentFlags;
    float nearNightmareSince = -1f;

    public bool Step1Unlocked => (testFlags & (TestPogoSale | TestHurt | TestBeaconInSight)) == (TestPogoSale | TestHurt | TestBeaconInSight);

    void SetTestFlag(int flag)
    {
        if ((testFlags & flag) != 0) return;
        testFlags |= flag;
        BroadcastState();
    }

    // Joueur local : critère rempli de son côté (une seule fois par partie).
    void RaiseTestFlag(int flag)
    {
        if (((testFlags | sentFlags) & flag) != 0) return;
        sentFlags |= flag;
        net.ToHost("QF|" + flag);
    }

    // S'approcher à moins de 4 m d'un Cauchemar et survivre les 10 secondes suivantes.
    void UpdateTestSubjects()
    {
        if (status.Dead || !net.InGame) { nearNightmareSince = -1f; return; }
        if (((testFlags | sentFlags) & TestNightmareSurvived) != 0) return;
        if (nearNightmareSince >= 0f)
        {
            if (Time.time - nearNightmareSince >= NightmareSurvival) RaiseTestFlag(TestNightmareSurvived);
            return;
        }
        foreach (var e in entities.Values)
            if (e != null && e.Nightmare && Vector3.Distance(e.transform.position + Vector3.up * 0.9f, player.transform.position) < NightmareApproach)
            {
                nearNightmareSince = Time.time;
                return;
            }
    }

    static string Status(bool ok) => ok ? "OK." : "En attente.";

    public string InfoMenu() => "Sous-menu /info :\n/hello" + (Step1Unlocked ? "\n/step1" : "");

    public string HelloText() =>
        "Nouveaux sujets de test. Enregistrement des comportements étudiés.\n"
        + "Réaction au stimuli de récompense. " + Status((testFlags & TestPogoSale) != 0) + "\n"
        + "Réaction au stimuli de dangers létals en milieu hostile. " + Status((testFlags & TestHurt) != 0) + "\n"
        + "Compréhension du balisage neuronal. " + Status((testFlags & TestBeaconInSight) != 0);

    public string Step1Text() =>
        "Sujets de test confirmés. Démarrage de la procédure d'acquisition.\n"
        + "Assimilation de la récompense par exploration proactive. " + Status((testFlags & TestTwentyBeacons) != 0) + "\n"
        + "Assimilation du châtiment à l'approche du danger. " + Status((testFlags & TestNightmareSurvived) != 0) + "\n"
        + "Survie au premier cycle ternaire. " + Status(round >= ThirdCycleRound);

    // ================= Le Réveil =================

    // Hôte : il sort du monticule creusé.
    void SpawnReveil(Vector3 pos)
    {
        if (maze == null || phase != Phase.Running) return;
        var e = Entity.Create<Reveil>(entitiesRoot, nextEntityId++, pos, Random.Range(0f, 360f), maze.nav);
        entities[e.Id] = e;
    }

    // Explosion (hôte) : les joueurs dans le rayon (Hub compris) sont blessés et repoussés ; hors du Hub, les objets posés
    // ou à ramasser sont détruits.
    public void Explode(Entity entity, Vector3 center, float radius, int damage, float knockback)
    {
        foreach (var p in PlayerPresence.All)
            if (p != null && p.Alive && Vector3.Distance(p.Position, center) <= radius + 0.4f) Hit(entity, p.PlayerId, damage, knockback);
        if (WorldBuilder.IsInHub(center)) return;
        foreach (var b in new List<Beacon>(beacons.Placed))
        {
            if (b == null || b.IsHub || WorldBuilder.IsInHub(b.transform.position) || CannonOccupied(b.Id)) continue;
            if (Vector3.Distance(b.transform.position, center) > radius + 0.5f || !beaconMessages.Remove(b.Id)) continue;
            tvVideos.Remove(b.Id);
            net.ToAll("BZ|" + b.Id);
        }
        foreach (var kv in new List<KeyValuePair<int, ItemPickup>>(items))
        {
            if (kv.Value == null || picked.Contains(kv.Key) || WorldBuilder.IsInHub(kv.Value.transform.position)) continue;
            if (Vector3.Distance(kv.Value.transform.position, center) > radius + 0.5f) continue;
            picked.Add(kv.Key);
            net.ToAll("IR|" + round + "|" + kv.Key);
        }
    }

    // /spawn 10 : près de l'hôte s'il est dans le labyrinthe (sinon au hasard dans la Zone 1).
    string SpawnReveilNearHost(System.Random rnd)
    {
        if (!maze.nav.Ready) maze.nav.BuildAll();
        Vector3 me = player.transform.position;
        int node = WorldBuilder.IsInHub(me) ? -1 : maze.nav.RandomNode(Mathf.Max(0, maze.nav.ZoneAt(me)), rnd, me, 5f, 10f);
        if (node < 0) node = maze.nav.RandomNode(0, rnd, Vector3.zero, 30f);
        if (node < 0) return "Impossible pour le moment.";
        var e = Entity.Create<Reveil>(entitiesRoot, nextEntityId++, maze.nav.Position(node), (float)rnd.NextDouble() * 360f, maze.nav);
        entities[e.Id] = e;
        return "Un Réveil sonne quelque part dans la Zone 1...";
    }

    // ================= Le Roi : lumières affolées, flash =================

    // Les néons à moins de 30 m d'un Roi clignotent frénétiquement (chez chaque joueur) ; repéré par un Roi, le joueur reçoit
    // un flash lumineux sur son écran.
    readonly HashSet<int> roiHunting = new HashSet<int>();

    void UpdateRoiEffects()
    {
        FlickerLight.Frenzy.Clear();
        foreach (var e in entities.Values)
        {
            if (!(e is Roi roi)) continue;
            FlickerLight.Frenzy.Add(roi.transform.position);
            bool hunting = roi.Current >= Roi.State.Aim && roi.Current <= Roi.State.Recover && roi.TargetId == net.MyId && !status.Dead;
            if (hunting && roiHunting.Add(roi.Id)) hud.FlashWhite();
            else if (!hunting) roiHunting.Remove(roi.Id);
        }
    }

    // ================= Réception =================

    bool HandleObjectMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "TQ" when host && f.Length >= 3:
            {
                int id = NetworkGame.I(f[1]);
                Beacon tv = beacons.Get(id);
                string video = YouTubeTv.VideoId("youtu.be/" + f[2]);
                if (tv == null || tv.Kind != DeviceKind.Television || !tv.Charged || video == null) return true;
                tvVideos[id] = (video, Time.time);
                net.ToAll(TvMsg(id));
                return true;
            }
            case "TV" when f.Length >= 4:
            {
                Beacon tv = beacons.Get(NetworkGame.I(f[1]));
                if (tv != null && tv.Screen != null) tv.Screen.SetVideo(f[2], NetworkGame.P(f[3]));
                return true;
            }
            case "QF" when host && f.Length >= 2:
            {
                int flag = NetworkGame.I(f[1]);
                if (flag == TestHurt || flag == TestNightmareSurvived) SetTestFlag(flag);
                return true;
            }
        }
        return false;
    }

    // Joueur qui arrive : vidéos en cours des Télévisions.
    void SendObjectSnapshot(string to)
    {
        foreach (int id in tvVideos.Keys) net.ToPeer(to, TvMsg(id));
    }
}
