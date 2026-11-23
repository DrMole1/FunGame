using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// V0.7.11 (chapitre 11.3) : environnement Château. Une zone de 60 m de haut, damier noir et blanc partout, plafond supérieur gris.
// - Rempart extérieur tiré parmi 12 gabarits (ligne brisée sur la grille de 4 x 4 de la zone) : 20 m de haut, 12 m d'épaisseur,
//   creux (rez-de-chaussée, 1er étage à 10 m, chemin de ronde à l'air libre à 20 m entre les créneaux), reliés par des escaliers
//   intérieurs. Une herse fermée de chaque côté ; un levier (1er étage ou chemin de ronde), relié aux deux herses par des câbles
//   noirs, les ouvre pour de bon. Douve de 4 m de profondeur devant (côté entrée), avec des escaliers pour en sortir ; 3 à 5
//   escaliers derrière (côté Neutral Room) montent sur le rempart. On ne le franchit autrement qu'en Canon.
// - À partir de la Zone 4, un rempart intérieur autour de la Neutral Room (3 gabarits), aux mêmes propriétés.
// - 6 Tours (30 m, 15 à 20 m de diamètre, créneaux, au moins 15 meurtrières, escalier en colimaçon jusqu'au sommet, étages à 10 et
//   20 m une fois sur deux), 1 Donjon au centre (une Tour 50 % plus haute et plus large), 10 Maisons-Fou (15 m, 8 m de diamètre).
// - Entre eux, des salles de 8 à 12 m de haut (côtés de 5 à 20 m), aux toits ceints de créneaux, reliées par des corridors (15 %
//   de corridors pour 85 % de salles ; 30 % des corridors ont un renfoncement en biseau) : cachots, salles d'armes, salles de
//   banquet, halls, chapelles. Éclairage par des lustres. Les cours (remparts, douves, abords des Tours) sont les endroits sans
//   bâtiment.
public static partial class WorldBuilder
{
    public const float ChateauHeight = 60f;
    const float RampartHeight = 20f, RampartThick = 12f, RampartFloor = 10f, MoatDepth = 4f, GateWidth = 4f, GateHeight = 5f;
    const float TowerHeight = 30f, MaisonHeight = 15f, MaisonDiameter = 8f;

