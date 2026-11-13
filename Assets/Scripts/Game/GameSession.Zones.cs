using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// V0.6 : remaniement du terrain. Les zones se génèrent au fil de l'avancée des joueurs (la zone où ils se trouvent et les
// zones voisines) ; un Hub toutes les deux zones ; glyphes neutres sous boîtier de verre (V0.6.5) et glyphes d'acquisition ;
// Sanction (La Tête de Cloche) ; Chenil où l'on peut se réfugier.
// Messages : GY|glyphe|balise (-1 : déchargé)                    hôte -> tous : glyphe d'acquisition chargé par cette Balise
//            GX|type|glyphe|écoulé (glyphe -1 : fin)              hôte -> tous : ordre d'un glyphe en cours
//            NG|glyphe -> NP|zone|type                            glyphe neutre actionné (entendu par les joueurs de la zone)
//            SN                                                   joueur -> hôte : ordre non respecté, Sanction
public partial class GameSession
{
    // Zones générées (index 0 : Zone 1) ; nombre de zones à générer (fixé par l'hôte, au moins 2 : la Zone 1 et sa voisine).
    readonly List<WorldBuilder.ZoneRefs> zones = new List<WorldBuilder.ZoneRefs>();
    int zoneCount = GameRules.InitialZones;
    // Zone la plus lointaine atteinte par les joueurs au cours de la partie (hôte, V0.6.3 : chance du Jeton WOOF).
    int lastZone = 1;
    // /tp N : au-delà, la génération prendrait trop longtemps.
    const int MaxTeleportZone = 30;
    float zoneTimer;
    // Chapitre 9.2 (V0.6.5) : dernière zone fermée pour de bon (0 : aucune) ; zones déjà désactivées.
    int sealedZones, deactivatedZones;
    // Zones dont les entités de la manche sont apparues (Normal, Cauchemar) ; retirées si leurs entités sont supprimées.
    readonly HashSet<int> normalsSpawned = new HashSet<int>(), nightmaresSpawned = new HashSet<int>();

    // ================= Zones et Hubs =================

    // Génération en cours, étalée sur plusieurs images : au plus ce temps par image (bien plus derrière l'écran de chargement).
    const long ZoneBuildBudgetMs = 4, ZoneLoadingBudgetMs = 33;
    WorldBuilder.ZoneBuild zoneBuild;
    readonly Stopwatch zoneBuildWatch = new Stopwatch(), navBuildWatch = new Stopwatch();
    // Grille de navigation d'une zone (hôte) : au plus ce temps par image.
    const long NavBuildBudgetMs = 3;

    void StartNextZone()
    {
        int index = zones.Count + 1;
        zoneBuild = WorldBuilder.StartZoneBuild(worldRoot, seed, index, index > 1 ? zones[index - 2] : null);
    }

    // À chaque image : avance la génération des zones qui manquent (les joueurs sont encore loin d'elles).
    void AdvanceZoneBuild()
    {
        if (zoneBuild == null)
        {
            if (zones.Count >= zoneCount) return;
            StartNextZone();
        }
        zoneBuildWatch.Restart();
        do
        {
            if (zoneBuild.Steps.MoveNext()) { zoneSteps++; continue; }
            CompleteZoneBuild();
            // Ce qui a été reçu pendant sa génération (boîtiers brisés, portes ouvertes).
            UpdateDoors(true);
            return;
        }
        while (zoneBuildWatch.ElapsedMilliseconds < (worldLoading ? ZoneLoadingBudgetMs : ZoneBuildBudgetMs));
    }

    // Tout de suite (début de partie, /tp) : termine la zone en cours et génère celles qui manquent.
    void GenerateZonesNow()
    {
        while (zones.Count < zoneCount)
        {
            if (zoneBuild == null) StartNextZone();
            while (zoneBuild.Steps.MoveNext()) { }
            CompleteZoneBuild();
        }
    }

