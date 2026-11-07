using System.Collections.Generic;
using UnityEngine;

// V0.6 : remaniement du terrain. Les zones se génèrent au fil de l'avancée des joueurs (la zone où ils se trouvent et les
// zones voisines) ; un Hub toutes les deux zones ; portes à orbes ; glyphes de la Neutral Room et glyphes d'acquisition ;
// Sanction (La Tête de Cloche) ; Chenil où l'on peut se réfugier.
// Messages : GY|glyphe|balise (-1 : déchargé)                    hôte -> tous : glyphe d'acquisition chargé par cette Balise
//            GX|type|glyphe|écoulé (glyphe -1 : fin)              hôte -> tous : ordre d'un glyphe en cours
//            NG|zone|type -> NP|zone|type                         glyphe neutre touché (entendu dans la Neutral Room)
//            SN                                                   joueur -> hôte : ordre non respecté, Sanction
public partial class GameSession
{
    // Zones générées (index 0 : Zone 1) ; nombre de zones à générer (fixé par l'hôte, au moins 2 : la Zone 1 et sa voisine).
    readonly List<WorldBuilder.ZoneRefs> zones = new List<WorldBuilder.ZoneRefs>();
    int zoneCount = 2;
    // /tp N : au-delà, la génération prendrait trop longtemps.
    const int MaxTeleportZone = 30;
    float zoneTimer;
    // Zones dont les entités de la manche sont apparues (Normal, Cauchemar) ; retirées si leurs entités sont supprimées.
    readonly HashSet<int> normalsSpawned = new HashSet<int>(), nightmaresSpawned = new HashSet<int>();

    // ================= Zones et Hubs =================

    void GenerateNextZone()
    {
        int index = zones.Count + 1;
        var zone = WorldBuilder.BuildZone(worldRoot, seed, index, index > 1 ? zones[index - 2] : null);
        zones.Add(zone);
        // Le Hub N est au sud de la zone 2N - 1.
        if (index > 1 && index % 2 == 1) AddHubFixtures(WorldBuilder.BuildHub(worldRoot, (index + 1) / 2));
        if (itemsRound == round) SpawnZoneItems(zone);
    }

    // Partie quittée ou nouvelle graine : seuls restent le Hub 1 et ses équipements.
    void DestroyZones()
    {
        foreach (var z in zones) if (z.root != null) Destroy(z.root);
        zones.Clear();
        for (int i = hubs.Count - 1; i >= 1; i--)
        {
            if (hubs[i].root != null) Destroy(hubs[i].root.gameObject);
            hubs.RemoveAt(i);
            buttons.RemoveAt(i);
            skipButtons.RemoveAt(i);
            pogos.RemoveAt(i);
        }
        beacons.RemoveHubsAbove(1);
        foreach (var list in kennels.Values)
            for (int i = list.Count - 1; i >= 1; i--)
            {
                if (list[i] != null) Destroy(list[i].gameObject);
                list.RemoveAt(i);
            }
        ClearGlyphState();
    }

    // Équipements d'un Hub : Bouton Rouge, Bouton Skip, Terminal, Pogo, Balise principale, Chenils (identiques au Hub 1).
    void AddHubFixtures(WorldBuilder.HubRefs h)
    {
        hubs.Add(h);
        buttons.Add(RedButton.Create(h.buttonAnchor, h.index));
        var skip = SkipButton.Create(h.skipAnchor, h.index);
        skip.gameObject.SetActive(CanSkipRound);
        skipButtons.Add(skip);
        TerminalStation.Create(h.terminalAnchor);
        var p = Pogo.Create(h.pogoAnchor);
        pogos.Add(p);
        if (h.index > 1)
        {
            beacons.AddHub(h.index, h.beaconPosition, h.beaconYaw);
            p.SetState(pogos[0].State, false, "");
            OnRosterChanged();
            UpdateScreens();
        }
        h.hubProbe.RenderProbe();
    }

