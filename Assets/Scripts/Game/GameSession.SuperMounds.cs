using System.Collections.Generic;
using System.Text;
using UnityEngine;

// V0.7.2 (chapitre 10.2) : Super Monticules. Comme les monticules, ils sont tirés d'après la graine à chaque manche, chez tous
// les joueurs (4 + nZone x 2 par zone, 16 au plus, les étoiles des plateformes d'abord) ; ils ne comptent pas dans le tableau
// de génération d'objets de valeur. Aucun message n'est envoyé en continu : seulement quand un joueur commence ou cesse de
// maintenir le clic, puis à l'ouverture.
// Messages : UH|monticule|1/0                     joueur -> hôte : clic maintenu (1) ou relâché (0)
//            UL|monticule|n                       hôte -> tous : traces allumées
//            UD|manche|monticule|joueur,joueur... hôte -> tous : toutes allumées, les joueurs creusent ensemble
//            UO|manche|monticule                  hôte -> tous : déterré (l'objet est posé par l'hôte, message BW)
//            UX|manche|monticule,monticule...     hôte -> joueur qui arrive : déjà déterrés
public partial class GameSession
{
    const int SuperIdsPerZone = 100;
    readonly Dictionary<int, SuperMound> superMounds = new Dictionary<int, SuperMound>();
    readonly Dictionary<int, (ItemType type, Vector3 pos)> superLoot = new Dictionary<int, (ItemType, Vector3)>();
    readonly HashSet<int> openedSupers = new HashSet<int>();
    // Hôte : joueurs qui maintiennent le clic sur chaque Super Monticule, fin des creusages en cours.
    readonly Dictionary<int, HashSet<string>> superHolders = new Dictionary<int, HashSet<string>>();
    readonly Dictionary<int, float> superDigEnds = new Dictionary<int, float>();
    // Joueur local : Super Monticule dont il maintient le clic ; fin de son creusage collectif.
    SuperMound superHeld;
    float superDigUntil = -1f;

    // V0.7.3 (Note 06) : modèles des Super Monticules et des objets qu'on y déterre chargés dès le lancement, pour que leur
    // première apparition ne fige pas le jeu.
    static bool lootPreloaded;

    static void PreloadSuperMoundArt()
    {
        if (lootPreloaded) return;
        lootPreloaded = true;
        for (int k = 1; k <= 4; k++) Resources.Load<GameObject>("Art/SuperMonticule" + k);
        foreach (var (type, _) in GameRules.SuperMoundLoot) Resources.Load<GameObject>("Art/" + Items.ModelName(type));
    }

    // Super Monticules d'une zone pour la manche ; renvoie leurs emplacements (les monticules ordinaires s'en écartent).
    List<Vector3> SpawnZoneSuperMounds(WorldBuilder.ZoneRefs zone)
    {
        var chosen = new List<Vector3>();
        var rnd = new System.Random(seed * 43 + round * 6151 + zone.index * 7349);
        // Les étoiles d'abord (mélangées entre elles), puis les autres emplacements.
        var stars = zone.superSpots.GetRange(0, zone.superPriority);
        var others = zone.superSpots.GetRange(zone.superPriority, zone.superSpots.Count - zone.superPriority);
        Shuffle(stars, rnd);
        Shuffle(others, rnd);
        stars.AddRange(others);
        int count = Mathf.Min(GameRules.SuperMoundsInZone(zone.index), stars.Count);
        int paws = GameRules.SuperMoundPaws(Mathf.Max(1, net.Roster.Count));
        for (int k = 0; k < count; k++)
        {
            int id = zone.index * SuperIdsPerZone + k;
            Vector3 pos = stars[k];
            ItemType loot = RollSuperLoot(rnd);
            float yaw = (float)rnd.NextDouble() * 360f;
            chosen.Add(pos);
            superLoot[id] = (loot, pos);
            if (openedSupers.Contains(id) || superMounds.ContainsKey(id)) continue;
            superMounds[id] = SuperMound.Create(itemsRoot, id, loot, pos, yaw, paws);
        }
        return chosen;
    }

    static void Shuffle<T>(List<T> list, System.Random rnd)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static ItemType RollSuperLoot(System.Random rnd)
    {
        double roll = rnd.NextDouble();
        foreach (var (type, chance) in GameRules.SuperMoundLoot)
        {
            if (roll < chance) return type;
            roll -= chance;
        }
        return GameRules.SuperMoundLoot[0].type;
    }

    // Nouvelle manche (ou nouvelle partie) : les Super Monticules restants disparaissent ; 'all' : leur état aussi.
    void ClearSuperMoundObjects()
    {
        foreach (var m in superMounds.Values) if (m != null) Destroy(m.gameObject);
        superMounds.Clear();
        superLoot.Clear();
        superHeld = null;
    }

    void ClearSuperMounds()
    {
        ClearSuperMoundObjects();
        openedSupers.Clear();
        superHolders.Clear();
        superDigEnds.Clear();
        superDigUntil = -1f;
    }

    // Nombre de joueurs changé : autant de traces que d'icônes sur un glyphe neutre.
    void RefreshSuperMoundPaws()
    {
        int paws = GameRules.SuperMoundPaws(Mathf.Max(1, net.Roster.Count));
        foreach (var m in superMounds.Values) if (m != null) m.SetPawCount(paws);
        if (net.IsHost) foreach (int id in new List<int>(superHolders.Keys)) CheckSuperHold(id);
    }