    void CompleteZoneBuild()
    {
        WorldBuilder.ZoneRefs zone = zoneBuild.Refs;
        int index = zones.Count + 1;
        zoneBuild = null;
        // Étapes de cette zone : estimation pour la barre de chargement de la suivante.
        if (zoneSteps > 0) stepsPerZone = zoneSteps;
        zoneSteps = 0;
        zones.Add(zone);
        RegisterArcades(zone.arcades);
        // Le Hub N est au sud de la zone 2N - 1.
        if (index > 1 && index % 2 == 1) AddHubFixtures(WorldBuilder.BuildHub(worldRoot, (index + 1) / 2));
        if (itemsRound == round) SpawnZoneItems(zone);
        RefreshGlyphPaws();
        // Glyphes chargés reçus pendant sa génération.
        foreach (var kv in new List<KeyValuePair<int, int>>(glyphSources))
            if (kv.Key / 100 == index) SetGlyphSource(kv.Key, kv.Value);
    }

    // Partie quittée ou nouvelle graine : seuls restent le Hub 1 et ses équipements.
    void DestroyZones()
    {
        // Génération abandonnée (nouvelle graine).
        if (zoneBuild != null && zoneBuild.Refs != null && zoneBuild.Refs.root != null) Destroy(zoneBuild.Refs.root);
        zoneBuild = null;
        foreach (var z in zones)
        {
            foreach (var m in z.arcades) UnregisterArcade(m);
            // Une porte de chapiteau scellée a quitté sa zone.
            if (z.acquisitionDoor != null) Destroy(z.acquisitionDoor.gameObject);
            if (z.root != null) Destroy(z.root);
        }
        zones.Clear();
        deactivatedZones = 0;
        SetHubsActive(1, hubs.Count, true);
        beacons.SealHubs(0);
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
            p.SetKills(pogos[0].Kills);
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

    // Nouvelle partie : boîtiers, glyphes, Hubs et portes de chapiteau refermés ; la Zone 1 et la Zone 2 à générer.
    void ClearProgress()
    {
        ResetSecret();
        brokenCases.Clear();
        activatedGlyphs.Clear();
        sealedZones = 0;
        openedHubs.Clear();
        openedAcquisitions.Clear();
        brokenBridges.Clear();
        itemZones = 2;
        zoneCount = GameRules.InitialZones;
        lastZone = 1;
        normalsSpawned.Clear();
        nightmaresSpawned.Clear();
    }

    // Porte numéro 'door' (entre la zone 2 x door - 1 et la zone 2 x door) : ouverte quand ses trois glyphes neutres sont actionnés.
    bool DoorOpen(int door)
    {
        for (int t = 0; t < GameRules.GlyphTypes; t++)
            if (!activatedGlyphs.Contains(GameRules.NeutralGlyphId(door * 2 - 1, t))) return false;
        return true;
    }

    // Zone accessible : la Zone 1, une zone paire dont la porte de la Neutral Room est ouverte, une zone impaire dont le Hub a ouvert
    // sa porte.
    bool ZoneUnlocked(int zone) => zone == 1 || (zone % 2 == 0 ? DoorOpen(zone / 2) : openedHubs.Contains((zone + 1) / 2));

    // Zone où se trouve un joueur, ou voisine (9.2 : on ne garde d'entités que là).
    bool ZoneActive(int zone)
    {
        if (zone <= sealedZones) return false;
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

    // Glyphes neutres, portes des Neutral Rooms, portes des chapiteaux (refermées si leur zone est scellée), portes des Hubs 2+.
    void UpdateDoors(bool animate)
    {
        foreach (var z in zones)
        {
            foreach (var g in z.neutralGlyphs) g.SetNeutralState(brokenCases.Contains(g.Id), activatedGlyphs.Contains(g.Id), animate);
            foreach (var b in z.bridges) b.SetBroken(brokenBridges.Contains(b.Id), animate);
            if (z.door != null)
            {
                bool open = DoorOpen(z.doorNumber);
                if (open != z.door.IsOpen || !animate) z.door.SetOpen(open, !animate);
            }
            if (z.acquisitionDoor != null)
            {
                bool open = openedAcquisitions.Contains(z.index) && z.index > sealedZones;
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
            if (!z.nav.Ready && ZoneActive(z.index))
            {
                navBuildWatch.Restart();
                do z.nav.BuildStep(1);
                while (!z.nav.Ready && navBuildWatch.ElapsedMilliseconds < (worldLoading ? ZoneLoadingBudgetMs : NavBuildBudgetMs));
                break;
            }
        zoneTimer -= Time.unscaledDeltaTime;
        if (zoneTimer > 0f) return;
        zoneTimer = 0.5f;
        int furthest = 1;
        foreach (var p in PlayerPresence.All)
            if (p != null && p.isActiveAndEnabled) furthest = Mathf.Max(furthest, WorldBuilder.ZoneAt(p.Position));
        lastZone = Mathf.Max(lastZone, furthest);
        UpdateSealing();
        // Les monticules n'apparaissent qu'autour des zones atteintes, même si les suivantes sont déjà générées.
        if (furthest + 1 > itemZones)
        {
            itemZones = furthest + 1;
            ApplyWorld();
            BroadcastState();
        }
        if (furthest + 1 > zoneCount)
        {
            zoneCount = furthest + 1;
            ApplyWorld();
            BroadcastState();
        }
        PlaceHangingBells();
        if (phase != Phase.Running) return;
        CullEntities();
        PopulateZones();
    }

    // Hôte (V0.6.13) : les Cloches Décoratives suspendues des zones 2+, une fois par partie et par zone générée.
    readonly HashSet<int> bellZones = new HashSet<int>();

    void PlaceHangingBells()
    {
        if (worldLoading || phase == Phase.GameOver) return;
        foreach (var z in zones)
        {
            if (z.index < 2 || z.index > Items.MaxBellZone || z.index <= sealedZones || !bellZones.Add(z.index)) continue;
            var rnd = new System.Random(seed * 31 + z.index * 977);
            foreach (var (pos, chain) in z.bellSpots)
                PlaceDevice((DeviceKind)((int)DeviceKind.BellZone + z.index), pos, (float)rnd.NextDouble() * 360f, "", false, chain);
        }
    }

    // ================= Fermeture des zones trop en arrière (chapitre 9.2, V0.6.5) =================

    // Hôte : dès la Zone 7, chaque zone impaire X débloquée referme, une fois tous les joueurs en vie à partir de la zone X - 4,
    // la porte d'entrée du chapiteau entre les zones X - 5 et X - 4.
    void UpdateSealing()
    {
        if (phase == Phase.GameOver) return;
        int target = sealedZones;
        for (int x = GameRules.FirstSealingZone; x <= Mathf.Max(lastZone, zones.Count); x += 2)
        {
            if (!ZoneUnlocked(x) && lastZone < x) break;
            if (AllPlayersFrom(x - 4)) target = Mathf.Max(target, x - 5);
        }
        if (target <= sealedZones) return;
        sealedZones = target;
        // Les appareils posés de l'autre côté de la porte ne servent plus (leurs liens ne doivent pas traverser le vide).
        foreach (var b in new List<Beacon>(beacons.Placed))
        {
            if (b == null || b.IsHub) continue;
            int zone = WorldBuilder.ZoneAt(b.transform.position);
            if (zone < 1 || zone > sealedZones || !beaconMessages.Remove(b.Id)) continue;
            tvVideos.Remove(b.Id);
            net.ToAll("BZ|" + b.Id);
        }
        UpdateDoors(true);
        BroadcastState();
    }

    bool AllPlayersFrom(int zone)
    {
        bool any = false;
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || !p.isActiveAndEnabled || !p.Alive) continue;
            any = true;
            if (WorldBuilder.ZoneAt(p.Position) < zone) return false;
        }
        return any;
    }

    // Tous : une fois la porte refermée, les zones et Hubs inaccessibles sont désactivés. La porte, seule à rester visible
    // depuis le chapiteau, sort de sa zone. Nouvelle partie sur la même graine : tout est réactivé.
    void UpdateSealedZones()
    {
        // Un joueur arrivé en cours de partie ne reste pas derrière une porte scellée.
        int here = WorldBuilder.ZoneAt(player.transform.position);
        if (here >= 1 && here <= sealedZones && !status.Dead) RespawnInHub();

        if (sealedZones < deactivatedZones) { RestoreSealedZones(); return; }
        if (sealedZones == deactivatedZones || sealedZones > zones.Count) return;
        var door = zones[sealedZones - 1].acquisitionDoor;
        if (door != null && !door.FullyClosed) return;

        foreach (var z in zones)
        {
            if (z.index > sealedZones || !z.root.activeSelf) continue;
            foreach (var m in z.arcades) UnregisterArcade(m);
            if (z.acquisitionDoor != null)
            {
                z.acquisitionDoor.transform.SetParent(worldRoot, true);
                z.acquisitionDoor.gameObject.SetActive(z.index == sealedZones);
            }
            z.root.SetActive(false);
        }
        // Seule la dernière porte scellée reste visible, depuis le chapiteau.
        foreach (var z in zones)
            if (z.index < sealedZones && z.acquisitionDoor != null) z.acquisitionDoor.gameObject.SetActive(false);
        deactivatedZones = sealedZones;
        SetHubsActive(1, sealedZones / 2, false);
        beacons.SealHubs(sealedZones / 2);
        RemoveSealedItems();
    }

    void RestoreSealedZones()
    {
        foreach (var z in zones)
        {
            if (z.index <= sealedZones || z.root.activeSelf) continue;
            z.root.SetActive(true);
            if (z.acquisitionDoor != null)
            {
                z.acquisitionDoor.transform.SetParent(z.root.transform, true);
                z.acquisitionDoor.gameObject.SetActive(true);
            }
            RegisterArcades(z.arcades);
        }
        SetHubsActive(sealedZones / 2 + 1, hubs.Count, true);
        beacons.SealHubs(sealedZones / 2);
        deactivatedZones = sealedZones;
    }

    // Hubs (numéros 'from' à 'to') et Chenils correspondants.
    void SetHubsActive(int from, int to, bool active)
    {
        for (int h = from; h <= to && h <= hubs.Count; h++)
        {
            if (hubs[h - 1].root != null) hubs[h - 1].root.gameObject.SetActive(active);
            foreach (var list in kennels.Values)
                if (h <= list.Count && list[h - 1] != null) list[h - 1].gameObject.SetActive(active);
        }
    }

    // Monticules et objets à terre des zones scellées.
    void RemoveSealedItems()
    {
        foreach (var kv in new List<KeyValuePair<int, Mound>>(mounds))
        {
            if (kv.Key / MoundIdsPerZone > sealedZones) continue;
            if (kv.Value != null) Destroy(kv.Value.gameObject);
            mounds.Remove(kv.Key);
        }
        foreach (var kv in new List<KeyValuePair<int, ItemPickup>>(items))
        {
            if (kv.Value == null) continue;
            int zone = WorldBuilder.ZoneAt(kv.Value.transform.position);
            if (zone < 1 || zone > sealedZones) continue;
            Destroy(kv.Value.gameObject);
            items.Remove(kv.Key);
            dropped.Remove(kv.Key);
        }
    }

    // ================= Zones trop loin pour être vues =================

    // Tous, pour ce joueur seulement : on ne dessine et n'anime que la zone de la caméra (et du personnage) et ses voisines. La
    // caméra voit à 45 m, une zone en fait 170. Un délai évite de tout rebasculer à chaque passage d'une limite de zone.
    const float ZoneVisibilityInterval = 0.25f, ZoneHideDelay = 5f;
    float zoneVisibilityTimer;

    void UpdateZoneVisibility()
    {
        zoneVisibilityTimer -= Time.unscaledDeltaTime;
        if (zoneVisibilityTimer > 0f) return;
        zoneVisibilityTimer = ZoneVisibilityInterval;
        Camera cam = Camera.main;
        int body = WorldBuilder.ZoneAt(player.transform.position), eye = cam != null ? WorldBuilder.ZoneAt(cam.transform.position) : 0;
        if (body < 1) body = eye;
        if (eye < 1) eye = body;
        if (body < 1) return;

        bool hidOne = false;
        foreach (var z in zones)
        {
            if (!z.root.activeSelf) continue;
            if (Mathf.Abs(z.index - body) <= 1 || Mathf.Abs(z.index - eye) <= 1)
            {
                z.lastNeeded = Time.unscaledTime;
                z.ShowVisuals();
            }
            // Une seule par passage, et jamais tant que du décor reste à poser (il ne serait pas caché).
            else if (!hidOne && !z.VisualsHidden && !WorldStreamer.Busy && Time.unscaledTime - z.lastNeeded > ZoneHideDelay)
            {
                z.HideVisuals();
                hidOne = true;
            }
        }
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
    // V0.6.11 : aux manches suivantes, les Cauchemars après 1 minute de jeu.
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
            bool nightmares = first ? z.index == 1 && played >= GameRules.LateNightmareDelay : played >= GameRules.NightmareDelay;
            if (nightmares && nightmaresSpawned.Add(z.index))
                for (int k = 0, n = first ? 1 : GameRules.NightmaresPerZone; k < n; k++) SpawnNightmare(z.index, rnd, k);
        }
    }

    // GDD 12 (V0.6.11) : Spawn_Manche, une seule fois par manche, quand les Cauchemars apparaissent ; entendu par chaque joueur
    // qui n'est pas dans un Hub (à ce moment-là). Chaque joueur le détecte lui-même (première entité Cauchemar de la manche),
    // une fois l'heure d'apparition atteinte : les Cauchemars de la manche précédente, pas encore effacés, ne comptent pas.
    int roundSpawnHeard = -1;

    void UpdateRoundSpawnSound()
    {
        if (phase != Phase.Running || roundSpawnHeard == round) return;
        float played = GameRules.RoundDuration - timeLeft;
        float delay = round <= GameRules.LateSpawnRound ? GameRules.LateNightmareDelay : GameRules.NightmareDelay;
        if (played < delay - 1f) return;
        bool any = false;
        foreach (var e in entities.Values) if (e != null && e.Nightmare) { any = true; break; }
        if (!any) return;
        roundSpawnHeard = round;
        if (!WorldBuilder.IsInHub(player.transform.position)) GameAudio.Play2D("round_spawn", 1f);
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
        public float sitBreach;      // "Assis !" : secondes d'affilée sans être immobile et accroupi
        public bool voiceChecked;    // V0.6.9 : l'ordre sonore a déjà été répété, ou n'a pas à l'être
        public AudioSource sound;
    }
    readonly Order[] orders = new Order[GameRules.GlyphTypes];
    float glyphTimer, lastBarkAt = -100f;
    // Sons des glyphes (neutres et d'acquisition) un peu baissés.
    const float GlyphVolume = 0.75f;

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
        CheckKennelOrder();
        CheckGlyphHolds();
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
                    if (d < best && beacons.InSight(b, BeaconNetwork.GlyphTarget(g.Id), g.Anchor)) { best = d; source = b.Id; }
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
            // (Plus bas : "A la niche !" s'arrête plus tôt si tous les joueurs sont dans leur niche.)
            if (!openedAcquisitions.Contains(z.index) && z.acquisitionGlyphs.TrueForAll(g => glyphSources.TryGetValue(g.Id, out int s) && s >= 0))
            {
                openedAcquisitions.Add(z.index);
                Log("Tous les glyphes de la Zone {0} sont chargés : la porte du chapiteau s'ouvre !", z.index.ToString());
                UpdateDoors(true);
                BroadcastState();
            }
        }
    }

    // Hôte (V0.6.1) : "A la niche !" s'arrête avant la fin des 30 secondes dès que tous les joueurs en vie sont dans leur
    // niche (un joueur caché qui n'est pas dans un Canon est dans sa niche).
    void CheckKennelOrder()
    {
        const int kennelOrder = 1;
        if (!OrderActive(kennelOrder)) return;
        int alive = 0;
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || !p.isActiveAndEnabled || !p.Alive) continue;
            alive++;
            bool inKennel = p.PlayerId == net.MyId ? InKennel : p.InCannon && !cannonOccupants.ContainsValue(p.PlayerId);
            if (!inKennel) return;
        }
        if (alive > 0) EndOrder(kennelOrder);
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
        if (order?.sound != null) GameAudio.ReleaseLoop(order.sound);
    }

    // Glyphe neutre actionné (une seule fois, boîtier brisé) : son entendu de tous les joueurs présents dans la zone.
    // V0.6.13 : tant que le clic est maintenu sur le glyphe, le joueur y allume une icône (bras droit tendu vers l'avant).
    Glyph glyphHeld;
    PlayerInteractor interactor;

    public void TouchGlyph(Glyph g)
    {
        if (!g.Neutral || glyphHeld == g) return;
        ReleaseGlyph();
        glyphHeld = g;
        net.ToHost("NH|" + g.Id + "|1");
    }

    void ReleaseGlyph()
    {
        if (glyphHeld == null) return;
        net.ToHost("NH|" + glyphHeld.Id + "|0");
        glyphHeld = null;
    }

    // Le clic relâché, le glyphe plus visé (ou actionné), le personnage mort : l'icône s'éteint.
    void UpdateGlyphHold()
    {
        if (glyphHeld == null) return;
        if (interactor == null) interactor = player.GetComponent<PlayerInteractor>();
        bool held = Input.GetMouseButton(0) || Controls.Held(GameAction.Interact);
        if (!held || status.Dead || glyphHeld.Activated || interactor == null || interactor.Current != glyphHeld) ReleaseGlyph();
    }

    // Icônes de chaque glyphe neutre selon le nombre de joueurs en jeu.
    void RefreshGlyphPaws()
    {
        int count = GameRules.GlyphPaws(Mathf.Max(1, net.Roster.Count));
        foreach (var z in zones) foreach (var g in z.neutralGlyphs) g.SetPawCount(count);
    }

    // Hôte : joueurs qui maintiennent le clic sur chaque glyphe neutre.
    readonly Dictionary<int, HashSet<string>> glyphHolders = new Dictionary<int, HashSet<string>>();

    void SetGlyphHolder(int id, string who, bool on)
    {
        Glyph g = FindGlyph(id);
        if (!glyphHolders.TryGetValue(id, out var set)) glyphHolders[id] = set = new HashSet<string>();
        bool valid = g != null && g.Neutral && phase != Phase.GameOver && brokenCases.Contains(id) && !activatedGlyphs.Contains(id);
        if (on && valid) set.Add(who); else set.Remove(who);
        CheckGlyphHold(id);
    }

    // Hôte : toutes les icônes allumées en même temps, le glyphe est actionné.
    void CheckGlyphHold(int id)
    {
        if (!glyphHolders.TryGetValue(id, out var set)) return;
        set.RemoveWhere(p => !net.Roster.ContainsKey(p));
        net.ToAll("NL|" + id + "|" + set.Count);
        Glyph g = FindGlyph(id);
        if (g == null || set.Count < GameRules.GlyphPaws(Mathf.Max(1, net.Roster.Count))) return;
        glyphHolders.Remove(id);
        ActivateNeutralGlyph(g);
    }

    void CheckGlyphHolds()
    {
        foreach (int id in new List<int>(glyphHolders.Keys)) if (glyphHolders[id].Count > 0) CheckGlyphHold(id);
    }

    // Hôte : glyphe neutre actionné (son pour toute la zone, porte de la Neutral Room si ses trois glyphes le sont).
    void ActivateNeutralGlyph(Glyph g)
    {
        if (g == null || !g.Neutral || phase == Phase.GameOver || !brokenCases.Contains(g.Id) || !activatedGlyphs.Add(g.Id)) return;
        net.ToAll("NP|" + g.Zone + "|" + g.Type);
        int door = (g.Zone + 1) / 2;
        if (DoorOpen(door)) Log("Les trois glyphes sont actionnés : la porte de la Zone {0} s'ouvre !", (door * 2).ToString());
        UpdateDoors(true);
        BroadcastState();
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
        glyphHolders.Clear();
        glyphHeld = null;
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
            beacons.ShowBeam(line, b, b.Top, g.Anchor);
        }

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
            if (!order.voiceChecked && elapsed >= GameRules.GlyphVoiceRepeatDelay)
            {
                order.voiceChecked = true;
                if (!status.Dead && !OrderFollowed(t, order)) PlayOrderVoice(t);
            }
            if (t == 0)
            {
                // V0.6.9 : la Sanction tombe après plus de 3 secondes d'affilée sans respecter l'ordre.
                order.sitBreach = OrderFollowed(t, order) ? 0f : order.sitBreach + Time.deltaTime;
                if (order.sitBreach > GameRules.GlyphSitGrace) CheckOrder(order, false);
            }
            else if (t == 2)
                CheckOrder(order, InKennel || Time.time - Mathf.Max(lastBarkAt, order.start) <= GameRules.GlyphBarkGap);
        }
    }

    // Le joueur local effectue l'action demandée (immobile et accroupi, dans sa niche, ou il a aboyé depuis le début de l'ordre).
    bool OrderFollowed(int type, Order order)
    {
        if (InKennel) return true;
        return type == 0 ? player.Crouched && presence.Still : type == 2 && lastBarkAt >= order.start;
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

    // Chaque coup de marteau sur sa cloche coûte 20 de sérénité aux joueurs qu'elle obnubile (note 07 du chapitre 14). Le coup qui
    // obnubile le joueur local lui donne aussi le zoom brusque (chapitre 3.4) et, une seule fois par entité, le son TeteDeCloche_Vue.
    readonly HashSet<int> teteStruck = new HashSet<int>();

    TeteDeCloche WatchTete(TeteDeCloche tete)
    {
        tete.Rang += () =>
        {
            if (tete == null || !tete.Obsesses(net.MyId)) return;
            LoseBellSerenity();
            if (status.Dead || Teleporting || InKennel || !teteStruck.Add(tete.Id)) return;
            OrbitCamera.BellStrike();
            if (tete.SightVoiced) return;
            tete.SightVoiced = true;
            GameAudio.Play3D("tete_vue", tete.transform.position + Vector3.up * tete.EyesLocal.y, 1f, GameRules.TeteSightRange + 20f, 0f);
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
    public static bool Hidden => InCannon || InKennel || Sliding;

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
                order.sound = GameAudio.Loop2D(Glyph.Sounds[type], 0.85f * GlyphVolume);
                // V0.6.7 : plus de texte, l'ordre est annoncé à voix haute, dans la langue du joueur.
                if (NetworkGame.P(f[3]) <= 0f) PlayOrderVoice(type);
                else order.voiceChecked = true;
                orders[type] = order;
                return true;
            }
            case "NG" when host && f.Length >= 2:
                ActivateNeutralGlyph(FindGlyph(NetworkGame.I(f[1])));
                return true;
            // V0.6.13 : clic maintenu (1) ou relâché (0) sur un glyphe neutre ; NL : icônes allumées.
            case "NH" when host && f.Length >= 3:
                SetGlyphHolder(NetworkGame.I(f[1]), from, f[2] == "1");
                return true;
            case "NL" when f.Length >= 3:
            {
                Glyph g = FindGlyph(NetworkGame.I(f[1]));
                if (g != null) g.SetPawsLit(NetworkGame.I(f[2]));
                return true;
            }
            case "NP" when f.Length >= 3:
            {
                int zone = NetworkGame.I(f[1]), type = NetworkGame.I(f[2]);
                if (zone < 1 || zone > zones.Count || type < 0 || type >= Glyph.Sounds.Length) return true;
                if (WorldBuilder.ZoneAt(player.transform.position) == zone) GameAudio.Play2D(Glyph.Sounds[type], 0.9f * GlyphVolume);
                return true;
            }
            // Ballons des Hubs (V0.6.9) : envoyés seulement quand un joueur en pousse un ; chacun simule ensuite le même trajet.
            case "BK" when host && f.Length >= 6:
                net.ToAll("BL|" + from + "|" + string.Join("|", f, 1, 5));
                return true;
            case "BL" when f.Length >= 7:
                if (f[1] != net.MyId && HubBall.All.TryGetValue(NetworkGame.I(f[2]), out var kicked) && kicked != null)
                    kicked.Apply(NetworkGame.P(f[3]), NetworkGame.P(f[4]), NetworkGame.P(f[5]), NetworkGame.P(f[6]));
                return true;
            case "SN" when host:
                SpawnTete(from);
                return true;
        }
        return false;
    }
}