    // Gabarits (coordonnées sur la grille de 4 x 4 de la zone : x d'ouest en est, y du sud (entrée) au nord (Neutral Room)).
    static readonly Vector2[][] OuterRamparts =
    {
        new[] { new Vector2(0, 2), new Vector2(1, 2), new Vector2(1, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 2), new Vector2(1, 2), new Vector2(1, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(3, 2), new Vector2(3, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(1, 2), new Vector2(1, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(2, 2), new Vector2(2, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(1, 2), new Vector2(1, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(3, 2), new Vector2(3, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(3, 2), new Vector2(3, 1), new Vector2(4, 1) },
    };

    static readonly Vector2[][] InnerRamparts =
    {
        new[] { new Vector2(1, 4), new Vector2(1, 3), new Vector2(3, 3), new Vector2(3, 4) },
        new[] { new Vector2(0, 3), new Vector2(3, 3), new Vector2(3, 4) },
        new[] { new Vector2(1, 4), new Vector2(1, 3), new Vector2(4, 3) },
    };

    // Point d'une grille de gabarit (4 x 4) en coordonnées monde (x, z).
    static Vector2 GridPoint(int zone, Vector2 g) => new Vector2(OriginX + g.x * ZoneSize / 4f, ZoneSouth(zone) + g.y * ZoneSize / 4f);

    internal class RampartPlan
    {
        public readonly List<Rect> rects = new List<Rect>();          // emprise de chaque segment (12 m d'épaisseur)
        public readonly List<bool> horizontal = new List<bool>();
        public readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>(), moat = new HashSet<Vector2Int>(), front = new HashSet<Vector2Int>(), back = new HashSet<Vector2Int>();
        public HashSet<Vector2Int> nrSide;                              // cellules du côté de la Neutral Room
        public int gateSegment = -1;
        public Vector2 gate;                                            // milieu du passage (x, z)
    }

    static RampartPlan PlanRampart(Maze m, Vector2[] template)
    {
        var plan = new RampartPlan();
        float half = RampartThick * 0.5f, west = OriginX, east = OriginX + ZoneSize, south = ZoneSouth(m.zone), north = south + ZoneSize;
        for (int k = 0; k + 1 < template.Length; k++)
        {
            Vector2 a = GridPoint(m.zone, template[k]), b = GridPoint(m.zone, template[k + 1]);
            bool horizontal = Mathf.Abs(a.y - b.y) < 0.01f;
            Rect r = horizontal
                ? Rect.MinMaxRect(Mathf.Min(a.x, b.x) - half, a.y - half, Mathf.Max(a.x, b.x) + half, a.y + half)
                : Rect.MinMaxRect(a.x - half, Mathf.Min(a.y, b.y) - half, a.x + half, Mathf.Max(a.y, b.y) + half);
            // Les bouts qui touchent le bord de la zone s'arrêtent au mur d'enceinte.
            r = Rect.MinMaxRect(Mathf.Max(r.xMin, west), Mathf.Max(r.yMin, south), Mathf.Min(r.xMax, east), Mathf.Min(r.yMax, north));
            plan.rects.Add(r);
            plan.horizontal.Add(horizontal);
        }
        // Cellules couvertes par le rempart.
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                Vector3 c = m.Center(i, j);
                var cell = Rect.MinMaxRect(c.x - Cell * 0.5f + 0.05f, c.z - Cell * 0.5f + 0.05f, c.x + Cell * 0.5f - 0.05f, c.z + Cell * 0.5f - 0.05f);
                if (plan.rects.Exists(r => r.Overlaps(cell))) plan.cells.Add(new Vector2Int(i, j));
            }
        // Côté de la Neutral Room : tout ce qu'on atteint depuis elle sans traverser le rempart.
        plan.nrSide = Flood(m, new Vector2Int(m.doorCol, m.N - 2), plan.cells);
        // Douve devant (côté entrée), une bande de sol devant la douve, et une bande derrière (escaliers vers le chemin de ronde).
        foreach (var c in plan.cells)
            for (int d = 0; d < 4; d++)
            {
                var n = c + new Vector2Int(DI[d], DJ[d]);
                if (!m.InGrid(n.x, n.y) || plan.cells.Contains(n) || m.Reserved(n.x, n.y) || m.Fixed(n.x, n.y)) continue;
                if (plan.nrSide.Contains(n)) plan.back.Add(n);
                else plan.moat.Add(n);
            }
        foreach (var c in plan.moat)
            for (int d = 0; d < 4; d++)
            {
                var n = c + new Vector2Int(DI[d], DJ[d]);
                if (!m.InGrid(n.x, n.y) || plan.cells.Contains(n) || plan.moat.Contains(n) || plan.nrSide.Contains(n) || m.Reserved(n.x, n.y) || m.Fixed(n.x, n.y)) continue;
                plan.front.Add(n);
            }
        // L'entrée de la zone (devant la porte du Hub) reste au sol.
        plan.moat.Remove(m.start);
        plan.front.Remove(m.start);
        return plan;
    }

    // Cellules atteintes depuis 'from' sans passer par 'blocked' (ni par le Hub).
    static HashSet<Vector2Int> Flood(Maze m, Vector2Int from, HashSet<Vector2Int> blocked)
    {
        var seen = new HashSet<Vector2Int>();
        if (!m.InGrid(from.x, from.y)) return seen;
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        seen.Add(from);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                var n = c + new Vector2Int(DI[d], DJ[d]);
                if (!m.InGrid(n.x, n.y) || seen.Contains(n) || blocked.Contains(n) || m.Reserved(n.x, n.y)) continue;
                seen.Add(n);
                queue.Enqueue(n);
            }
        }
        return seen;
    }

    // Tour ronde posée dans la zone : centre, rayon, hauteur (Tours, Donjon, Maisons-Fou).
    internal class RoundPlan { public Vector3 center; public float radius, height; public int kind; }   // kind : 0 Tour, 1 Donjon, 2 Maison-Fou

    static IEnumerator BuildChateauSteps(ZoneBuild build, Transform parent, int seed, int zone, ZoneRefs previous)
    {
        var rnd = new System.Random(seed * 37 + zone * 6151);
        Maze m = CreateMaze(seed, zone);
        m.terrain = TerrainKind.Chateau;
        m.voidCell = new bool[m.N, m.N];
        m.voidCeil = ChateauHeight;
        m.bevelChance = 0.3f;
        if (previous != null) m.southHeights = previous.maze.northHeights;

        // ---------- Plan : remparts, Tours, Donjon, Maisons-Fou ----------
        var ramparts = new List<RampartPlan> { PlanRampart(m, OuterRamparts[rnd.Next(OuterRamparts.Length)]) };
        if (zone >= 4) ramparts.Add(PlanRampart(m, InnerRamparts[rnd.Next(InnerRamparts.Length)]));
        void MarkVoid(Vector2Int c) { if (m.InGrid(c.x, c.y) && !m.Reserved(c.x, c.y) && !m.Fixed(c.x, c.y) && c != m.start) m.voidCell[c.x, c.y] = true; }
        foreach (var rp in ramparts)
        {
            foreach (var c in rp.cells) MarkVoid(c);
            foreach (var c in rp.moat) MarkVoid(c);
            foreach (var c in rp.front) MarkVoid(c);
            foreach (var c in rp.back) MarkVoid(c);
        }
        // Une marge de sol libre devant le Hub et devant la Neutral Room.
        bool NearKeep(int i, int j) => Mathf.Abs(i - m.start.x) + Mathf.Abs(j - m.start.y) <= 2 || m.neutralRoom.Contains(new Vector2Int(i, j))
            || Mathf.Abs(i - m.doorCol) <= 2 && j >= m.N - 5;

        var rounds = new List<RoundPlan>();
        // Emprise libre : ni rempart, ni douve, ni autre bâtiment rond (3 m d'écart), ni les abords du Hub et de la Neutral Room ; les
        // cours au pied des remparts sont permises.
        var rampartArea = new HashSet<Vector2Int>();
        foreach (var rp in ramparts) { rampartArea.UnionWith(rp.cells); rampartArea.UnionWith(rp.moat); }
        bool PlotFree(Vector3 center, float plot)
        {
            Vector3 o = m.origin;
            int i0 = Mathf.FloorToInt((center.x - plot - o.x) / Cell), i1 = Mathf.FloorToInt((center.x + plot - o.x) / Cell);
            int j0 = Mathf.FloorToInt((center.z - plot - o.z) / Cell), j1 = Mathf.FloorToInt((center.z + plot - o.z) / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    if (!m.InGrid(i, j) || m.Reserved(i, j) || m.Fixed(i, j) || rampartArea.Contains(new Vector2Int(i, j)) || NearKeep(i, j)) return false;
            foreach (var other in rounds)
                if (Flat(other.center - center) < other.radius + plot + 3f) return false;
            return true;
        }
        void MarkPlot(Vector3 center, float plot)
        {
            Vector3 o = m.origin;
            for (int i = Mathf.FloorToInt((center.x - plot - o.x) / Cell); i <= Mathf.FloorToInt((center.x + plot - o.x) / Cell); i++)
                for (int j = Mathf.FloorToInt((center.z - plot - o.z) / Cell); j <= Mathf.FloorToInt((center.z + plot - o.z) / Cell); j++)
                    MarkVoid(new Vector2Int(i, j));
        }
        // Donjon d'abord, au centre de la zone (les 4 parts centrales de la grille de 4 x 4).
        {
            float radius = Rand(rnd, 15f, 20f) * 1.5f * 0.5f;
            Vector2 lo = GridPoint(zone, new Vector2(1f, 1f)), hi = GridPoint(zone, new Vector2(3f, 3f));
            for (int attempt = 0; attempt < 400; attempt++)
            {
                float r = attempt < 300 ? radius : radius * 0.8f;
                var c = new Vector3(Rand(rnd, lo.x + r, hi.x - r), 0f, Rand(rnd, lo.y + r, hi.y - r));
                if (!PlotFree(c, r + 3f)) continue;
                MarkPlot(c, r + 3f);
                rounds.Add(new RoundPlan { center = c, radius = r, height = TowerHeight * 1.5f, kind = 1 });
                break;
            }
        }
        void PlaceRounds(int count, int kind, float minR, float maxR, float height)
        {
            for (int k = 0; k < count; k++)
                for (int attempt = 0; attempt < 300; attempt++)
                {
                    float r = Rand(rnd, minR, maxR);
                    var c = new Vector3(Rand(rnd, OriginX + r + 6f, OriginX + ZoneSize - r - 6f), 0f, Rand(rnd, ZoneSouth(zone) + r + 6f, ZoneSouth(zone) + ZoneSize - r - 6f));
                    if (!PlotFree(c, r + 2f)) continue;
                    MarkPlot(c, r + 2f);
                    rounds.Add(new RoundPlan { center = c, radius = r, height = height, kind = kind });
                    break;
                }
        }
        PlaceRounds(6, 0, 7.5f, 10f, TowerHeight);
        PlaceRounds(10, 2, MaisonDiameter * 0.5f, MaisonDiameter * 0.5f, MaisonHeight);
        yield return null;

        // ---------- Salles et corridors (labyrinthe dans les cellules restantes) ----------
        var style = new ZoneStyle
        {
            maxRooms = 400, minRoomSize = 1, maxRoomSize = 4, roomCoverage = 0.85f, pitChance = 0f, minPitDepth = 1.5f, maxPitDepth = 0f,
            maxLayerDepth = 0f, bridgeChance = 0f, maxHeight = 12f, floors = new[] { 0f }, ceilings = new[] { 8f, 9f, 10f, 11f, 12f },
            chaoticCeilings = new[] { 8f, 10f, 12f }, pitCeilings = new[] { 8f }, fixedRooms = true,
        };
        Generate(m, rnd, style);
        StampFixedRooms(m);
        var natures = new Dictionary<Room, int>();
        foreach (Room room in m.rooms)
            if (!room.plain) { room.plain = true; natures[room] = rnd.Next(5); }
        OpenCourtyards(m, rnd);
        // Chemins (Parfum, câbles) : les cours se traversent librement, pas les remparts (sauf par leur passage).
        var rampartCells = new HashSet<Vector2Int>();
        foreach (var rp in ramparts) rampartCells.UnionWith(rp.cells);
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
                foreach (int d in new[] { 0, 2 })
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (!m.Void(i, j) || !m.Void(ni, nj)) continue;
                    if (rampartCells.Contains(new Vector2Int(i, j)) || rampartCells.Contains(new Vector2Int(ni, nj))) continue;
                    SetWall(m, i, j, d, false);
                }
        yield return null;

        var refs = new ZoneRefs { index = zone, maze = m, root = new GameObject("Zone " + zone), terrain = TerrainKind.Chateau };
        build.Refs = refs;
        Transform root = refs.root.transform;
        root.SetParent(parent, false);
        var colliders = new GameObject("Colliders");
        colliders.transform.SetParent(root, false);
        refs.structure = colliders.transform;
        refs.entrance = m.Center(m.start.x, m.start.y);
        var outer = ramparts[0];
        refs.sideOf = p => m.CellAt(p, out int ci, out int cj) && outer.nrSide.Contains(new Vector2Int(ci, cj)) ? 1 : 0;

        var geometry = BuildGeometry(m, root, colliders, new System.Random(seed * 97 + zone * 173));
        while (geometry.MoveNext()) yield return null;

        // ---------- Cours, douves, toits, enceinte de 60 m ----------
        var shell = new Kit(root, colliders.transform, "Z" + zone + "_Chateau");
        BuildCourtyards(m, ramparts, shell, rnd);
        BuildRoofs(m, shell);
        BuildEnclosure(m, shell, ChateauHeight, previous);
        shell.Flush();
        yield return null;

        // ---------- Remparts ----------
        for (int k = 0; k < ramparts.Count; k++)
        {
            BuildRampart(m, refs, ramparts[k], root, colliders.transform, zone * 10 + k, new System.Random(seed * 41 + zone * 313 + k * 71));
            yield return null;
        }

        // ---------- Tours, Donjon, Maisons-Fou ----------
        float highest = float.MinValue;
        foreach (var plan in rounds)
        {
            var kit = new Kit(root, colliders.transform, "Z" + zone + (plan.kind == 2 ? "_MaisonFou" : plan.kind == 1 ? "_Donjon" : "_Tour"));
            if (plan.kind == 2) BuildMaisonFou(kit, plan, rnd);
            else
            {
                Vector3 summit = BuildTower(kit, plan, rnd, root);
                // Cloches Décoratives tout en haut des Tours et du Donjon (chapitre 7.6).
                refs.bellSpots.Add((summit + new Vector3(Rand(rnd, -1.5f, 1.5f), 0f, Rand(rnd, -1.5f, 1.5f)), 0f));
                if (summit.y > highest) highest = summit.y;
                m.perches.Add((summit + Vector3.right * 2f, 0f, 3f));
            }
            kit.Flush();
            yield return null;
        }
        // Le plus haut d'abord : les Cloches de la zone y apparaissent en priorité.
        refs.bellSpots.Sort((a, b) => b.pos.y.CompareTo(a.pos.y));
        int bells = GameRules.HangingBellsInZone(zone);
        if (refs.bellSpots.Count > bells) refs.bellSpots.RemoveRange(bells, refs.bellSpots.Count - bells);

        BuildNeutralRoom(m, root, refs, new System.Random(seed * 59 + zone * 613));
        BuildBevels(m, root, refs, new System.Random(seed * 53 + zone * 929));
        yield return null;

        // Décor des salles (lustres, emplacements des monticules), puis le mobilier selon la nature de chaque salle.
        Physics.SyncTransforms();
        floorEyes = new List<Vector3>();
        statueLog = refs.statues;
        var decor = Decorate(m, root, colliders.transform, rnd, refs.itemSpots);
        while (decor.MoveNext()) yield return null;
        statueLog = null;
        FurnishCastle(m, root, natures, rnd);
        CourtyardSpots(m, refs, rnd);
        yield return null;
        var supers = FindSuperMoundSpots(m, refs, new System.Random(seed * 67 + zone * 389));
        while (supers.MoveNext()) yield return null;

        refs.lowestFloor = -MoatDepth - 1f;
        refs.nav = new MazeNav();
        refs.nav.AddZone(m.origin, m.N, Cell, (i, j) => !m.Reserved(i, j), (i, j) => TowerHeight * 1.5f + 2f, refs.lowestFloor);
        refs.nav.Prepare(colliders.transform, refs.door != null ? refs.door.Blocker : null);
        AddReflectionProbes(m, root);
        if (zone == GameRules.SecretCorpseZone) PlaceSecretCorpse(m, root);
        // Le mur nord de la zone monte jusqu'au plafond gris : la zone suivante n'a rien à compléter.
        for (int i = 0; i < m.N; i++) m.northHeights[i] = ChateauHeight;
    }

    // Les cours (cellules vides) s'ouvrent sur les salles et corridors voisins : chaque région de salles est reliée à au moins une
    // cour (sauf celle de la Neutral Room et celle du Hub, déjà reliées au reste), et chaque cour à plusieurs portes.
    static void OpenCourtyards(Maze m, System.Random rnd)
    {
        var edges = new List<(Vector2Int town, int dir)>();
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (m.Void(i, j) || m.Reserved(i, j) || m.Fixed(i, j)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (m.Void(ni, nj)) edges.Add((new Vector2Int(i, j), d));
                }
            }
        Shuffle(edges, rnd);
        // Régions de salles (cellules reliées sans mur) : chacune reçoit une porte vers une cour, puis une sur cinq de plus.
        var region = new Dictionary<Vector2Int, int>();
        int regions = 0;
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                var c = new Vector2Int(i, j);
                if (m.Void(i, j) || m.Reserved(i, j) || region.ContainsKey(c)) continue;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(c);
                region[c] = regions;
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        var n = q + new Vector2Int(DI[d], DJ[d]);
                        if (!m.InGrid(n.x, n.y) || m.Void(n.x, n.y) || m.Reserved(n.x, n.y) || region.ContainsKey(n) || HasWall(m, q.x, q.y, d)) continue;
                        region[n] = regions;
                        queue.Enqueue(n);
                    }
                }
                regions++;
            }
        var served = new HashSet<int>();
        foreach (var (town, dir) in edges)
        {
            int reg = region.TryGetValue(town, out int r) ? r : -1;
            if (m.Fixed(town.x, town.y)) continue;
            // Une porte au moins par région, puis environ une arête sur douze.
            if (served.Contains(reg) && rnd.NextDouble() > 0.08) continue;
            served.Add(reg);
            SetWall(m, town.x, town.y, dir, false);
        }
    }

    // Sol des cours (au niveau du Hub), douves de 4 m creusées devant les remparts, avec leurs escaliers.
    static void BuildCourtyards(Maze m, List<RampartPlan> ramparts, Kit kit, System.Random rnd)
    {
        var moat = new HashSet<Vector2Int>();
        foreach (var rp in ramparts) moat.UnionWith(rp.moat);
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (!m.Void(i, j)) continue;
                Vector3 c = m.Center(i, j);
                var cell = Rect.MinMaxRect(c.x - Cell * 0.5f, c.z - Cell * 0.5f, c.x + Cell * 0.5f, c.z + Cell * 0.5f);
                bool isMoat = moat.Contains(new Vector2Int(i, j)) && m.Void(i, j);
                kit.Floor(cell, isMoat ? -MoatDepth : 0f);
                if (!isMoat) continue;
                // Parois de la douve, là où la cellule voisine n'en fait pas partie.
                for (int d = 0; d < 4; d++)
                {
                    var n = new Vector2Int(i + DI[d], j + DJ[d]);
                    if (moat.Contains(n) && m.Void(n.x, n.y)) continue;
                    Vector3 dir = new Vector3(DI[d], 0f, DJ[d]);
                    Vector3 wc = c + dir * (Cell * 0.5f - 0.15f) + Vector3.down * (MoatDepth * 0.5f);
                    kit.Box(wc, d < 2 ? new Vector3(0.3f, MoatDepth, Cell) : new Vector3(Cell, MoatDepth, 0.3f));
                }
            }
        // Escaliers pour sortir des douves (vers la bande de sol devant, et vers le pied du rempart).
        foreach (var rp in ramparts)
        {
            var cells = new List<Vector2Int>(rp.moat);
            Shuffle(cells, rnd);
            int wanted = Mathf.Max(2, cells.Count / 6), placed = 0;
            foreach (var c in cells)
            {
                if (placed >= wanted) break;
                for (int d = 0; d < 4; d++)
                {
                    var n = new Vector2Int(c.x + DI[d], c.y + DJ[d]);
                    if (!m.InGrid(n.x, n.y) || moat.Contains(n) || !m.Void(n.x, n.y)) continue;
                    // En haut : le bord de la cellule voisine (au sol) ; l'escalier descend vers l'intérieur de la douve.
                    Vector3 edge = m.Center(c.x, c.y) + new Vector3(DI[d], 0f, DJ[d]) * (Cell * 0.5f);
                    Vector3 down = -new Vector3(DI[d], 0f, DJ[d]);
                    float run = StairRun(MoatDepth);
                    KitStairs(kit, edge + down * run, -down, 2.4f, -MoatDepth, 0f);
                    placed++;
                    break;
                }
            }
        }
    }

    // Toits des salles et corridors (visibles depuis les remparts et les Tours), ceints de créneaux au-dessus des cours.
    static void BuildRoofs(Maze m, Kit kit)
    {
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (m.Void(i, j) || m.Reserved(i, j)) continue;
                float ceil = m.Ceil(i, j);
                Vector3 c = m.Center(i, j);
                float e = Cell * 0.5f + Wall * 0.5f;
                kit.walls.WorldQuad(new Vector3(c.x - e, ceil + 0.01f, c.z - e), new Vector3(c.x + e, ceil + 0.01f, c.z - e),
                    new Vector3(c.x + e, ceil + 0.01f, c.z + e), new Vector3(c.x - e, ceil + 0.01f, c.z + e), Vector3.up, 0.5f);
                Room room = m.RoomAt(i, j);
                if (room == null) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    bool edge = !m.InGrid(ni, nj) || m.Void(ni, nj) || m.RoomAt(ni, nj) != room && m.Ceil(ni, nj) < ceil;
                    if (!edge || !m.InGrid(ni, nj)) continue;
                    Vector3 dir = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                    Vector3 mid = c + dir * (Cell * 0.5f);
                    KitMerlons(kit, mid - side * (Cell * 0.5f), mid + side * (Cell * 0.5f), ceil, -0.35f, dir, null, kit.walls, false);
                }
            }
    }

    // Enceinte de la zone jusqu'au plafond gris (au-dessus des salles), et le plafond lui-même.
    static void BuildEnclosure(Maze m, Kit kit, float height, ZoneRefs previous)
    {
        float x0 = m.origin.x, z0 = m.origin.z, size = m.N * Cell;
        for (int k = 0; k < m.N; k++)
        {
            // Ouest, est, nord : au-dessus des salles de bord (les cours ont déjà leur mur jusqu'en haut).
            foreach (var (i, j, d) in new[] { (0, k, 1), (m.N - 1, k, 0), (k, m.N - 1, 2), (k, 0, 3) })
            {
                if (m.Void(i, j)) continue;
                float from = m.Reserved(i, j) ? WallHeight : m.Ceil(i, j);
                if (d == 3 && m.southHeights != null) from = Mathf.Max(from, m.southHeights[i]);
                if (height - from < 0.05f) continue;
                Vector3 c = m.Center(i, j) + new Vector3(DI[d], 0f, DJ[d]) * (Cell * 0.5f) + Vector3.up * ((from + height) * 0.5f);
                kit.Box(c, d < 2 ? new Vector3(Wall, height - from, Cell + Wall) : new Vector3(Cell + Wall, height - from, Wall));
            }
        }
        // Plafond gris (et sa collision : rien ne passe au-dessus, pas même un boulet de canon).
        kit.grey.WorldQuad(new Vector3(x0, height, z0), new Vector3(x0, height, z0 + size), new Vector3(x0 + size, height, z0 + size), new Vector3(x0 + size, height, z0), Vector3.down, 0.1f);
        kit.Collider(new Vector3(x0 + size * 0.5f, height + 0.5f, z0 + size * 0.5f), new Vector3(size, 1f, size));
    }

    // ---------- Rempart ----------

    static void BuildRampart(Maze m, ZoneRefs refs, RampartPlan plan, Transform root, Transform colliders, int leverId, System.Random rnd)
    {
        var kit = new Kit(root, colliders, "Z" + m.zone + "_Rempart" + leverId);
        float west = OriginX, east = OriginX + ZoneSize;
        // Passage (herses) : sur le plus long segment horizontal, loin de ses extrémités.
        int best = -1;
        for (int k = 0; k < plan.rects.Count; k++)
            if (plan.horizontal[k] && (best < 0 || plan.rects[k].width > plan.rects[best].width)) best = k;
        if (best < 0) best = 0;
        plan.gateSegment = best;
        Rect gr = plan.rects[best];
        bool gateAlongX = plan.horizontal[best];
        float gateAt = gateAlongX ? Rand(rnd, gr.xMin + 14f, gr.xMax - 14f) : Rand(rnd, gr.yMin + 14f, gr.yMax - 14f);
        plan.gate = gateAlongX ? new Vector2(gateAt, gr.center.y) : new Vector2(gr.center.x, gateAt);

        // Faces : les bords de la réunion des emprises (sans le mur d'enceinte), 1 m d'épaisseur, du sol à 20 m.
        var stairGaps = new List<Rect>();
        foreach (var (a, b, inward) in OutlineEdges(plan.rects, west, east, ZoneSouth(m.zone), ZoneSouth(m.zone) + ZoneSize))
        {
            bool alongX = Mathf.Abs(a.y - b.y) < 0.01f;
            float lo = alongX ? Mathf.Min(a.x, b.x) : Mathf.Min(a.y, b.y), hi = alongX ? Mathf.Max(a.x, b.x) : Mathf.Max(a.y, b.y);
            // Ouverture des herses sur les deux faces du segment du passage.
            var pieces = new List<Rect> { Rect.MinMaxRect(lo, 0f, hi, RampartHeight) };
            bool gateFace = alongX == gateAlongX && (alongX ? Mathf.Abs(a.y - gr.yMin) < 0.01f || Mathf.Abs(a.y - gr.yMax) < 0.01f : Mathf.Abs(a.x - gr.xMin) < 0.01f || Mathf.Abs(a.x - gr.xMax) < 0.01f)
                && gateAt > lo && gateAt < hi;
            if (gateFace) pieces = Subtract(pieces, new[] { Rect.MinMaxRect(gateAt - GateWidth * 0.5f, -1f, gateAt + GateWidth * 0.5f, GateHeight) });
            foreach (Rect p in pieces)
            {
                Vector3 c = alongX ? new Vector3(p.center.x, p.center.y, a.y + inward.y * 0.5f) : new Vector3(a.x + inward.x * 0.5f, p.center.y, p.center.x);
                kit.Box(c, alongX ? new Vector3(p.width, p.height, 1f) : new Vector3(1f, p.height, p.width));
            }
            // Meurtrières (fond noir) tous les 4 m, au rez-de-chaussée, au 1er étage.
            Vector3 outward = new Vector3(-inward.x, 0f, -inward.y);
            for (float t = lo + 3f; t < hi - 2f; t += 4f)
                foreach (float y in new[] { 2.5f, 13f })
                {
                    if (gateFace && Mathf.Abs(t - gateAt) < GateWidth) continue;
                    Vector3 p = alongX ? new Vector3(t, y, a.y) : new Vector3(a.x, y, t);
                    Vector3 side = alongX ? Vector3.right : Vector3.forward;
                    p += outward * 0.02f;
                    kit.dark.WorldQuad(p - side * 0.15f - Vector3.up * 0.9f, p + side * 0.15f - Vector3.up * 0.9f, p + side * 0.15f + Vector3.up * 0.9f, p - side * 0.15f + Vector3.up * 0.9f, outward, 1f);
                }
        }

        // Escaliers intérieurs : du rez-de-chaussée au 1er étage, puis du 1er étage au chemin de ronde, le long du segment du passage.
        float run = StairRun(RampartFloor);
        var interior = new List<(Vector3 foot, Vector3 dir, float y0, float y1)>();
        if (gateAlongX)
        {
            float z = gr.yMin + 1f + 1.3f;
            float xa = gateAt + GateWidth + 1f, xb = gateAt - GateWidth - 1f;
            if (xa + run < gr.xMax - 1f) interior.Add((new Vector3(xa, 0f, z), Vector3.right, 0f, RampartFloor));
            else interior.Add((new Vector3(xb, 0f, z), Vector3.left, 0f, RampartFloor));
            float z2 = gr.yMax - 1f - 1.3f;
            if (xb - run > gr.xMin + 1f) interior.Add((new Vector3(xb, 0f, z2), Vector3.left, RampartFloor, RampartHeight));
            else interior.Add((new Vector3(xa, 0f, z2), Vector3.right, RampartFloor, RampartHeight));
        }
        else
        {
            float x = gr.xMin + 1f + 1.3f, za = gateAt + GateWidth + 1f;
            interior.Add((new Vector3(x, 0f, za), Vector3.forward, 0f, RampartFloor));
            interior.Add((new Vector3(gr.xMax - 2.3f, 0f, za), Vector3.forward, RampartFloor, RampartHeight));
        }
        var holes10 = new List<Rect>();
        var holes20 = new List<Rect>();
        foreach (var (foot, dir, y0, y1) in interior)
        {
            KitStairs(kit, foot + Vector3.up * 0f, dir, 2.4f, y0, y1, y0 <= 0.01f);
            Vector3 head = foot + dir * run;
            Rect hole = MinMax(foot - new Vector3(Mathf.Abs(dir.z), 0f, Mathf.Abs(dir.x)) * 1.3f, head + new Vector3(Mathf.Abs(dir.z), 0f, Mathf.Abs(dir.x)) * 1.3f);
            (y1 > RampartFloor + 0.1f ? holes20 : holes10).Add(hole);
        }

        // Dalles du 1er étage et du chemin de ronde (sans recouvrement entre segments), percées au-dessus des escaliers.
        var pieces10 = new List<Rect>();
        for (int k = 0; k < plan.rects.Count; k++)
        {
            var own = new List<Rect> { plan.rects[k] };
            for (int j = 0; j < k; j++) own = Subtract(own, new[] { plan.rects[j] });
            pieces10.AddRange(own);
        }
        foreach (Rect p in Subtract(new List<Rect>(pieces10), holes10)) kit.Slab(p, RampartFloor, 0.4f);
        foreach (Rect p in Subtract(new List<Rect>(pieces10), holes20)) kit.Slab(p, RampartHeight, 0.6f);

        // Escaliers extérieurs, derrière le rempart (côté Neutral Room), vers le chemin de ronde : 3 à 5.
        int wanted = 3 + rnd.Next(3), placedBack = 0;
        var gapsByEdge = new List<(Vector3 a, Vector3 b, float from, float to)>();
        var candidates = new List<(Vector3 a, Vector3 b, Vector2 inward)>();
        foreach (var e in OutlineEdges(plan.rects, west, east, ZoneSouth(m.zone), ZoneSouth(m.zone) + ZoneSize))
        {
            Vector2 mid = (e.a + e.b) * 0.5f;
            Vector3 probe = new Vector3(mid.x - e.inward.x * 3f, 0f, mid.y - e.inward.y * 3f);
            if (m.CellAt(probe, out int pi, out int pj) && plan.nrSide.Contains(new Vector2Int(pi, pj))) candidates.Add((new Vector3(e.a.x, 0f, e.a.y), new Vector3(e.b.x, 0f, e.b.y), e.inward));
        }
        float backRun = StairRun(RampartHeight);
        for (int attempt = 0; attempt < 40 && placedBack < wanted && candidates.Count > 0; attempt++)
        {
            var (a, b, inward) = candidates[rnd.Next(candidates.Count)];
            float length = Vector3.Distance(a, b);
            if (length < backRun + 4f) continue;
            Vector3 dir = (b - a).normalized;
            float start = Rand(rnd, 2f, length - backRun - 2f);
            float end = start + backRun;
            if (gapsByEdge.Exists(g => Vector3.Distance(g.a, a) < 0.1f && start < g.to + 3f && end > g.from - 3f)) continue;
            Vector3 outward = new Vector3(-inward.x, 0f, -inward.y);
            Vector3 foot = a + dir * start + outward * 1.35f;
            KitStairs(kit, foot, dir, 2.4f, 0f, RampartHeight);
            gapsByEdge.Add((a, b, end - 3f, end + 0.5f));
            placedBack++;
        }

        // Créneaux du chemin de ronde, sur les deux bords (ouverts là où arrivent les escaliers extérieurs).
        foreach (var (a2, b2, inward) in OutlineEdges(plan.rects, west, east, ZoneSouth(m.zone), ZoneSouth(m.zone) + ZoneSize))
        {
            Vector3 a = new Vector3(a2.x, 0f, a2.y), b = new Vector3(b2.x, 0f, b2.y);
            var gaps = new List<(float, float)>();
            foreach (var g in gapsByEdge) if (Vector3.Distance(g.a, a) < 0.1f && Vector3.Distance(g.b, b) < 0.1f) gaps.Add((g.from, g.to));
            KitMerlons(kit, a, b, RampartHeight, 0.5f, new Vector3(inward.x, 0f, inward.y), gaps);
        }

        // Herses (de chaque côté du passage) et levier (1er étage ou chemin de ronde), reliés par des câbles noirs.
        var gates = new List<CastleGate>();
        Quaternion gateRot = gateAlongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 at = gateAlongX ? new Vector3(gateAt, 0f, gr.center.y + side * (RampartThick * 0.5f - 0.5f)) : new Vector3(gr.center.x + side * (RampartThick * 0.5f - 0.5f), 0f, gateAt);
            gates.Add(CastleGate.Create(root, at, gateRot, GateWidth, GateHeight));
        }
        refs.leverGates[leverId] = gates;
        bool onTop = rnd.NextDouble() < 0.5;
        float leverY = onTop ? RampartHeight : RampartFloor;
        Vector3 leverPos = gateAlongX ? new Vector3(gateAt + (rnd.NextDouble() < 0.5 ? -1f : 1f) * Rand(rnd, 6f, 10f), leverY, gr.center.y)
                                      : new Vector3(gr.center.x, leverY, gateAt + (rnd.NextDouble() < 0.5 ? -1f : 1f) * Rand(rnd, 6f, 10f));
        if (gateAlongX) leverPos.x = Mathf.Clamp(leverPos.x, gr.xMin + 3f, gr.xMax - 3f);
        else leverPos.z = Mathf.Clamp(leverPos.z, gr.yMin + 3f, gr.yMax - 3f);
        var lever = CastleLever.Create(root, leverId, leverPos, gateAlongX ? 0f : 90f);
        refs.levers.Add(lever);
        foreach (var g in gates)
        {
            // Le câble descend le long de la face intérieure, puis court sur le sol jusqu'au pied de la herse.
            Vector3 gp = g.transform.position;
            Vector3 wallSide = gateAlongX ? new Vector3(leverPos.x, 0f, gr.yMin + 1.05f) : new Vector3(gr.xMin + 1.05f, 0f, leverPos.z);
            var points = new List<Vector3>
            {
                leverPos + Vector3.up * 0.05f,
                new Vector3(wallSide.x, leverPos.y + 0.03f, wallSide.z),
                new Vector3(wallSide.x, 0.03f, wallSide.z),
                new Vector3(gateAlongX ? gp.x : wallSide.x, 0.03f, gateAlongX ? wallSide.z : gp.z),
                gp + Vector3.up * 0.03f,
            };
            lever.AddCable(KitRibbon(root, points, 0.06f));
        }

        // Tourelles décoratives (modèle Meshy de tour) aux angles du chemin de ronde, et lustres à l'intérieur.
        foreach (Rect r in plan.rects)
        {
            foreach (var corner in new[] { new Vector3(r.xMin + 1.5f, RampartHeight, r.yMin + 1.5f), new Vector3(r.xMax - 1.5f, RampartHeight, r.yMax - 1.5f) })
            {
                if (corner.x < west + 2f || corner.x > east - 2f || rnd.NextDouble() < 0.5) continue;
                var turret = GameArt.SpawnModel("TourChateau", root, 4.5f);
                if (turret != null) turret.transform.position = corner;
            }
            AddChandelier(root, new Vector3(r.center.x, RampartFloor - 0.4f, r.center.y), 14f, 1.3f, rnd);
            AddChandelier(root, new Vector3(r.center.x, RampartHeight - 0.6f, r.center.y), 14f, 1.3f, rnd);
        }
        kit.Flush();
    }

    // Bords de la réunion des rectangles (sans ceux posés sur le bord de la zone) : (début, fin, direction vers l'intérieur).
    static List<(Vector2 a, Vector2 b, Vector2 inward)> OutlineEdges(List<Rect> rects, float west, float east, float south, float north)
    {
        var result = new List<(Vector2, Vector2, Vector2)>();
        foreach (Rect r in rects)
        {
            var edges = new[]
            {
                (new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(0f, 1f)),
                (new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(0f, -1f)),
                (new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(1f, 0f)),
                (new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(-1f, 0f)),
            };
            foreach (var (a, b, inward) in edges)
            {
                bool alongX = inward.x == 0f;
                if (alongX && (Mathf.Abs(a.y - south) < 0.01f || Mathf.Abs(a.y - north) < 0.01f)) continue;
                if (!alongX && (Mathf.Abs(a.x - west) < 0.01f || Mathf.Abs(a.x - east) < 0.01f)) continue;
                // Retire les parties de l'arête à l'intérieur d'un autre rectangle.
                float lo = alongX ? a.x : a.y, hi = alongX ? b.x : b.y, line = alongX ? a.y : a.x;
                var spans = new List<Vector2> { new Vector2(lo, hi) };
                foreach (Rect o in rects)
                {
                    if (o == r) continue;
                    float oLo = alongX ? o.xMin : o.yMin, oHi = alongX ? o.xMax : o.yMax, cLo = alongX ? o.yMin : o.xMin, cHi = alongX ? o.yMax : o.xMax;
                    if (line <= cLo + 0.01f || line >= cHi - 0.01f) continue;
                    var next = new List<Vector2>();
                    foreach (var s in spans)
                    {
                        if (s.y <= oLo || s.x >= oHi) { next.Add(s); continue; }
                        if (s.x < oLo) next.Add(new Vector2(s.x, oLo));
                        if (s.y > oHi) next.Add(new Vector2(oHi, s.y));
                    }
                    spans = next;
                }
                foreach (var s in spans)
                    if (s.y - s.x > 0.3f)
                        result.Add(alongX ? (new Vector2(s.x, line), new Vector2(s.y, line), inward) : (new Vector2(line, s.x), new Vector2(line, s.y), inward));
            }
        }
        return result;
    }

    // ---------- Tours et Donjon ----------

    // Tour ronde (Tour ou Donjon) : mur percé d'une porte et de meurtrières, créneaux, escalier en colimaçon jusqu'au sommet, étages
    // intermédiaires une fois sur deux. Retourne le centre du sommet.
    static Vector3 BuildTower(Kit kit, RoundPlan plan, System.Random rnd, Transform root)
    {
        float R = plan.radius, H = plan.height;
        Vector3 c = plan.center;
        const int sides = 24;
        float doorAngle = Rand(rnd, 0f, 360f);
        var slits = new List<(int, float)>();
        int slitCount = 15 + rnd.Next(8);
        for (int k = 0; k < slitCount; k++) slits.Add((rnd.Next(sides), Rand(rnd, 4f, H - 3f)));
        KitRoundWall(kit, c, R, 0f, H, sides, new List<float> { doorAngle }, 2.6f, 3.2f, slits);
        // Escalier contre le mur, de la porte jusqu'au sommet.
        float rOut = R - 1.05f, rIn = R - 3.6f;
        float end = KitSpiral(kit, c, rIn, rOut, 0f, H, doorAngle + 25f);
        // Étages (à 10 et 20 m pour une Tour, 15 et 30 m pour le Donjon), une fois sur deux, au centre (l'escalier passe autour).
        float step = H / 3f;
        foreach (float level in new[] { step, step * 2f })
            if (rnd.NextDouble() < 0.5) KitDisc(kit, c, rIn - 0.05f, level, 0.35f, sides);
        // Sommet : dalle pleine, ouverte au-dessus de l'arrivée de l'escalier, créneaux tout autour.
        float arrivalSpan = 2.2f / SpiralRise * (SpiralTread / ((rIn + rOut) * 0.5f)) * Mathf.Rad2Deg;
        KitDisc(kit, c, R - 1f, H, 0.5f, sides, end - arrivalSpan, end + 2f, 0f);
        KitRoundMerlons(kit, c, R, H, sides);
        AddChandelier(root, c + Vector3.up * (H * 0.5f), 16f, 1.4f, rnd);
        return c + Vector3.up * H;
    }

    // Maison-Fou : bâtiment en forme de fou (pièce d'échecs) de 15 m, 8 m de diamètre ; on y entre par deux portes (une salle au sol,
    // sous un plafond à 8 m) ; au-dessus, le corps effilé, le col et la mitre.
    static void BuildMaisonFou(Kit kit, RoundPlan plan, System.Random rnd)
    {
        Vector3 c = plan.center;
        float R = plan.radius;
        const int sides = 16;
        float door = Rand(rnd, 0f, 360f);
        KitRoundWall(kit, c, R, 0f, 8f, sides, new List<float> { door, door + 180f }, 2.4f, 3f, null, 0.8f);
        KitDisc(kit, c, R - 0.4f, 8.2f, 0.4f, sides);
        // Corps effilé (anneaux de plus en plus étroits), col, mitre (pas de collision au-dessus de la salle).
        float y = 8.2f;
        foreach (var (radius, height) in new[] { (R * 0.85f, 1.6f), (R * 0.7f, 1.4f), (R * 0.95f, 0.4f), (R * 0.75f, 1.2f), (R * 0.6f, 1.2f), (R * 0.42f, 1f), (R * 0.22f, 0.8f) })
        {
            for (int k = 0; k < sides; k++)
            {
                float a = (k + 0.5f) * 360f / sides;
                float chord = 2f * radius * Mathf.Sin(Mathf.PI / sides) + 0.06f;
                Vector3 p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (radius - 0.3f) + Vector3.up * (y + height * 0.5f);
                kit.Turned(p, new Vector3(chord, height, 0.6f), a, false);
            }
            y += height;
        }
        // Fente de la mitre (fond noir) et bouton au sommet.
        var top = GameArt.Prim(PrimitiveType.Sphere, kit.root, c + Vector3.up * (y + 0.4f), Vector3.one * 0.9f, GameArt.CheckerWall);
        foreach (var col in top.GetComponentsInChildren<Collider>()) Object.Destroy(col);
    }

    // ---------- Mobilier des salles (modèles Meshy) ----------

    static readonly string[][] Furniture =
    {
        new[] { "Grille", "Menottes", "Latrines" },                 // cachot
        new[] { "Epee", "Masse", "Cible", "Mannequin" },             // salle d'armes
        new[] { "Table", "Chaise", "Chaise", "Chaise" },             // salle de banquet
        new[] { "Cadre", "Cadre" },                                  // hall (portraits)
        new[] { "Banc", "Banc", "Croix", "Autel" },                  // chapelle
    };

    static float ModelHeight(string model)
    {
        switch (model)
        {
            case "Grille": return 2.8f;
            case "Menottes": return 0.8f;
            case "Latrines": return 1.4f;
            case "Epee": return 1.1f;
            case "Masse": return 0.9f;
            case "Cible": return 1.6f;
            case "Mannequin": return 1.8f;
            case "Table": return 3.2f;
            case "Chaise": return 1.2f;
            case "Cadre": return 1.4f;
            case "Banc": return 2.6f;
            case "Croix": return 2.6f;
            case "Autel": return 2f;
            default: return 1f;
        }
    }

    static void FurnishCastle(Maze m, Transform root, Dictionary<Room, int> natures, System.Random rnd)
    {
        var props = new GameObject("Mobilier_Z" + m.zone).transform;
        props.SetParent(root, false);
        foreach (var kv in natures)
        {
            Room room = kv.Key;
            string[] set = Furniture[kv.Value];
            Vector3 min = m.Center(room.rect.xMin, room.rect.yMin) - new Vector3(Cell, 0f, Cell) * 0.5f, max = m.Center(room.rect.xMax - 1, room.rect.yMax - 1) + new Vector3(Cell, 0f, Cell) * 0.5f;
            int count = 2 + room.cells.Count;
            for (int k = 0; k < count; k++)
            {
                string model = set[rnd.Next(set.Length)];
                bool onWall = model == "Cadre" || model == "Menottes" || model == "Grille" || model == "Epee" || model == "Masse";
                Vector3 p;
                float yaw;
                if (onWall)
                {
                    // Contre un mur de la salle (cadres et armes accrochés, grilles et menottes contre la paroi).
                    int d = rnd.Next(4);
                    float t = (float)rnd.NextDouble();
                    p = d == 0 ? new Vector3(max.x - 0.35f, 0f, Mathf.Lerp(min.z + 1f, max.z - 1f, t)) : d == 1 ? new Vector3(min.x + 0.35f, 0f, Mathf.Lerp(min.z + 1f, max.z - 1f, t))
                      : d == 2 ? new Vector3(Mathf.Lerp(min.x + 1f, max.x - 1f, t), 0f, max.z - 0.35f) : new Vector3(Mathf.Lerp(min.x + 1f, max.x - 1f, t), 0f, min.z + 0.35f);
                    yaw = d == 0 ? -90f : d == 1 ? 90f : d == 2 ? 180f : 0f;
                    if (model == "Cadre" || model == "Epee" || model == "Masse") p.y = Rand(rnd, 1.4f, 2.4f);
                }
                else
                {
                    p = new Vector3(Rand(rnd, min.x + 1.5f, max.x - 1.5f), 0f, Rand(rnd, min.z + 1.5f, max.z - 1.5f));
                    yaw = Rand(rnd, 0f, 360f);
                }
                if (!m.CellAt(p, out int ci, out int cj) || m.RoomAt(ci, cj) != room || m.keepClear.Contains(new Vector2Int(ci, cj))) continue;
                // Plus grande dimension (armes couchées, tables, bancs, cadres...).
                var go = GameArt.SpawnModelFloor(model, props, ModelHeight(model));
                if (go == null) continue;
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                // Portrait d'un chien (skin) dans les cadres des halls.
                if (model == "Cadre") AddPortrait(go.transform, rnd);
                if (!onWall || model == "Grille")
                {
                    Bounds b = GameArt.LocalBounds(go.transform);
                    var col = go.AddComponent<BoxCollider>();
                    col.center = b.center;
                    col.size = b.size;
                }
            }
        }
    }

    // Image d'un skin de chien dans un cadre de hall (texture Resources/Art/Portraits si elle existe, sinon une toile sombre).
    static void AddPortrait(Transform frame, System.Random rnd)
    {
        Bounds b = GameArt.LocalBounds(frame);
        var tex = Resources.Load<Texture2D>("Art/Portraits/Portrait" + (rnd.Next(8) + 1));
        Material mat = tex != null ? GameArt.Lit("Portrait" + tex.name, Color.white, tex, 0.2f) : GameArt.Lit("PortraitSombre", new Color(0.12f, 0.1f, 0.1f), null, 0.2f);
        var quad = GameArt.Prim(PrimitiveType.Quad, frame, new Vector3(b.center.x, b.center.y, b.max.z - 0.02f), new Vector3(b.size.x * 0.7f, b.size.y * 0.7f, 1f), mat);
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (var c in quad.GetComponentsInChildren<Collider>()) Object.Destroy(c);
    }

    // Emplacements de monticules dans les cours (au sol, hors des douves et des escaliers).
    static void CourtyardSpots(Maze m, ZoneRefs refs, System.Random rnd)
    {
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (!m.Void(i, j) || rnd.NextDouble() > 0.35) continue;
                Vector3 p = m.Center(i, j) + new Vector3(Rand(rnd, -1.5f, 1.5f), 0f, Rand(rnd, -1.5f, 1.5f));
                if (!Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.95f || !hit.transform.IsChildOf(refs.structure)) continue;
                refs.itemSpots.Add(hit.point);
            }
    }
}