    WorldBuilder.HubRefs NearestHub(Vector3 p)
    {
        WorldBuilder.HubRefs best = hub;
        float bestDist = float.MaxValue;
        foreach (var h in hubs)
        {
            float d = (h.center - p).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = h; }
        }
        return best;
    }

    Pogo NearestPogo()
    {
        Pogo best = pogos[0];
        float bestDist = float.MaxValue;
        foreach (var p in pogos)
        {
            if (p == null) continue;
            float d = (p.transform.position - player.transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    // Collisions du bâti d'une zone (sols, murs, rampes), sans le décor.
    bool IsStructure(Transform t)
    {
        foreach (var z in zones) if (t.IsChildOf(z.structure)) return true;
        return false;
    }

    // Nouvelle partie : orbes, Hubs et portes de chapiteau refermés ; la Zone 1 et la Zone 2 à générer.
    void ClearProgress()
    {
        brokenOrbs.Clear();
        openedHubs.Clear();
        openedAcquisitions.Clear();
        zoneCount = 2;
        normalsSpawned.Clear();
        nightmaresSpawned.Clear();
    }

    // Porte numéro 'door' (entre la zone 2 x door - 1 et la zone 2 x door) dont fait partie l'orbe ; 0 si sa zone n'existe pas.
    int DoorOfOrb(int orb)
    {
        if (orb < 0) return 0;
        int door = orb < GameRules.OrbsOfDoor(1) ? 1 : 2 + (orb - GameRules.FirstOrb(2)) / GameRules.OrbsOfDoor(2);
        return door * 2 - 1 <= zones.Count ? door : 0;
    }

    bool DoorOpen(int door)
    {
        int first = GameRules.FirstOrb(door);
        for (int k = 0; k < GameRules.OrbsOfDoor(door); k++) if (!brokenOrbs.Contains(first + k)) return false;
        return true;
    }

    // Zone accessible : la Zone 1, une zone paire dont la porte à orbes est ouverte, une zone impaire dont le Hub a ouvert
    // sa porte.
    bool ZoneUnlocked(int zone) => zone == 1 || (zone % 2 == 0 ? DoorOpen(zone / 2) : openedHubs.Contains((zone + 1) / 2));

    // Zone où se trouve un joueur, ou voisine (9.2 : on ne garde d'entités que là).
    bool ZoneActive(int zone)
    {
        bool any = false;
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || !p.isActiveAndEnabled) continue;
            int z = Mathf.Max(1, WorldBuilder.ZoneAt(p.Position));
            any = true;
            if (Mathf.Abs(z - zone) <= 1) return true;
        }
        return !any && zone <= 2;
    }

    // Orbes (activés au début des manches, brisés par une Clé), portes à orbes, portes des chapiteaux, portes des Hubs 2+.
    void UpdateDoors(bool animate)
    {
        foreach (var z in zones)
        {
            if (z.door != null)
            {
                int first = GameRules.FirstOrb(z.doorNumber);
                for (int k = 0; k < z.orbs.Length; k++)
                    z.orbs[k].SetState(phase != Phase.GameOver && GameRules.OrbActive(first + k, round), brokenOrbs.Contains(first + k), animate);
                bool open = DoorOpen(z.doorNumber);
                if (open != z.door.IsOpen || !animate) z.door.SetOpen(open, !animate);
            }
            if (z.acquisitionDoor != null)
            {
                bool open = openedAcquisitions.Contains(z.index);
                if (open != z.acquisitionDoor.IsOpen || !animate) z.acquisitionDoor.SetOpen(open, !animate);
            }
        }
        for (int i = 1; i < hubs.Count; i++)
        {
            bool open = openedHubs.Contains(hubs[i].index);
            hubs[i].door.SetOpen(open, !animate);
            buttons[i].SetRaised(!open, animate);
        }
    }

    // Hôte : génération des zones (celle des joueurs et la suivante), grilles de navigation, entités de la manche et
    // suppression des entités trop loin des joueurs (9.2, Optimisation et Performance).
    void UpdateHostZones()
    {
        // La grille de navigation se calcule en arrière-plan, quelques colonnes par image, une zone à la fois.
        foreach (var z in zones)
            if (!z.nav.Ready && ZoneActive(z.index)) { z.nav.BuildStep(6); break; }
        zoneTimer -= Time.unscaledDeltaTime;
        if (zoneTimer > 0f) return;
        zoneTimer = 0.5f;
        int furthest = 1;
        foreach (var p in PlayerPresence.All)
            if (p != null && p.isActiveAndEnabled) furthest = Mathf.Max(furthest, WorldBuilder.ZoneAt(p.Position));
        if (furthest + 1 > zoneCount)
        {
            zoneCount = furthest + 1;
            ApplyWorld();
            BroadcastState();
        }
        if (phase != Phase.Running) return;
        CullEntities();
        PopulateZones();
    }

    // Début de manche : les entités de la manche précédente disparaissent pour laisser place à d'autres.
    void StartRoundEntities()
    {
        ClearEntities();
        normalsSpawned.Clear();
        nightmaresSpawned.Clear();
        if (phase == Phase.Running) PopulateZones();
    }

    // V0.6 : à chaque manche, 2 entités de type Normal et 2 de type Cauchemar par zone (proche des joueurs, grille prête).
    // Manche 1 : les Normal après 2 minutes de jeu, et un seul Cauchemar, dans la Zone 1, après 3 minutes.
    void PopulateZones()
    {
        float played = GameRules.RoundDuration - timeLeft;
        bool first = round <= GameRules.LateSpawnRound;
        foreach (var z in zones)
        {
            if (!ZoneActive(z.index) || !z.nav.Ready) continue;
            var rnd = new System.Random(seed * 17 + round * 613 + z.index * 7907 + nextEntityId * 131);
            if ((!first || played >= GameRules.LateNormalsDelay) && normalsSpawned.Add(z.index))
                for (int k = 0; k < GameRules.NormalsPerZone; k++) SpawnNormal(z.index, rnd);
            bool nightmares = !first || z.index == 1 && played >= GameRules.LateNightmareDelay;
            if (nightmares && nightmaresSpawned.Add(z.index))
                for (int k = 0, n = first ? 1 : GameRules.NightmaresPerZone; k < n; k++) SpawnNightmare(z.index, rnd);
        }
    }

    // Entités au-delà des zones des joueurs et de leurs voisines : supprimées (elles reviendront si des joueurs approchent).
    void CullEntities()
    {
        foreach (var e in new List<Entity>(entities.Values))
        {
            if (e == null || e.Zone <= 0 || e is TeteDeCloche || ZoneActive(e.Zone)) continue;
            normalsSpawned.Remove(e.Zone);
            nightmaresSpawned.Remove(e.Zone);
            entities.Remove(e.Id);
            watching.Remove(e.Id);
            Destroy(e.gameObject);
        }
    }

    // ================= Glyphes =================

    // Hôte : Balise qui charge chaque glyphe d'acquisition. Tous : ordres en cours (début, glyphe qui les a déclenchés).
    readonly Dictionary<int, int> glyphSources = new Dictionary<int, int>();
    readonly Dictionary<int, LineRenderer> glyphBeams = new Dictionary<int, LineRenderer>();
    class Order
    {
        public int glyph;
        public float start;          // Time.time chez ce joueur
        public bool sanctioned;      // joueur local déjà sanctionné pour cet ordre
        public AudioSource sound;
    }
    readonly Order[] orders = new Order[GameRules.GlyphTypes];
    float glyphTimer, lastBarkAt = -100f;
    static readonly string[] OrderTexts = { "Assis !", "A la niche !", "Aboie !" };

    Glyph FindGlyph(int id)
    {
        int zone = id / 100;
        if (zone < 1 || zone > zones.Count) return null;
        foreach (var g in zones[zone - 1].acquisitionGlyphs) if (g.Id == id) return g;
        foreach (var g in zones[zone - 1].neutralGlyphs) if (g.Id == id) return g;
        return null;
    }

    bool OrderActive(int type) => orders[type] != null && Time.time - orders[type].start < GameRules.GlyphDuration[type];

    // Hôte : un glyphe d'acquisition est chargé par la Balise (ou Balise Améliorée) chargée la plus proche qui le voit.
    void UpdateHostGlyphs()
    {
        glyphTimer -= Time.unscaledDeltaTime;
        if (glyphTimer > 0f) return;
        glyphTimer = 0.4f;
        foreach (var z in zones)
        {
            if (z.acquisitionGlyphs.Count == 0) continue;
            foreach (var g in z.acquisitionGlyphs)
            {
                int source = -1;
                float best = float.MaxValue;
                foreach (var b in beacons.Placed)
                {
                    if (b == null || !b.Relays || !b.Charged) continue;
                    float d = (b.Top - g.Anchor).sqrMagnitude;
                    if (d < best && beacons.InSight(b.Top, g.Anchor)) { best = d; source = b.Id; }
                }
                glyphSources.TryGetValue(g.Id, out int before);
                if (!glyphSources.ContainsKey(g.Id)) before = -1;
                if (source == before) continue;
                glyphSources[g.Id] = source;
                net.ToAll("GY|" + g.Id + "|" + source);
                if (source >= 0 && before < 0) OnGlyphCharged(g);
                else if (source < 0) OnGlyphDischarged(g);
            }
            // Tous les glyphes de la zone chargés : la porte du chapiteau s'ouvre, pour de bon.
            if (!openedAcquisitions.Contains(z.index) && z.acquisitionGlyphs.TrueForAll(g => glyphSources.TryGetValue(g.Id, out int s) && s >= 0))
            {
                openedAcquisitions.Add(z.index);
                Log("Tous les glyphes de la Zone {0} sont chargés : la porte du chapiteau s'ouvre !", z.index.ToString());
                UpdateDoors(true);
                BroadcastState();
            }
        }
    }

    // Hôte : son ordre s'impose à tous (un seul ordre en cours par type de glyphe), pendant une manche.
    void OnGlyphCharged(Glyph g)
    {
        if (phase != Phase.Running || OrderActive(g.Type)) return;
        net.ToAll("GX|" + g.Type + "|" + g.Id + "|0");
    }

    void OnGlyphDischarged(Glyph g)
    {
        if (orders[g.Type] != null && orders[g.Type].glyph == g.Id) EndOrder(g.Type);
    }

    // Hôte : son glyphe s'est déchargé avant la fin, l'ordre cesse chez tous.
    void EndOrder(int type) => net.ToAll("GX|" + type + "|-1|0");

    void StopOrder(int type)
    {
        var order = orders[type];
        orders[type] = null;
        if (order?.sound != null) Destroy(order.sound.gameObject);
    }

    // Glyphe neutre touché : son seul, entendu de tous les joueurs présents dans la Neutral Room.
    public void TouchGlyph(Glyph g)
    {
        if (!g.Neutral) return;
        Gesture(PlayerGesture.Use);
        net.ToHost("NG|" + g.Zone + "|" + g.Type);
    }

    void ResetGlyphs()
    {
        for (int t = 0; t < orders.Length; t++) StopOrder(t);
        ClearGlyphState();
        hud.SetOrders(null);
    }

    void ClearGlyphState()
    {
        glyphSources.Clear();
        foreach (var beam in glyphBeams.Values) if (beam != null) Destroy(beam.gameObject);
        glyphBeams.Clear();
        for (int t = 0; t < orders.Length; t++) StopOrder(t);
    }

    void SendGlyphSnapshot(string to)
    {
        foreach (var kv in glyphSources) if (kv.Value >= 0) net.ToPeer(to, "GY|" + kv.Key + "|" + kv.Value);
        for (int t = 0; t < orders.Length; t++)
            if (OrderActive(t)) net.ToPeer(to, "GX|" + t + "|" + orders[t].glyph + "|" + NetworkGame.F(Time.time - orders[t].start));
    }

    // Tous : lueur du glyphe, glyphe allumé sur la porte du chapiteau, rayon d'énergie vers la Balise.
    void SetGlyphSource(int id, int source)
    {
        glyphSources[id] = source;
        Glyph g = FindGlyph(id);
        if (g == null) return;
        g.SetCharged(source >= 0);
        var zone = zones[g.Zone - 1];
        if (zone.acquisitionDoor != null) zone.acquisitionDoor.SetGlyphLit(zone.acquisitionGlyphs.IndexOf(g), source >= 0);
    }

    // Joueur local : rayons des glyphes chargés, ordres affichés et respectés (sinon, la Sanction).
    void UpdateGlyphOrders()
    {
        foreach (var kv in glyphSources)
        {
            Glyph g = kv.Value >= 0 ? FindGlyph(kv.Key) : null;
            Beacon b = g != null ? beacons.Get(kv.Value) : null;
            glyphBeams.TryGetValue(kv.Key, out var line);
            if (b == null)
            {
                if (line != null && line.gameObject.activeSelf) line.gameObject.SetActive(false);
                continue;
            }
            if (line == null) glyphBeams[kv.Key] = line = beacons.NewBeam();
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            // L'énergie circule de la Balise vers le glyphe.
            line.SetPosition(0, b.Top);
            line.SetPosition(1, g.Anchor);
        }

        var texts = new List<string>();
        for (int t = 0; t < orders.Length; t++)
        {
            var order = orders[t];
            if (order == null) continue;
            float elapsed = Time.time - order.start;
            if (elapsed >= GameRules.GlyphDuration[t])
            {
                // "A la niche !" : à la fin des 30 secondes, il faut être dans sa niche. Puis l'ordre cesse (chez chacun).
                if (t == 1) CheckOrder(order, InKennel);
                StopOrder(t);
                continue;
            }
            texts.Add(Loc.T(OrderTexts[t]));
            if (t == 0 && elapsed >= GameRules.GlyphSitGrace)
                CheckOrder(order, InKennel || player.Crouched && presence.Still);
            else if (t == 2)
                CheckOrder(order, InKennel || Time.time - Mathf.Max(lastBarkAt, order.start) <= GameRules.GlyphBarkGap);
        }
        hud.SetOrders(texts);
    }

    void CheckOrder(Order order, bool obeyed)
    {
        if (obeyed || order.sanctioned || status.Dead || phase != Phase.Running || !net.InGame) return;
        order.sanctioned = true;
        net.ToHost("SN");
    }

    // ================= Sanction : La Tête de Cloche =================

    // Hôte : elle apparaît à 30 m du joueur sanctionné (une seule à ses trousses à la fois).
    bool SpawnTete(string playerId)
    {
        PlayerPresence prey = null;
        foreach (var p in PlayerPresence.All) if (p != null && p.PlayerId == playerId) prey = p;
        if (prey == null || !prey.Alive || zones.Count == 0) return false;
        foreach (var e in entities.Values) if (e is TeteDeCloche t && t.TargetId == playerId && t.Current == TeteDeCloche.State.Hunt) return false;
        int zone = Mathf.Clamp(WorldBuilder.ZoneAt(prey.Position), 1, zones.Count);
        MazeNav nav = zones[zone - 1].nav;
        if (!nav.Ready) nav.BuildAll();
        var rnd = new System.Random(seed + nextEntityId * 389 + Mathf.RoundToInt(Time.time * 10f));
        float d = GameRules.TeteSpawnDistance;
        int node = nav.RandomNode(0, rnd, prey.Position, d - 4f, d + 4f);
        if (node < 0 || WorldBuilder.IsInHub(nav.Position(node))) node = nav.RandomNode(0, rnd, prey.Position, 15f);
        if (node < 0) return false;
        var tete = WatchTete(Entity.Create<TeteDeCloche>(entitiesRoot, nextEntityId++, nav.Position(node), (float)rnd.NextDouble() * 360f, nav));
        tete.Zone = zone;
        tete.Setup(playerId);
        entities[tete.Id] = tete;
        return true;
    }

    // Chaque coup de marteau sur sa cloche coûte 20 de sérénité aux joueurs qu'elle obnubile (note 07 du chapitre 14).
    TeteDeCloche WatchTete(TeteDeCloche tete)
    {
        tete.Rang += () =>
        {
            if (tete != null && tete.Obsesses(net.MyId)) LoseBellSerenity();
        };
        return tete;
    }

    // ================= Chenil (note 05 du chapitre 4) =================

    // Dans sa niche : caméra à l'intérieur, tournée vers l'extérieur (on peut la bouger un peu) ; le personnage est
    // invincible, aucune entité ne peut le prendre pour cible (les poursuites s'arrêtent) et il ne peut pas y mourir.
    // Un clic (interagir) ou un saut l'en fait sortir.
    static Kennel kennelInside;
    float kennelEnteredAt;
    public static bool InKennel => kennelInside != null;
    // Caché aux yeux des autres et des entités : dans un Canon ou dans sa niche.
    public static bool Hidden => InCannon || InKennel;

    public void EnterKennel(Kennel k)
    {
        if (k == null || InKennel || status.Dead || Teleporting || InCannon || CannonFlying || localDig >= 0 || phase == Phase.GameOver) return;
        kennelInside = k;
        kennelEnteredAt = Time.time;
        EndTeleport(false);
        terminal.Close();
        postItUI.Close();
        player.SetFrozen(true);
        player.Teleport(k.transform.position + Vector3.up * 0.95f, k.transform.eulerAngles.y);
        avatar.SetHidden(true);
        OrbitCamera.KennelEye = k.Eye;
        OrbitCamera.KennelYaw = k.transform.eulerAngles.y;
        GameAudio.Play3D("pop", k.transform.position, 0.7f, 15f, 0.1f, 0.7f);
        Gesture(PlayerGesture.Use);
    }

    void UpdateKennel()
    {
        if (!InKennel) return;
        if (status.Dead || kennelInside == null || phase == Phase.GameOver) { LeaveKennel(!status.Dead); return; }
        if (Time.time - kennelEnteredAt < 0.3f || TerminalUI.IsOpen || PostItUI.IsOpen || ControlsUI.IsOpen) return;
        if (Controls.Pressed(GameAction.Interact) || Controls.Down(GameAction.Jump)) LeaveKennel(true);
    }

    // Sortie : devant la niche, face à l'extérieur.
    void LeaveKennel(bool placeOutside)
    {
        if (kennelInside == null) return;
        Kennel k = kennelInside;
        kennelInside = null;
        OrbitCamera.KennelEye = null;
        if (player == null) return;
        avatar.SetHidden(false);
        player.SetFrozen(false);
        if (placeOutside && k != null)
        {
            Vector3 p = k.transform.position + k.transform.forward * 1.3f;
            player.Teleport(new Vector3(p.x, k.transform.position.y + 0.95f, p.z), k.transform.eulerAngles.y);
        }
    }

    // ================= Godmod : Balise Améliorée posée d'un clic (V0.6) =================

    // Case d'inventaire vide sélectionnée : un clic sur le sol pose une Balise Améliorée (sans rien retirer de l'inventaire).
    public void GodPlaceEnhanced()
    {
        if (!Godmode || !net.IsHost) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, 14f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            || hit.normal.y <= 0.7f || hit.collider.GetComponentInParent<Interactable>() != null
            || Vector3.Distance(hit.point, player.transform.position) > GameRules.InteractRange + 1.5f) return;
        Gesture(PlayerGesture.Place);
        Vector3 back = player.transform.position - hit.point;
        float yaw = back.sqrMagnitude > 0.01f ? Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg : 0f;
        net.ToHost("BN|" + NetworkGame.F(hit.point.x) + "|" + NetworkGame.F(hit.point.y) + "|" + NetworkGame.F(hit.point.z) + "|" + (int)DeviceKind.Enhanced
            + "|" + NetworkGame.F(yaw));
    }

    // ================= Réception =================

    bool HandleZoneMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "GY" when f.Length >= 3:
                SetGlyphSource(NetworkGame.I(f[1]), NetworkGame.I(f[2]));
                return true;
            case "GX" when f.Length >= 4:
            {
                int type = NetworkGame.I(f[1]), glyph = NetworkGame.I(f[2]);
                if (type < 0 || type >= orders.Length) return true;
                StopOrder(type);
                if (glyph < 0) return true;
                var order = new Order { glyph = glyph, start = Time.time - NetworkGame.P(f[3]) };
                // Le son du glyphe, partout, en boucle, pendant toute la durée de l'ordre.
                order.sound = GameAudio.Loop2D(Glyph.Sounds[type], 0.85f);
                orders[type] = order;
                return true;
            }
            case "NG" when host && f.Length >= 3:
                net.ToAll("NP|" + f[1] + "|" + f[2]);
                return true;
            case "NP" when f.Length >= 3:
            {
                int zone = NetworkGame.I(f[1]), type = NetworkGame.I(f[2]);
                if (zone < 1 || zone > zones.Count || type < 0 || type >= Glyph.Sounds.Length) return true;
                if (zones[zone - 1].neutralRoom.Contains(player.transform.position)) GameAudio.Play2D(Glyph.Sounds[type], 0.9f);
                return true;
            }
            case "SN" when host:
                SpawnTete(from);
                return true;
        }
        return false;
    }
}