    // Zones scellées : leurs Super Monticules disparaissent.
    void RemoveSealedSuperMounds()
    {
        foreach (var kv in new List<KeyValuePair<int, SuperMound>>(superMounds))
        {
            if (kv.Key / SuperIdsPerZone > sealedZones) continue;
            if (kv.Value != null) Destroy(kv.Value.gameObject);
            superMounds.Remove(kv.Key);
        }
    }

    // ================= Joueur local =================

    public void TouchSuperMound(SuperMound m)
    {
        if (m == null || m.Digging || superHeld == m || status.Dead || Eating) return;
        ReleaseSuperMound();
        superHeld = m;
        net.ToHost("UH|" + m.Id + "|1");
    }

    void ReleaseSuperMound()
    {
        if (superHeld == null) return;
        if (superHeld != null) net.ToHost("UH|" + superHeld.Id + "|0");
        superHeld = null;
    }

    // Le clic relâché, le tas plus visé (ou déterré), le personnage mort : la trace s'éteint.
    void UpdateSuperMounds()
    {
        if (superHeld != null)
        {
            if (interactor == null) interactor = player.GetComponent<PlayerInteractor>();
            bool held = Input.GetMouseButton(0) || Controls.Held(GameAction.Interact);
            if (superHeld == null || !held || status.Dead || superHeld.Digging || interactor == null || interactor.Current != superHeld) ReleaseSuperMound();
        }
        // Creusage collectif : le personnage reste immobile 1,2 seconde.
        if (superDigUntil >= 0f && (Time.time >= superDigUntil || status.Dead))
        {
            superDigUntil = -1f;
            player.Digging = false;
        }
        if (!net.IsHost || superDigEnds.Count == 0) return;
        var done = new List<int>();
        foreach (var kv in superDigEnds) if (Time.time >= kv.Value) done.Add(kv.Key);
        foreach (int id in done)
        {
            superDigEnds.Remove(id);
            net.ToAll("UO|" + round + "|" + id);
            // L'objet déterré est posé au sol à la place du tas (ramassable comme une Balise).
            if (superLoot.TryGetValue(id, out var loot))
                PlaceDevice(Beacon.KindOf(loot.type), loot.pos, Random.Range(0f, 360f), "");
        }
    }

    // Bras droit tendu vers le Super Monticule (comme vers un glyphe neutre).
    bool ArmToSuperMound => superHeld != null && !status.Dead;

    // ================= Hôte =================

    void SetSuperHolder(int id, string who, bool on)
    {
        if (!superHolders.TryGetValue(id, out var set)) superHolders[id] = set = new HashSet<string>();
        bool valid = superLoot.ContainsKey(id) && !openedSupers.Contains(id) && !superDigEnds.ContainsKey(id) && phase != Phase.GameOver;
        if (on && valid) set.Add(who); else set.Remove(who);
        CheckSuperHold(id);
    }

    void CheckSuperHold(int id)
    {
        if (!superHolders.TryGetValue(id, out var set)) return;
        set.RemoveWhere(p => !net.Roster.ContainsKey(p));
        net.ToAll("UL|" + id + "|" + set.Count);
        if (set.Count < GameRules.SuperMoundPaws(Mathf.Max(1, net.Roster.Count)) || superDigEnds.ContainsKey(id)) return;
        superHolders.Remove(id);
        superDigEnds[id] = Time.time + GameRules.DigDuration;
        foreach (string p in set) noiseAt[p] = Time.time;
        net.ToAll("UD|" + round + "|" + id + "|" + string.Join(",", set));
    }

    void SendSuperMoundSnapshot(string to)
    {
        var sb = new StringBuilder();
        foreach (int id in openedSupers) { if (sb.Length > 0) sb.Append(','); sb.Append(id); }
        net.ToPeer(to, "UX|" + round + "|" + sb);
    }

    // Déterré : le tas disparaît (gerbe de terre si 'animate').
    void OpenSuperMound(int id, bool animate)
    {
        if (!openedSupers.Add(id)) return;
        superDigEnds.Remove(id);
        if (superMounds.TryGetValue(id, out var m) && m != null)
        {
            if (superHeld == m) superHeld = null;
            if (animate) m.Open();
            else Destroy(m.gameObject);
        }
        superMounds.Remove(id);
    }

    // ================= Réception =================

    bool HandleSuperMoundMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "UH" when host && f.Length >= 3:
                SetSuperHolder(NetworkGame.I(f[1]), from, f[2] == "1");
                return true;
            case "UL" when f.Length >= 3:
                if (superMounds.TryGetValue(NetworkGame.I(f[1]), out var lit) && lit != null) lit.SetPawsLit(NetworkGame.I(f[2]));
                return true;
            case "UD" when f.Length >= 4:
            {
                if (NetworkGame.I(f[1]) != round) return true;
                int id = NetworkGame.I(f[2]);
                if (superMounds.TryGetValue(id, out var m) && m != null) m.StartDigging();
                if (superHeld != null && superHeld.Id == id) superHeld = null;
                // Les joueurs qui tenaient une trace creusent tous en même temps (ils aboient en commençant).
                foreach (string p in f[3].Split(','))
                {
                    if (p == net.MyId && !status.Dead)
                    {
                        superDigUntil = Time.time + GameRules.DigDuration;
                        player.Digging = true;
                        Bark();
                    }
                }
                return true;
            }
            case "UO" when f.Length >= 3:
                if (NetworkGame.I(f[1]) == round) OpenSuperMound(NetworkGame.I(f[2]), true);
                return true;
            case "UX" when !host && f.Length >= 3:
                if (NetworkGame.I(f[1]) != round) return true;
                foreach (string s in f[2].Split(','))
                    if (s.Length > 0) OpenSuperMound(NetworkGame.I(s), false);
                return true;
        }
        return false;
    }
}
