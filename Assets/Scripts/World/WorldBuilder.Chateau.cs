using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Chapitre 11.3 : structure Château, posée dans une zone d'Échiquier (parcelles bleues d'un gabarit, WorldBuilder.Structure).
// V0.7.15 :
// - la douve est un ravin de 80 m de profondeur (comme celui du Blockhaus : y tomber tue), large de 10 m entre l'Échiquier et le
//   rempart ; des ponts suspendus (ceux du Blockhaus) la franchissent, de l'Échiquier aux entrées du rempart (3 par rempart,
//   espacées d'au moins 30 m). Côté Échiquier, sa paroi garde le damier, sans aucune fenêtre
//   (V0.7.16, Note 01) ;
// - remparts de 13 m (une cellule d'épaisseur, descendant jusqu'au fond du ravin), sur lesquels on ne peut ni monter ni sauter,
//   visibles malgré la distance et l'éclairage (briques lumineuses), surmontés de créneaux, percés côté ravin de deux rangées de
//   meurtrières en quinconce (une par cellule et par rangée) ; à chaque entrée, un passage de 4 x 5 m, la herse ouverte (1 m plus
//   bas qu'avant, on passe dessous) et une arche sur chaque face ;
// - plus aucun extérieur dans le Château (Note 09) : les abords des Tours et du Donjon et l'arrivée de chaque entrée sont des halls
//   couverts (plafond gris à 12 m, lustres), reliés aux salles et corridors voisins ; plus d'escalier vers les toits ni le rempart
//   (Note 05) ;
// - murs en briques noires et blanches, sols en damier, plafonds gris (béton) ; jusqu'à 6 Tours (30 m, 15 à 20 m de diamètre,
//   créneaux, au moins 15 meurtrières, escalier en colimaçon de 3 m de large jusqu'au dernier étage, sans accès au toit ; étages à 10
//   et 20 m une fois sur deux) et 1 Donjon (une Tour 50 % plus haute et plus large), au centre de la zone de préférence ; entre eux,
//   des salles de 8 à 12 m de haut (côtés de 5 à 20 m) et des corridors (15 % pour 85 % de salles ; 30 % des corridors ont un
//   renfoncement en biseau) : cachots, salles d'armes, salles de banquet, chapelles. Chaque entrée a son arche ; éclairage par des
//   lustres ; aucun objet décoratif ne flotte.
// V0.7.17 : 4 à 6 salles par Château suivent les schémas du GDD (cachot, salle d'armes, salle de banquet, chapelle ; orientés au
//   hasard) ; nouveaux objets Meshy (tonneaux, gros tonneaux, caisses, cible humaine, enclume, puits, seau) ; torches au mur des
//   cachots ; rez-de-chaussée des Tours meublé (enclume, puits une fois sur deux, tonneaux, caisses, seau).
// V0.7.18 : 11 schémas de salles (Notes temporaires 02) : les 4 premiers revus, 4 salles en longueur (15 x 30 m) et 3 salles en L ;
//   chaque salle sur schéma est unique dans le Château et prend la forme de son schéma. Plus d'épées ni de masses d'armes dans les
//   salles d'armes (Note 01).
// V0.7.19 : 3 à 5 salles sur schéma par Château ; croix orthodoxe (Meshy) ; pierres tombales dans les chapelles ; cimetières, colliers,
//   corniches, carreaux au plafond, arches des couloirs, objets plus gros, mannequins à tête de chien, tours de Kremlin : voir
//   WorldBuilder.Chateau719.
public static partial class WorldBuilder
{
    public const float ChateauHeight = 60f;
    // V0.7.15 : remparts de 13 m, douve en ravin de deux cellules (10 m), halls couverts à 12 m (la hauteur des plus hautes salles).
    const float RampartHeight = 13f, GateWidth = 4f, GateHeight = 5f, HallHeight = 12f;
    const int MoatCells = 2;
    const float TowerHeight = 30f;
    const int MaxTowers = 6;
    // Meurtrières des remparts (schéma de la Note 04) : rangée basse au milieu de chaque cellule, rangée haute entre deux cellules.
    const float SlitWidth = 0.5f * 1.3f, SlitHeight = 2.8f, SlitLow = 2.7f, SlitHigh = 7.9f;

    // Tour ronde posée dans la structure : centre, rayon, hauteur.
    internal class RoundPlan { public Vector3 center; public float radius, height; public int kind; public float doorAngle, stairInner; }   // kind : 0 Tour, 1 Donjon

    internal class ChateauPlan
    {
        public StructureSite site;
        public System.Random rnd;
        public readonly HashSet<Vector2Int> moat = new HashSet<Vector2Int>(), rampart = new HashSet<Vector2Int>();
        public readonly HashSet<Vector2Int> halls = new HashSet<Vector2Int>();      // halls couverts (cellules vides)
        public readonly HashSet<Vector2Int> yards = new HashSet<Vector2Int>();      // V0.7.19 : halls autour des Tours (cimetières)
        public readonly List<RoundPlan> rounds = new List<RoundPlan>();
        public readonly Dictionary<Room, int> natures = new Dictionary<Room, int>();
        public readonly List<(Room room, int layout, int turns)> templated = new List<(Room, int, int)>();   // V0.7.17 : salles sur schéma (V0.7.18 : disposition, posée tournée de 'turns')
        public readonly List<(Vector3 a, Vector3 b)> bridges = new List<(Vector3, Vector3)>();
    }

    // Avant l'Échiquier : structure, douve, rempart, entrées, Tours et Donjon, puis les salles et corridors du Château.
    static ChateauPlan PlanChateau(Maze m, int seed, int zone)
    {
        var plan = new ChateauPlan { rnd = new System.Random(seed * 37 + zone * 6151) };
        var rnd = plan.rnd;
        var site = plan.site = MarkStructure(m, TerrainKind.Chateau, Terrains.TemplateOf(seed, zone));
        int n = m.N;
        m.voidCell = new bool[n, n];
        m.moatCell = new bool[n, n];
        // Les murs voisins d'une cellule vide (halls, rempart) montent jusqu'au plafond des halls.
        m.voidCeil = HallHeight;
        m.voidWallFull = true;
        m.structureBevelChance = 0.3f;
        m.archChance = 1f;
        m.archDoorways = true;
        m.structureCeilingSlab = RoofSlab;
        m.structureCorridorCeil = 8f;
        m.brickWalls = true;
        m.greyCeilings = true;

        // Douve (à 1 et 2 cellules de l'Échiquier), rempart (à 3 cellules) ; pas autour de la Neutral Room ni devant sa porte.
        bool NearRoom(int i, int j) => m.neutralRoom.Contains(new Vector2Int(i, j)) || Mathf.Abs(i - m.doorCol) <= 2 && j >= n - 5;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (!m.InStructure(i, j) || m.Fixed(i, j) || NearRoom(i, j)) continue;
                var c = new Vector2Int(i, j);
                if (site.dist[i, j] <= MoatCells) { plan.moat.Add(c); m.moatCell[i, j] = true; }
                else if (site.dist[i, j] == MoatCells + 1) plan.rampart.Add(c);
                else continue;
                m.voidCell[i, j] = true;
            }
        // Entrées : une ligne droite par-dessus la douve et à travers le rempart, jusqu'à un hall.
        PickCrossings(m, site, MoatCells + 2, c =>
        {
            for (int k = 1; k <= MoatCells; k++) if (!plan.moat.Contains(c.At(k))) return false;
            var landing = c.At(MoatCells + 2);
            return plan.rampart.Contains(c.At(MoatCells + 1)) && !NearRoom(landing.x, landing.y) && !m.Fixed(landing.x, landing.y);
        }, rnd);
        foreach (var c in site.crossings)
        {
            var landing = c.At(MoatCells + 2);
            m.voidCell[landing.x, landing.y] = true;
            plan.halls.Add(landing);
        }

        // Tours et Donjon : loin des remparts, du Hub et de la Neutral Room ; un hall couvert les entoure.
        bool NearKeep(int i, int j) => Mathf.Abs(i - m.start.x) + Mathf.Abs(j - m.start.y) <= 2 || m.neutralRoom.Contains(new Vector2Int(i, j))
            || Mathf.Abs(i - m.doorCol) <= 2 && j >= m.N - 5;
        bool PlotFree(Vector3 center, float plot)
        {
            Vector3 o = m.origin;
            int i0 = Mathf.FloorToInt((center.x - plot - o.x) / Cell), i1 = Mathf.FloorToInt((center.x + plot - o.x) / Cell);
            int j0 = Mathf.FloorToInt((center.z - plot - o.z) / Cell), j1 = Mathf.FloorToInt((center.z + plot - o.z) / Cell);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    if (!m.InStructure(i, j) || m.Fixed(i, j) || site.dist[i, j] <= MoatCells + 1 || NearKeep(i, j) || plan.halls.Contains(new Vector2Int(i, j))) return false;
                    if (site.crossings.Exists(g => g.At(MoatCells + 2) == new Vector2Int(i, j) || g.At(MoatCells + 3) == new Vector2Int(i, j))) return false;
                }
            foreach (var other in plan.rounds)
                if (Flat(other.center - center) < other.radius + plot + 3f) return false;
            return true;
        }
        void MarkPlot(Vector3 center, float plot)
        {
            Vector3 o = m.origin;
            for (int i = Mathf.FloorToInt((center.x - plot - o.x) / Cell); i <= Mathf.FloorToInt((center.x + plot - o.x) / Cell); i++)
                for (int j = Mathf.FloorToInt((center.z - plot - o.z) / Cell); j <= Mathf.FloorToInt((center.z + plot - o.z) / Cell); j++)
                {
                    if (!m.InStructure(i, j)) continue;
                    m.voidCell[i, j] = true;
                    plan.halls.Add(new Vector2Int(i, j));
                    plan.yards.Add(new Vector2Int(i, j));
                }
        }
        // Cellules libres de la structure (pour tirer un centre).
        var inner = new List<Vector2Int>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (m.InStructure(i, j) && site.dist[i, j] > MoatCells + 1 && !m.Fixed(i, j)) inner.Add(new Vector2Int(i, j));
        bool TryRound(float radius, float height, int kind, Rect? area, int attempts)
        {
            if (inner.Count == 0) return false;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var cell = inner[rnd.Next(inner.Count)];
                Vector3 c = m.Center(cell.x, cell.y) + new Vector3(Rand(rnd, -2.5f, 2.5f), 0f, Rand(rnd, -2.5f, 2.5f));
                if (area.HasValue && !area.Value.Contains(new Vector2(c.x, c.z))) continue;
                if (!PlotFree(c, radius + 2f)) continue;
                MarkPlot(c, radius + 2f);
                plan.rounds.Add(new RoundPlan { center = c, radius = radius, height = height, kind = kind });
                return true;
            }
            return false;
        }
        // Le Donjon d'abord, au centre de la zone (les 4 parcelles du milieu) si la structure y passe ; sinon ailleurs, plus petit s'il
        // le faut.
        {
            float radius = Rand(rnd, 15f, 20f) * 1.5f * 0.5f;
            Vector2 lo = GridPoint(zone, new Vector2(1f, 1f)), hi = GridPoint(zone, new Vector2(3f, 3f));
            var center = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
            if (!TryRound(radius, TowerHeight * 1.5f, 1, center, 300) && !TryRound(radius * 0.8f, TowerHeight * 1.5f, 1, center, 200)
                && !TryRound(radius * 0.8f, TowerHeight * 1.5f, 1, null, 300))
                TryRound(radius * 0.6f, TowerHeight * 1.5f, 1, null, 300);
        }
        // Jusqu'à 6 Tours.
        int towers = 3 + rnd.Next(MaxTowers - 2);
        for (int k = 0; k < towers; k++) TryRound(Rand(rnd, 7.5f, 10f), TowerHeight, 0, null, 120);

        // Salles et corridors dans le reste de la structure.
        var style = new ZoneStyle
        {
            maxRooms = 400, minRoomSize = 1, maxRoomSize = 4, roomCoverage = 0.85f, pitChance = 0f, minPitDepth = 1.5f, maxPitDepth = 0f,
            maxLayerDepth = 0f, bridgeChance = 0f, maxHeight = 12f, floors = new[] { 0f }, ceilings = new[] { 8f, 9f, 10f, 11f, 12f },
            chaoticCeilings = new[] { 8f, 10f, 12f }, pitCeilings = new[] { 8f }, fixedRooms = true,
        };
        // V0.7.18 : 4 à 6 (V0.7.19 : 3 à 5) salles uniques, chacune sur un schéma différent parmi les 11 du GDD (tirés dans un ordre au hasard ; un
        // schéma qui ne trouve pas sa place cède la sienne au suivant), posées d'abord, à leur forme, tournées au hasard.
        var order = new List<int>();
        for (int k = 0; k < RoomTemplates.Length; k++) order.Add(k);
        for (int k = order.Count - 1; k > 0; k--)
        {
            int s = rnd.Next(k + 1);
            (order[k], order[s]) = (order[s], order[k]);
        }
        // Chaque schéma à sa taille normale d'abord, puis, s'il en manque, resserré (V0.7.18 : la place est comptée dans le Château).
        style.bigCount = MinTemplateRooms + rnd.Next(MaxTemplateRooms - MinTemplateRooms + 1);
        // Les schémas tirés pour ce Château, les plus grands posés d'abord (ils se casent moins bien) ; les autres ensuite, au cas où.
        int Area(int t) { int c = 0; foreach (bool b in TemplateMask(t)) if (b) c++; return c; }
        var drawn = order.GetRange(0, style.bigCount);
        drawn.Sort((x, y) => Area(y).CompareTo(Area(x)));
        order.RemoveRange(0, style.bigCount);
        order.InsertRange(0, drawn);
        var layouts = new List<int>(order);
        foreach (int t in order) layouts.Add(t + RoomTemplates.Length);
        style.bigShapes = layouts.ConvertAll(TemplateMask);
        style.bigGroups = layouts.ConvertAll(l => l % RoomTemplates.Length).ToArray();
        Generate(m, rnd, style, (i, j) => m.InStructure(i, j));
        foreach (Room room in m.rooms)
            if (!room.plain) { room.plain = true; plan.natures[room] = rnd.Next(RoomNatures); }
        foreach (var (room, shape, turns) in style.bigPlaced)
            if (room.cells.Count > 0) plan.templated.Add((room, layouts[shape], turns));
        foreach (var t in plan.templated)
        {
            plan.natures[t.room] = TemplateNature[t.layout % RoomTemplates.Length];
            LimitDoors(m, t.room, TemplateRoomDoors, rnd);
        }
        return plan;
    }

    // Point d'une grille de gabarit (4 x 4) en coordonnées monde (x, z).
    static Vector2 GridPoint(int zone, Vector2 g) => new Vector2(OriginX + g.x * ZoneSize / 4f, ZoneSouth(zone) + g.y * ZoneSize / 4f);

    // Après l'Échiquier : ouvertures des ponts dans le mur de l'Échiquier, passages des entrées, halls reliés aux salles, quelques
    // fenêtres de l'Échiquier sur la douve.
    static void FinishChateauPlan(Maze m, ChateauPlan plan)
    {
        foreach (var c in plan.site.crossings)
        {
            m.wallHoles[EdgeKey(c.e.x, c.e.y, c.d)] = (0f, WindowHigh);
            // Le long du pont et sous le rempart (chemins : Parfum Odorant).
            for (int k = 1; k <= MoatCells + 1; k++) SetWall(m, c.At(k).x, c.At(k).y, c.d, false);
            Vector3 dir = new Vector3(DI[c.d], 0f, DJ[c.d]), lift = Vector3.up * -0.02f;
            Vector3 a = m.Center(c.e.x, c.e.y) + dir * (Cell * 0.5f + Wall * 0.5f) + lift;
            var last = c.At(MoatCells);
            Vector3 b = m.Center(last.x, last.y) + dir * (Cell * 0.5f) + lift;
            plan.bridges.Add((a, b));
        }
        // Les halls voisins communiquent.
        foreach (var c in plan.halls)
            foreach (int d in new[] { 0, 2 })
                if (plan.halls.Contains(c + new Vector2Int(DI[d], DJ[d]))) SetWall(m, c.x, c.y, d, false);
        OpenCourtyards(m, plan.halls, plan.rnd);
        // V0.7.16 (Note 01) : plus de fenêtre de l'Échiquier sur la douve.
    }

    // Les halls s'ouvrent sur les salles et corridors voisins : tout ce qui se touche finit relié (chaque hall et chaque ensemble de
    // salles), puis environ une arête sur douze de plus.
    static void OpenCourtyards(Maze m, HashSet<Vector2Int> courts, System.Random rnd)
    {
        bool Town(int i, int j) => m.InStructure(i, j) && !m.Void(i, j) && !m.Reserved(i, j);
        var node = new Dictionary<Vector2Int, int>();
        int nodes = 0;
        // Ensembles de salles (cellules reliées sans mur).
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                var c = new Vector2Int(i, j);
                if (!Town(i, j) || node.ContainsKey(c)) continue;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(c);
                node[c] = nodes;
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        var nb = q + new Vector2Int(DI[d], DJ[d]);
                        if (!Town(nb.x, nb.y) || node.ContainsKey(nb) || HasWall(m, q.x, q.y, d)) continue;
                        node[nb] = nodes;
                        queue.Enqueue(nb);
                    }
                }
                nodes++;
            }
        // Halls (cellules vides voisines).
        foreach (var c in courts)
        {
            if (node.ContainsKey(c)) continue;
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(c);
            node[c] = nodes;
            while (queue.Count > 0)
            {
                var q = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    var nb = q + new Vector2Int(DI[d], DJ[d]);
                    if (!courts.Contains(nb) || node.ContainsKey(nb)) continue;
                    node[nb] = nodes;
                    queue.Enqueue(nb);
                }
            }
            nodes++;
        }
        var parent = new int[nodes];
        for (int k = 0; k < nodes; k++) parent[k] = k;
        int Find(int k) { while (parent[k] != k) k = parent[k] = parent[parent[k]]; return k; }
        var edges = new List<(Vector2Int town, int dir)>();
        foreach (var c in courts)
            for (int d = 0; d < 4; d++)
            {
                var t = c + new Vector2Int(DI[d], DJ[d]);
                if (Town(t.x, t.y) && !m.Fixed(t.x, t.y)) edges.Add((t, d ^ 1));
            }
        edges.Sort((a, b) => a.town.x != b.town.x ? a.town.x.CompareTo(b.town.x) : a.town.y != b.town.y ? a.town.y.CompareTo(b.town.y) : a.dir.CompareTo(b.dir));
        Shuffle(edges, rnd);
        foreach (var (town, dir) in edges)
        {
            var court = town + new Vector2Int(DI[dir], DJ[dir]);
            int a = Find(node[town]), b = Find(node[court]);
            if (a == b && rnd.NextDouble() > 0.08) continue;
            parent[a] = b;
            SetWall(m, town.x, town.y, dir, false);
        }
    }

    // Après la géométrie de l'Échiquier : halls, douve, remparts, entrées, enceinte et plafond, toits, Tours et Donjon, ponts.
    static IEnumerator BuildChateauSteps(Maze m, ChateauPlan plan, ZoneRefs refs, Transform root, Transform colliders, int zone)
    {
        var rnd = plan.rnd;
        var site = plan.site;
        var shell = new Kit(root, colliders, "Z" + zone + "_Chateau");
        var walls = new Kit(root, colliders, "Z" + zone + "_Remparts");
        var checker = new Kit(root, colliders, "Z" + zone + "_Douve");

        // Halls : sol, plafond gris à 12 m (percé pour les Tours).
        {
            var mask = new bool[m.N, m.N];
            foreach (var c in plan.halls) mask[c.x, c.y] = true;
            foreach (Rect r in MaskRects(mask, m.origin.x, m.origin.z, Cell)) shell.Floor(r, 0f);
        }
        BuildHallCeilings(m, plan, shell);
        BuildMoat(m, plan, checker, walls, rnd, root);
        BuildRamparts(m, plan, walls);
        yield return null;
        // Entrées : passage sous le rempart (herse ouverte, arche de chaque côté).
        foreach (var c in site.crossings) BuildCastleEntrance(m, c, root, walls);
        if (site.crossings.Count > 0)
        {
            var first = site.crossings[0];
            refs.structureEntry = m.Center(first.e.x, first.e.y);
        }
        // Enceinte au-dessus de l'Échiquier : le damier, comme lui.
        StructureShell(m, checker, ChateauHeight);
        BuildRoofs(m, shell);
        shell.Flush(GameArt.BrickWall);
        walls.Flush(RampartBrick);
        checker.Flush();
        yield return null;

        // Tours et Donjon.
        foreach (var round in plan.rounds)
        {
            var kit = new Kit(root, colliders, "Z" + zone + (round.kind == 1 ? "_Donjon" : "_Tour"));
            var (topFloor, roofAbove) = BuildTower(kit, round, rnd, root);
            // Cloche Décorative au dernier étage (chapitre 7.6), suspendue sous le toit ; c'est l'endroit le plus haut atteignable.
            Vector3 aside = Quaternion.Euler(0f, Rand(rnd, 0f, 360f), 0f) * Vector3.forward * Mathf.Min(2.5f, round.radius * 0.3f);
            refs.bellSpots.Insert(0, HangingBell(topFloor + aside, roofAbove));
            m.perches.Add((topFloor + Vector3.right * 2f, 0f, 3f));
            kit.Flush(GameArt.BrickWall);
            yield return null;
        }
        int bells = GameRules.HangingBellsInZone(zone);
        if (refs.bellSpots.Count > bells) refs.bellSpots.RemoveRange(bells, refs.bellSpots.Count - bells);

        // Lustres des halls : à chaque arrivée d'une entrée, et dans un tiers des autres cellules (hors des Tours).
        var landings = new HashSet<Vector2Int>();
        foreach (var c in site.crossings) landings.Add(c.At(MoatCells + 2));
        var hallList = new List<Vector2Int>(plan.halls);
        hallList.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        foreach (var c in hallList)
        {
            Vector3 p = m.Center(c.x, c.y);
            if (plan.rounds.Exists(r => Flat(r.center - p) < r.radius + 1.8f)) continue;
            if (!landings.Contains(c) && rnd.NextDouble() >= 0.35) continue;
            AddChandelier(root, p + Vector3.up * HallHeight, 14f, 1.2f, rnd);
        }

        // Ponts suspendus au-dessus de la douve.
        foreach (var (a, b) in plan.bridges) refs.bridges.Add(SuspendedBridge.Create(root, zone * 100 + 60 + refs.bridges.Count, a, b));
        // Dehors (luminosité ambiante relevée) : la douve seulement ; y tomber tue.
        refs.outdoors = p => m.CellAt(p, out int oi, out int oj) && plan.moat.Contains(new Vector2Int(oi, oj));
        refs.ravineAt = refs.outdoors;
    }

    // Plafond des halls (dessous gris, dessus en damier, dalle de 0,3 m), sur une grille de 0,5 m qui épargne l'intérieur des Tours :
    // il rejoint leur mur (1 m d'épaisseur) sans entrer dedans.
    static void BuildHallCeilings(Maze m, ChateauPlan plan, Kit kit)
    {
        const int sub = 10;
        const float unit = Cell / sub, slab = 0.3f;
        int n = m.N * sub;
        var mask = new bool[n, n];
        foreach (var c in plan.halls)
            for (int a = 0; a < sub; a++)
                for (int b = 0; b < sub; b++)
                {
                    int x = c.x * sub + a, z = c.y * sub + b;
                    float x0 = m.origin.x + x * unit, z0 = m.origin.z + z * unit;
                    bool inside = false;
                    foreach (var r in plan.rounds)
                    {
                        // Point du carré le plus proche du centre de la Tour.
                        float px = Mathf.Clamp(r.center.x, x0, x0 + unit), pz = Mathf.Clamp(r.center.z, z0, z0 + unit);
                        if (new Vector2(px - r.center.x, pz - r.center.z).magnitude < r.radius - 0.95f) { inside = true; break; }
                    }
                    mask[x, z] = !inside;
                }
        foreach (Rect r in MaskRects(mask, m.origin.x, m.origin.z, unit))
        {
            kit.grey.WorldQuad(new Vector3(r.xMin, HallHeight, r.yMin), new Vector3(r.xMin, HallHeight, r.yMax), new Vector3(r.xMax, HallHeight, r.yMax), new Vector3(r.xMax, HallHeight, r.yMin), Vector3.down, 0.1f);
            // V0.7.16 (Note 04) : dessus de la dalle 1 cm au-dessus du haut des murs (plus de scintillement).
            float topY = HallHeight + slab + SeamLift;
            kit.floors.WorldQuad(new Vector3(r.xMin, topY, r.yMin), new Vector3(r.xMax, topY, r.yMin), new Vector3(r.xMax, topY, r.yMax), new Vector3(r.xMin, topY, r.yMax), Vector3.up, 0.5f);
            kit.Collider(new Vector3(r.center.x, HallHeight + slab * 0.5f, r.center.y), new Vector3(r.width, slab, r.height));
        }
    }

    // Douve en ravin (V0.7.15, Note 04) : fond à -80 m ; ses parois sous l'Échiquier (et au-dessus de ce qu'il construit, jusqu'au
    // plafond du Château : StructureShell) gardent le damier ; le mur de l'Échiquier qui la borde se voit de loin et sans éclairage,
    // percé des départs des ponts. V0.7.16 (Note 01) : plus aucune fenêtre (ni factice, ni percée) côté Échiquier.
    static void BuildMoat(Maze m, ChateauPlan plan, Kit kit, Kit castle, System.Random rnd, Transform root)
    {
        var visible = new MeshBuilder();
        var sorted = new List<Vector2Int>(plan.moat);
        sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        foreach (var c in sorted)
        {
            Vector3 center = m.Center(c.x, c.y);
            var rect = Rect.MinMaxRect(center.x - Cell * 0.5f, center.z - Cell * 0.5f, center.x + Cell * 0.5f, center.z + Cell * 0.5f);
            kit.Floor(rect, -RavineReach, 1f);
            for (int d = 0; d < 4; d++)
            {
                var nb = new Vector2Int(c.x + DI[d], c.y + DJ[d]);
                if (plan.moat.Contains(nb) || plan.rampart.Contains(nb)) continue;
                bool boundary = !m.InGrid(nb.x, nb.y);
                bool echiquier = !boundary && !m.InStructure(nb.x, nb.y);
                Vector3 dir = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                Vector3 edge = center + dir * (Cell * 0.5f);
                Vector3 Size(float h) => d < 2 ? new Vector3(Wall, h, Cell + Wall) : new Vector3(Cell + Wall, h, Wall);
                // Sous le sol (de -80 à 0 m) ; au-dessus, l'Échiquier et l'enceinte. Contre la Neutral Room (dans la structure), la paroi
                // monte aussi de son plafond à celui du Château.
                // V0.7.16 (Note 04) : 1 cm au-dessus du sol de l'Échiquier (plus de scintillement au départ des ponts).
                kit.Box(edge + Vector3.up * ((-RavineReach + SeamLift) * 0.5f), Size(RavineReach + SeamLift));
                if (!boundary && !echiquier)
                {
                    float top = m.Ceil(nb.x, nb.y);
                    // V0.7.16 (Note 04) : 1 cm au-dessus du plafond gris qu'elle borde (plus de scintillement).
                    top += SeamLift;
                    if (ChateauHeight - top > 0.05f) castle.Box(edge + Vector3.up * ((top + ChateauHeight) * 0.5f), Size(ChateauHeight - top));
                    continue;
                }
                if (boundary) continue;
                float ceil = m.Ceil(nb.x, nb.y);
                // Mur de l'Échiquier visible de loin (damier lumineux), percé de ses ouvertures.
                var holes = new List<Rect>();
                if (m.wallHoles.TryGetValue(EdgeKey(c.x, c.y, d), out var hole)) holes.Add(Rect.MinMaxRect(-AerialHoleWidth * 0.5f, hole.low, AerialHoleWidth * 0.5f, hole.high));
                Vector3 face = edge - dir * (Wall * 0.5f + 0.015f);
                // V0.7.16 : du fond de la douve au plafond du Château (éclairage uniforme de la paroi, plus de parcelles sombres).
                foreach (Rect p in Subtract(new List<Rect> { Rect.MinMaxRect(-Cell * 0.5f - Wall * 0.5f, -RavineReach, Cell * 0.5f + Wall * 0.5f, Mathf.Max(ceil, ChateauHeight)) }, holes))
                    visible.WorldQuad(face + side * p.xMin + Vector3.up * p.yMin, face + side * p.xMax + Vector3.up * p.yMin,
                        face + side * p.xMax + Vector3.up * p.yMax, face + side * p.xMin + Vector3.up * p.yMax, -dir, 0.5f);
            }
        }
        // Au bord de la zone, la paroi descend jusqu'au fond (au-dessus : le mur du bord, puis l'enceinte).
        if (!visible.IsEmpty) GameArt.MeshObject("Z" + m.zone + "_FacadesDouve", root, visible.Build("FacadesDouve"), VisibleChecker);
    }

    // Remparts : une cellule d'épaisseur, de -80 m (le fond de la douve) à 13 m ; créneaux sur leurs deux bords ; côté douve, deux
    // rangées de meurtrières en quinconce, au même espacement partout (Note 04) : la basse au milieu de chaque cellule (sauf devant
    // une entrée), la haute au droit de chaque séparation entre deux cellules.
    static void BuildRamparts(Maze m, ChateauPlan plan, Kit walls)
    {
        int n = m.N;
        var all = new bool[n, n];
        var solid = new bool[n, n];
        var gates = new HashSet<Vector2Int>();
        foreach (var c in plan.site.crossings) gates.Add(c.At(MoatCells + 1));
        foreach (var c in plan.rampart)
        {
            all[c.x, c.y] = true;
            solid[c.x, c.y] = !gates.Contains(c);
        }
        float x0 = m.origin.x, z0 = m.origin.z;
        foreach (Rect r in MaskRects(solid, x0, z0, Cell))
            walls.Box(new Vector3(r.center.x, (RampartHeight - RavineReach) * 0.5f, r.center.y), new Vector3(r.width, RampartHeight + RavineReach, r.height));
        // Créneaux au sommet, sur ses deux bords.
        foreach (var (a, b, inward) in MaskEdges(all, x0, z0, Cell, (x, z) => m.InGrid(x, z) && !all[x, z] && m.InStructure(x, z)))
            KitMerlons(walls, new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y), RampartHeight, 0.3f, new Vector3(inward.x, 0f, inward.y), null, null, false);
        // Meurtrières, côté douve.
        foreach (var (a, b, inward) in MaskEdges(all, x0, z0, Cell, (x, z) => plan.moat.Contains(new Vector2Int(x, z))))
        {
            Vector3 start = new Vector3(a.x, 0f, a.y), end = new Vector3(b.x, 0f, b.y);
            float length = Vector3.Distance(start, end);
            Vector3 along = (end - start) / length, outward = -new Vector3(inward.x, 0f, inward.y);
            Vector3 face = start + outward * 0.02f;
            void Slit(float t, float bottom)
            {
                Vector3 p0 = face + along * (t - SlitWidth * 0.5f), p1 = face + along * (t + SlitWidth * 0.5f);
                walls.dark.WorldQuad(p0 + Vector3.up * bottom, p1 + Vector3.up * bottom, p1 + Vector3.up * (bottom + SlitHeight), p0 + Vector3.up * (bottom + SlitHeight), outward, 0.5f);
            }
            for (float t = Cell * 0.5f; t < length; t += Cell)
            {
                // Pas de meurtrière basse au-dessus de la herse d'une entrée.
                Vector3 mid = start + along * t + new Vector3(inward.x, 0f, inward.y) * (Cell * 0.5f);
                bool gate = m.CellAt(mid, out int gi, out int gj) && gates.Contains(new Vector2Int(gi, gj));
                if (!gate) Slit(t, SlitLow);
            }
            for (float t = Cell; t < length - 0.01f; t += Cell) Slit(t, SlitHigh);
        }
    }

    // Entrée d'un rempart : passage de 4 x 5 m sous le rempart (du fond de la douve jusqu'au sommet ailleurs), herse ouverte au milieu,
    // arche sur chaque face. Le pont suspendu y arrive depuis l'Échiquier.
    static void BuildCastleEntrance(Maze m, Crossing c, Transform root, Kit walls)
    {
        Vector3 dir = new Vector3(c.Step.x, 0f, c.Step.y), side = new Vector3(c.Step.y, 0f, c.Step.x);
        bool alongX = Mathf.Abs(dir.x) > 0.5f;
        var gateCell = c.At(MoatCells + 1);
        Vector3 boundary = m.Center(gateCell.x, gateCell.y) - dir * (Cell * 0.5f);
        Vector3 Along(float t, float s, float y) => boundary + dir * t + side * s + Vector3.up * y;
        Vector3 Size(float lengthAlong, float acrossSide, float h) => alongX ? new Vector3(lengthAlong, h, acrossSide) : new Vector3(acrossSide, h, lengthAlong);
        Rect Area(float t0, float t1, float s0, float s1)
        {
            Vector3 a = Along(t0, s0, 0f), b = Along(t1, s1, 0f);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }
        float t0 = 0f, t1 = Cell, mid = Cell * 0.5f, sideWall = (Cell - GateWidth) * 0.5f;
        // Sol du passage, et le bloc plein qui le porte jusqu'au fond de la douve.
        walls.Floor(Area(t0, t1, -GateWidth * 0.5f, GateWidth * 0.5f), 0f, 0.5f);
        walls.Box(Along(mid, 0f, (-RavineReach - 0.5f) * 0.5f), Size(Cell, GateWidth, RavineReach - 0.5f));
        foreach (float s in new[] { -1f, 1f })
            walls.Box(Along(mid, s * (GateWidth * 0.5f + sideWall * 0.5f), (RampartHeight - RavineReach) * 0.5f), Size(Cell, sideWall, RampartHeight + RavineReach));
        walls.Box(Along(mid, 0f, (GateHeight + RampartHeight) * 0.5f), Size(Cell, GateWidth, RampartHeight - GateHeight));
        Quaternion rot = alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
        foreach (float t in new[] { t0, t1 }) DoorArch(walls.walls, Along(t, 0f, 0f), rot, GateWidth, GateHeight, 0.6f);
        var gate = CastleGate.Create(root, Along(mid, 0f, 0f), rot, GateWidth, GateHeight);
        gate.SetOpen(true, true);
    }

    // Briques noires et blanches des remparts, lumineuses : on les voit malgré la distance et l'éclairage.
    static Material rampartBrick;
    static Material RampartBrick
    {
        get
        {
            if (rampartBrick != null) return rampartBrick;
            Material brick = GameArt.BrickWall;
            var mat = new Material(GameArt.Emissive("RempartLumineux", Color.white, Color.white)) { name = "RempartLumineux" };
            mat.mainTexture = brick.mainTexture;
            mat.color = brick.color;
            mat.SetTexture("_EmissionMap", brick.mainTexture);
            mat.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.55f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return rampartBrick = mat;
        }
    }

    // Dessus des toits : la collision du plafond (0,3 m).
    const float RoofSlab = 0.3f;

    // Toits des salles et corridors (vus depuis les Tours), ceints de créneaux au-dessus des salles plus basses. V0.7.15 (Note 05) : on
    // ne monte plus sur les toits (plus d'escalier) ; contre un hall, le mur monte jusqu'à son plafond et cache le toit.
    static void BuildRoofs(Maze m, Kit kit)
    {
        int n = m.N;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (!m.InStructure(i, j) || m.Void(i, j) || m.Reserved(i, j)) continue;
                float ceil = m.Ceil(i, j), roof = ceil + RoofSlab;
                Vector3 c = m.Center(i, j);
                float e = Cell * 0.5f + Wall * 0.5f;
                kit.floors.WorldQuad(new Vector3(c.x - e, roof, c.z - e), new Vector3(c.x + e, roof, c.z - e),
                    new Vector3(c.x + e, roof, c.z + e), new Vector3(c.x - e, roof, c.z + e), Vector3.up, 0.5f);
                // Tranche du toit au-dessus des murs, là où le voisin est plus bas.
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (!m.InGrid(ni, nj) || m.Void(ni, nj) || m.Reserved(ni, nj) || m.Ceil(ni, nj) >= ceil - 0.01f) continue;
                    Vector3 dir = new Vector3(DI[d], 0f, DJ[d]);
                    Vector3 fc = c + dir * (Cell * 0.5f) + Vector3.up * (ceil + RoofSlab * 0.5f);
                    // V0.7.20 : sans sa face du dessus, que couvre déjà le sol du toit (elles scintillaient l'une contre l'autre).
                    kit.walls.Box(fc, d < 2 ? new Vector3(Wall, RoofSlab, Cell + Wall) : new Vector3(Cell + Wall, RoofSlab, Wall), 0.5f, 63 & ~(1 << 2));
                }
                Room room = m.RoomAt(i, j);
                if (room == null) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (!m.InGrid(ni, nj) || m.Void(ni, nj) || m.RoomAt(ni, nj) == room || m.Ceil(ni, nj) >= ceil) continue;
                    Vector3 dir = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
                    Vector3 mid = c + dir * (Cell * 0.5f);
                    KitMerlons(kit, mid - side * (Cell * 0.5f), mid + side * (Cell * 0.5f), roof, -0.35f, dir, null, kit.walls, false);
                }
            }
    }

    // ---------- Tours et Donjon ----------

    // Tour ronde (Tour ou Donjon) : mur percé d'une porte (sous une arche) et de meurtrières, créneaux, escalier en colimaçon de 3 m
    // de large jusqu'au dernier étage, étages intermédiaires une fois sur deux. V0.7.14 : le toit est fermé (on ne peut ni y monter ni
    // sortir de la Tour par le haut) ; le dernier étage est 4 m dessous. Retourne le centre du dernier étage et sa hauteur sous toit.
    const float TowerTopRoom = 4f;

    static (Vector3 topFloor, float headroom) BuildTower(Kit kit, RoundPlan plan, System.Random rnd, Transform root)
    {
        float R = plan.radius, H = plan.height, top = H - TowerTopRoom;
        Vector3 c = plan.center;
        const int sides = 24;
        float doorAngle = Rand(rnd, 0f, 360f);
        var slits = new List<(int, float)>();
        int slitCount = 15 + rnd.Next(8);
        for (int k = 0; k < slitCount; k++) slits.Add((rnd.Next(sides), Rand(rnd, 4f, H - 3f)));
        KitRoundWall(kit, c, R, 0f, H, sides, new List<float> { doorAngle }, 2.6f, 3.2f, slits);
        RoundDoorArch(kit, c, R, doorAngle, 2.6f, 3.2f);
        // Escalier contre le mur, de la porte jusqu'au dernier étage.
        float rOut = R - 1.05f, rIn = rOut - TowerStairWidth;
        float end = KitSpiral(kit, c, rIn, rOut, 0f, top, doorAngle + 25f);
        plan.doorAngle = doorAngle;
        plan.stairInner = rIn;
        // Étages (à 10 et 20 m pour une Tour, 15 et 30 m pour le Donjon), une fois sur deux, au centre (l'escalier passe autour).
        float step = H / 3f, lowestCeiling = top;
        foreach (float level in new[] { step, step * 2f })
            if (level < top - 2.5f && rnd.NextDouble() < 0.5)
            {
                KitDisc(kit, c, rIn - 0.05f, level, TowerFloorThick, sides);
                lowestCeiling = Mathf.Min(lowestCeiling, level - TowerFloorThick);
            }
        // Dernier étage : dalle pleine, ouverte au-dessus de l'arrivée de l'escalier ; puis le toit fermé et les créneaux autour.
        // V0.7.16 (Note 03) : ouverture agrandie : elle couvre les 3,5 derniers mètres de montée (avant : 2,2 m, la tête d'un personnage
        // de 1,8 m butait sous la dalle de 0,5 m), plus un pan de marge (les pans de la dalle sont gardés ou retirés en entier).
        float arrivalSpan = TowerArrivalHeadroom / SpiralRise * (SpiralTread / ((rIn + rOut) * 0.5f)) * Mathf.Rad2Deg + 360f / sides;
        arrivalSpan = Mathf.Min(arrivalSpan, 200f);
        // V0.7.19 (Note 02) : même épaisseur que les étages intermédiaires (0,35 m), tranche visible le long de l'ouverture.
        KitDisc(kit, c, R - 1f, top, TowerFloorThick, sides, end - arrivalSpan, end + 2f, 0f);
        KitDisc(kit, c, R - 0.5f, H + 0.5f, 0.5f, sides);
        KitRoundMerlons(kit, c, R, H + 0.5f, sides);
        // Lustres : sous le premier plafond, et sous le toit (aucun décor ne flotte).
        AddChandelier(root, c + Vector3.up * lowestCeiling, 16f, 1.4f, rnd);
        if (lowestCeiling < top - 0.1f) AddChandelier(root, c + Vector3.up * H, 10f, 1.1f, rnd);
        return (c + Vector3.up * top, H - top);
    }

    const float TowerStairWidth = 3f, TowerArrivalHeadroom = 3.5f, TowerFloorThick = 0.35f;

    // Arche autour d'une porte percée dans un mur rond (épaisseur 1 m), face à l'angle 'angle'.
    static void RoundDoorArch(Kit kit, Vector3 center, float radius, float angle, float width, float height)
    {
        Quaternion rot = Quaternion.Euler(0f, angle, 0f);
        DoorArch(kit.walls, center + rot * Vector3.forward * (radius - 0.5f), rot, width, height, 1.3f);
    }

    // ---------- Mobilier des salles (modèles Meshy) ----------

    // V0.7.12 : cachots, salles d'armes, salles de banquet (avec les tableaux des anciens halls), chapelles. Où se pose chaque objet :
    // Wall : contre un mur (au sol) ; Near : au sol, près d'un mur ; Hung : accroché à un mur ; Floor : au sol, dans la salle.
    // V0.7.17 : nouveaux objets (Meshy) : tonneaux (banquet et Tours, au sol près d'un mur), caisses (banquet, cachot, Tours ; trois
    // modèles), cible humaine (salle d'armes), gros tonneaux couchés (banquet, contre les murs), enclume (une par rez-de-chaussée de
    // Tour), puits (au centre d'une Tour sur deux) et seaux (toutes les salles, au sol, près d'un autre objet) ; les torches reviennent,
    // au mur des cachots seulement.
    enum Spot { Wall, Near, Hung, Floor }

    static readonly string[][] Furniture =
    {
        new[] { "Grille", "Latrines", "Caisse" },                                                        // cachot (V0.7.20 : sans menottes)
        new[] { "Cible", "Mannequin", "CibleHumaine" },                                                   // salle d'armes (V0.7.18 : sans épées ni masses)
        new[] { "Table", "Chaise", "Chaise", "Chaise", "Cadre", "Tonneau", "Caisse", "TonneauGros" },     // salle de banquet
        new[] { "Banc", "Banc", "CroixOrthodoxe", "Autel", "PierreTombale" },                             // chapelle (V0.7.19 : pierres tombales)
    };
    const int RoomNatures = 4, Dungeon = 0;

    static Spot SpotOf(string model)
    {
        switch (model)
        {
            case "Grille": case "Latrines": case "TonneauGros": return Spot.Wall;
            case "Menottes": case "Autel": case "Tonneau": return Spot.Near;
            case "Cadre": return Spot.Hung;
            default: return Spot.Floor;
        }
    }

    // Facteur de taille qui double le volume (racine cubique de 2).
    const float VolumeX2 = 1.26f;

    static float ModelHeight(string model)
    {
        if (model.StartsWith("Caisse")) return 1f;
        switch (model)
        {
            case "Grille": return 2.8f;
            case "Menottes": return 0.8f;
            case "Latrines": return 1.4f;
            case "Cible": return 1.6f;
            case "Mannequin": return 1.8f;
            case "CibleHumaine": return 1.8f;
            case "Table": return 3.2f;
            case "Chaise": return 1.2f;
            case "Cadre": return 1.4f;
            case "Banc": return 2.6f;
            case "CroixOrthodoxe": return 2.6f;
            case "PierreTombale1": return 1.3f;     // V0.7.19 : stèle (hauteur)
            case "PierreTombale2": return 2.4f;     // V0.7.19 : tombeau et sa croix (longueur)
            case "Autel": return 2f;
            case "Tonneau": return 1.1f;
            case "TonneauGros": return 1.7f;
            case "Enclume": return 0.9f;
            case "Puit": return 2.8f * VolumeX2;     // V0.7.20 (Note 08) : volume doublé
            case "Seau": return 0.45f;
            default: return 1f;
        }
    }

    // Caisses : un des trois modèles.
    static string ModelVariant(string model, System.Random rnd) =>
        model == "Caisse" ? "Caisse" + (1 + rnd.Next(3)) : model == "PierreTombale" ? "PierreTombale" + (1 + rnd.Next(2)) : model;

    static void FurnishCastle(Maze m, Transform root, ChateauPlan plan, ZoneRefs refs)
    {
        var rnd = plan.rnd;
        var natures = plan.natures;
        var props = new GameObject("Mobilier_Z" + m.zone).transform;
        props.SetParent(root, false);
        // V0.7.17 : 4 à 6 salles suivent chacune un schéma du GDD. V0.7.18 : chaque schéma une seule fois, la salle ayant déjà sa
        // forme ; parmi les orientations qui gardent cette forme (quatre pour un schéma carré, deux en longueur, une en L), celle qui
        // laisse le plus d'objets en place (portes dégagées).
        var templatedRooms = new HashSet<Room>();
        foreach (var (room, layout, turns) in plan.templated)
        {
            templatedRooms.Add(room);
            bool[,] shape = TemplateMask(layout), placedShape = TurnMask(shape, turns);
            var allowed = new List<int>();
            for (int k = 0; k < 4; k++) if (SameMask(TurnMask(shape, k), placedShape)) allowed.Add(k);
            for (int k = allowed.Count - 1; k > 0; k--)
            {
                int sw = rnd.Next(k + 1);
                (allowed[k], allowed[sw]) = (allowed[sw], allowed[k]);
            }
            int best = turns, bestSkips = int.MaxValue;
            foreach (int k in allowed)
            {
                int skips = FurnishFromTemplate(m, room, layout, k, props, rnd, true);
                if (skips < bestSkips) { best = k; bestSkips = skips; }
            }
            FurnishFromTemplate(m, room, layout, best, props, rnd, false);
        }
        foreach (var kv in natures)
        {
            Room room = kv.Key;
            if (room.cells.Count == 0 || templatedRooms.Contains(room)) continue;
            string[] set = Furniture[kv.Value];
            bool narrow = NarrowRoom(room, CastleNarrowSide);
            var placed = new List<Vector3>();
            var floorProps = new List<GameObject>();
            int count = 2 + room.cells.Count;
            for (int k = 0; k < count; k++)
            {
                string model = ModelVariant(set[rnd.Next(set.Length)], rnd);
                Spot spot = SpotOf(model);
                // V0.7.19 : salle de moins de 10 m de côté : tout objet touche un mur.
                if (narrow && spot == Spot.Floor) spot = Spot.Near;
                var cell = room.cells[rnd.Next(room.cells.Count)];
                if (m.keepClear.Contains(cell) || m.archCells.Contains(cell)) continue;
                var go = GameArt.SpawnModelFloor(model, props, ModelHeight(model));
                if (go == null) continue;
                DressProp(go, model, rnd);
                bool ok = spot == Spot.Floor
                    ? PlaceOnFloor(m, room, go, cell, 0f, Rand(rnd, 0f, 360f), placed, rnd) && FitInCell(m, room, go, cell)
                    : PlaceAgainstWall(m, go, cell, rnd.Next(4), spot == Spot.Hung ? Rand(rnd, 1.5f, 2.1f) : 0f, spot == Spot.Near ? 0.25f : 0.02f, placed, rnd);
                if (!ok) { Object.Destroy(go); continue; }
                // Portrait d'un chien (skin) dans les tableaux.
                if (model == "Cadre") AddPortrait(go.transform, rnd);
                if (model == "Grille") SinkGrille(go);
                if (spot != Spot.Hung) { AddPropCollider(go); floorProps.Add(go); }
            }
            // Un seau au sol, près d'un autre objet (V0.7.19 : contre un mur dans une salle étroite).
            if (narrow)
            {
                var bucket = GameArt.SpawnModelFloor("Seau", props, ModelHeight("Seau"));
                var cell = room.cells[rnd.Next(room.cells.Count)];
                if (bucket != null && (m.keepClear.Contains(cell) || !PlaceAgainstWall(m, bucket, cell, rnd.Next(4), 0f, 0.05f, placed, rnd))) Object.Destroy(bucket);
                else if (bucket != null) AddPropCollider(bucket);
            }
            else if (floorProps.Count > 0) AddBucketNear(m, room, floorProps[rnd.Next(floorProps.Count)], props, rnd);
            // Cachots : une ou deux torches au mur, à flamme blanche.
            if (kv.Value == Dungeon) AddRoomTorches(m, room, props, placed, 1 + rnd.Next(2), rnd);
        }
        foreach (var round in plan.rounds) FurnishTower(round, props, rnd);
        DressCastle(m, root, plan, refs, props);
    }

    // V0.7.20 : la grille des cachots entre dans le sol jusqu'à sa barre du bas : ses piques inférieurs (13,7 % de la hauteur du
    // modèle, sous cette barre) ne se voient plus.
    const float GrilleSpikes = 0.137f;

    static void SinkGrille(GameObject go) => go.transform.position += Vector3.down * (GameArt.LocalBounds(go.transform).size.y * GrilleSpikes);

    static void AddPropCollider(GameObject go)
    {
        Bounds b = GameArt.LocalBounds(go.transform);
        var col = go.AddComponent<BoxCollider>();
        col.center = b.center;
        col.size = b.size;
    }

    // Torches au mur d'une salle (cellule sans décor réservé, mur plein), à 2,4 m du sol.
    static void AddRoomTorches(Maze m, Room room, Transform props, List<Vector3> placed, int torches, System.Random rnd)
    {
        for (int attempt = 0; attempt < 16 && torches > 0; attempt++)
        {
            var cell = room.cells[rnd.Next(room.cells.Count)];
            int d = rnd.Next(4);
            if (m.keepClear.Contains(cell) || !HasWall(m, cell.x, cell.y, d) || m.HasWallHole(cell.x, cell.y, d)) continue;
            Vector3 n = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
            Vector3 p = m.Center(cell.x, cell.y) + n * (Cell * 0.5f - Wall * 0.5f) + side * Rand(rnd, -1.2f, 1.2f) + Vector3.up * (room.floor + 2.4f);
            if (placed.Exists(q => Flat(q - p) < 1.4f)) continue;
            placed.Add(p);
            AddTorch(props, p, -n, true, rnd);
            torches--;
        }
    }

    // Seau posé au sol à côté d'un objet (dans la même cellule de la salle, sans entrer dans les murs).
    static void AddBucketNear(Maze m, Room room, GameObject near, Transform props, System.Random rnd)
    {
        Bounds nb = WorldBounds(near.transform);
        var go = GameArt.SpawnModelFloor("Seau", props, ModelHeight("Seau"));
        if (go == null) return;
        Vector3 dir = Quaternion.Euler(0f, Rand(rnd, 0f, 360f), 0f) * Vector3.forward;
        float reach = Mathf.Abs(dir.x) * nb.extents.x + Mathf.Abs(dir.z) * nb.extents.z + 0.35f;
        go.transform.SetPositionAndRotation(new Vector3(nb.center.x, nb.min.y, nb.center.z) + dir * reach, Quaternion.Euler(0f, Rand(rnd, 0f, 360f), 0f));
        if (room != null)
        {
            Vector3 p = go.transform.position;
            var cell = new Vector2Int(Mathf.FloorToInt((p.x - m.origin.x) / Cell), Mathf.FloorToInt((p.z - m.origin.z) / Cell));
            if (m.RoomAt(cell.x, cell.y) != room || !FitInCell(m, room, go, cell)) { Object.Destroy(go); return; }
        }
        AddPropCollider(go);
    }

    // ---------- Salles sur schéma (V0.7.17, V0.7.18) ----------

    // Schémas du GDD (11.3), la ligne 0 au "nord" du schéma. Symboles : 1 Tableau, 3 Tonneau, 4 Caisse, A Autel, B Banc d'église,
    // C Chaise, CH Cible Humaine, Ci Cible d'archerie, Cr Croix, En Enclume, G Grille, L Latrine, Ma Mannequin, Me Menotte, P Puit,
    // S Seau, Ta Table, To Torche, TG Gros tonneau ; "." : case vide ; "x" : hors de la salle (salles en L).
    // V0.7.18 : 11 schémas (les 4 premiers revus ; plus d'épées ni de masses d'armes).
    static readonly string[][] RoomTemplates =
    {
        new[]   // Template 1 : cachot (une cellule fermée par une rangée de grilles, ouverte d'un côté)
        {
            "L . Me . . . G . . .",
            "Me . . . . . G . . To",
            ". . . . . . . . . .",
            "G G G G G G G . . .",
            ". . . . . . . . . .",
            ". . . . . . . . . .",
            ". . . . . . . . . .",
            ". . . . Ta . . . . .",
            "To . . . C . . . . To",
            ". . To . . . . To . .",
        },
        new[]   // Template 2 : salle d'armes
        {
            ". . 4 4 4 3 . . . .",
            ". . . . . . . . . .",
            ". . . . . . . . . .",
            "Ma . . Ma . . Ma . . Ma",
            ". . . . . . . . . .",
            ". . . . . . . . . .",
            "Ma . . Ma . . Ma . . Ma",
            ". . . . . . . . . .",
            ". . . . . . . . . 3",
            ". . Ci . Ci . Ci . 3 3",
        },
        new[]   // Template 3 : salle de banquet (deux longues tables)
        {
            ". . TG 3 . . . . . .",
            ". . . . . . . . . .",
            ". C Ta C . . . . . 1",
            ". C Ta C . . . . . .",
            ". C Ta C . . C Ta C .",
            ". C Ta C . . C Ta C .",
            "1 . . . . . C Ta C .",
            ". . . . . . C Ta C 1",
            ". . . . . . . . . .",
            ". . 1 . . . 3 TG 3 .",
        },
        new[]   // Template 4 : chapelle (l'autel au nord, les bancs tournés vers lui)
        {
            ". 4 Cr . . . . . . .",
            ". . . . A Cr . . . .",
            "Cr . . . . . . . . Cr",
            ". . B B . . B B . .",
            "Cr . . . . . . . . Cr",
            ". . B B . . B B . Cr",
            "Cr . . . . . . . . .",
            "Cr . B B . . B B . .",
            ". . . . . . . . . Cr",
            ". . . . . . . . . .",
        },
        new[]   // Template 5 : cachot en longueur (deux rangées de grilles)
        {
            ". . . . . 4 3",
            "Me . . . . . To",
            ". . . . . . .",
            "G G G G G . .",
            ". . 3 . . . .",
            "L . . . . . Me",
            "L . . . . . Me",
            "To . . 3 3 . .",
            ". . G G G G G",
            ". . . . 4 . .",
            "L . . . . . Me",
            "L . . . . . Me",
            "To . . . . . .",
            "3 4 . . . . .",
        },
        new[]   // Template 6 : salle d'armes en longueur
        {
            ". Ci . Ci . Ci .",
            ". . . . . . .",
            "Ma . . . . . Ma",
            ". . . . . . .",
            ". . . . . . .",
            "Ma . . . . . Ma",
            ". . . . . . .",
            ". . . . . . .",
            "Ma . . . . . Ma",
            ". . . . . . .",
            ". . . . . . .",
            "Ma . . . . . Ma",
            ". . . . . . .",
            "3 . . 4 3 4 3",
        },
        new[]   // Template 7 : salle de banquet en longueur (une longue table double)
        {
            ". . TG . . TG 3 .",
            ". . . . . . . .",
            ". . . . . . . 1",
            ". . C Ta Ta C . .",
            "1 . C Ta Ta C . .",
            ". . C Ta Ta C . .",
            ". . C Ta Ta C . .",
            ". . C Ta Ta C . 1",
            ". . C Ta Ta C . .",
            ". . C Ta Ta C . .",
            ". . C Ta Ta C . .",
            "1 . . . . . . 1",
            ". . . . . . . .",
            ". . 3 TG TG . . .",
        },
        new[]   // Template 8 : chapelle en longueur (deux autels au nord)
        {
            "Cr Cr . . . . . Cr",
            ". . . A A . . .",
            "Cr . . . . . . .",
            ". . . . . . . Cr",
            ". . B B B B . .",
            ". . . . . . . Cr",
            "Cr . B B B B . Cr",
            ". . . . . . . .",
            "Cr . B B B B . Cr",
            "Cr . . . . . . .",
            ". . B B B B . .",
            "Cr . . . . . . Cr",
            ". . . . . . . Cr",
            "Cr 4 . . . 4 . Cr",
        },
        new[]   // Template 9 : cachot en L
        {
            "L L . . . To L L . . . . G 3 .",
            ". . . . . . . . . . . . G . .",
            ". . . . . . . . . . . . G 3 .",
            "G G G G G G G G G G . . G . .",
            ". 4 3 . 4 . . . . . . . . . .",
            ". . . . . . . . 4 . 4 3 . 4 To",
            "G G G G . . . x x x x x x x x",
            "L . . G . . 4 x x x x x x x x",
            "L . . G . . . x x x x x x x x",
            "To . . . . . 4 x x x x x x x x",
            ". . . . . . 3 x x x x x x x x",
            "G G G G . . 4 x x x x x x x x",
            "3 3 . . . . To x x x x x x x x",
            ". . . . . . . x x x x x x x x",
        },
        new[]   // Template 10 : salle de banquet en L (un puits)
        {
            "4 . 4 3 TG . . . 1 . . . . 1",
            ". . . . . . . C C C C C C .",
            "3 . . . . . . Ta Ta Ta Ta Ta Ta .",
            "4 . . P . . . Ta Ta Ta Ta Ta Ta .",
            ". . . . . . . C C C C C C .",
            ". . . . . . . . . . 1 . . .",
            "1 . . . . . . x x x x x x x",
            ". C Ta Ta C . . x x x x x x x",
            ". C Ta Ta C . . x x x x x x x",
            ". C Ta Ta C . 1 x x x x x x x",
            "1 C Ta Ta C . . x x x x x x x",
            ". . . . . . . x x x x x x x",
        },
        new[]   // Template 11 : chapelle en L (les bancs autour de l'autel)
        {
            ". . Cr . . Cr Cr . . . . Cr",
            "Cr B B B . . . . . Cr . .",
            ". B . . . . . . . . . .",
            ". B . A . . . . Cr . . .",
            "Cr . . . . B . . . . . Cr",
            "Cr . . . B B . Cr . . Cr .",
            ". Cr . . . . . x x x x x",
            ". . . . . . Cr x x x x x",
            "Cr . . . . . . x x x x x",
            "Cr . . . Cr . Cr x x x x x",
        },
    };
    // Découpage de chaque schéma en cellules de 5 m, par tronçons (cases, cellules) : en colonnes, puis en lignes. Une case fait
    // environ 2 m (de 1,9 à 2,5 m) ; les retours des salles en L tombent sur une limite de cellule. Une "disposition" est l'indice
    // dans ces tableaux : le schéma (disposition % 11) à sa taille normale, puis (à partir de 11) resserré.
    static readonly int[][] TemplateCols =
    {
        new[] { 10, 4 }, new[] { 10, 4 }, new[] { 10, 4 }, new[] { 10, 4 },
        new[] { 7, 3 }, new[] { 7, 3 }, new[] { 8, 3 }, new[] { 8, 3 },
        new[] { 7, 3, 8, 3 }, new[] { 7, 3, 7, 3 }, new[] { 7, 3, 5, 2 },
        // Versions resserrées (cases de 1,25 à 2,5 m), quand la place manque :
        new[] { 10, 3 }, new[] { 10, 3 }, new[] { 10, 3 }, new[] { 10, 3 },
        new[] { 7, 2 }, new[] { 7, 2 }, new[] { 8, 3 }, new[] { 8, 3 },
        new[] { 7, 2, 8, 3 }, new[] { 7, 3, 7, 2 }, new[] { 7, 2, 5, 2 },
    };
    static readonly int[][] TemplateRows =
    {
        new[] { 10, 4 }, new[] { 10, 4 }, new[] { 10, 4 }, new[] { 10, 4 },
        new[] { 14, 6 }, new[] { 14, 6 }, new[] { 14, 6 }, new[] { 14, 6 },
        new[] { 6, 3, 8, 3 }, new[] { 6, 3, 6, 3 }, new[] { 6, 3, 4, 2 },
        new[] { 10, 3 }, new[] { 10, 3 }, new[] { 10, 3 }, new[] { 10, 3 },
        new[] { 14, 5 }, new[] { 14, 5 }, new[] { 14, 5 }, new[] { 14, 5 },
        new[] { 6, 2, 8, 3 }, new[] { 6, 2, 6, 3 }, new[] { 6, 2, 4, 2 },
    };
    // Nature de chaque schéma : 0 cachot, 1 salle d'armes, 2 salle de banquet, 3 chapelle.
    static readonly int[] TemplateNature = { 0, 1, 2, 3, 0, 1, 2, 3, 0, 2, 3 };
    const int MinTemplateRooms = 3, MaxTemplateRooms = 5, TemplateRoomDoors = 2;   // V0.7.19 : 3 à 5 (avant : 4 à 6)

    static int SegSquares(int[] seg) { int s = 0; for (int k = 0; k < seg.Length; k += 2) s += seg[k]; return s; }
    static int SegCells(int[] seg) { int s = 0; for (int k = 1; k < seg.Length; k += 2) s += seg[k]; return s; }

    // Position (en cellules) d'une coordonnée du schéma (en cases) le long d'un axe découpé en tronçons.
    static float SegPos(int[] seg, float s)
    {
        float cells = 0f;
        for (int k = 0; k < seg.Length; k += 2)
        {
            if (s <= seg[k] || k + 2 >= seg.Length) return cells + s * seg[k + 1] / seg[k];
            s -= seg[k];
            cells += seg[k + 1];
        }
        return cells;
    }

    // Taille (en m) de la case 'square' le long d'un axe découpé en tronçons.
    static float SegSize(int[] seg, int square)
    {
        for (int k = 0; k < seg.Length; k += 2)
        {
            if (square < seg[k] || k + 2 >= seg.Length) return Cell * seg[k + 1] / seg[k];
            square -= seg[k];
        }
        return Cell;
    }

    // V0.7.19 : jeu de schémas (ceux du Château, ceux du Blockhaus) et leur découpage en cellules.
    internal class TemplateSet
    {
        public string[][] templates;
        public int[][] cols, rows;
    }

    static TemplateSet chateauSet;
    static TemplateSet ChateauSet => chateauSet ??= new TemplateSet { templates = RoomTemplates, cols = TemplateCols, rows = TemplateRows };

    // Cases d'un schéma : [colonne, ligne] (null : case vide) ; 'inside' : la case appartient à la salle.
    static string[,] ParseTemplate(int layout, out bool[,] inside, TemplateSet set = null)
    {
        set ??= ChateauSet;
        int w = SegSquares(set.cols[layout]), h = SegSquares(set.rows[layout]);
        var grid = new string[w, h];
        inside = new bool[w, h];
        string[] rows = set.templates[layout % set.templates.Length];
        for (int r = 0; r < h; r++)
        {
            string[] tokens = rows[r].Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int c = 0; c < w; c++)
            {
                string t = c < tokens.Length ? tokens[c] : ".";
                inside[c, r] = t != "x";
                grid[c, r] = t != "." && t != "x" ? t : null;
            }
        }
        return grid;
    }

    // Forme d'une disposition en cellules ([colonne, ligne], la ligne 0 au nord).
    static bool[,] TemplateMask(int layout) => TemplateMask(layout, null);

    static bool[,] TemplateMask(int layout, TemplateSet set)
    {
        set ??= ChateauSet;
        int[] cols = set.cols[layout], rows = set.rows[layout];
        var mask = new bool[SegCells(cols), SegCells(rows)];
        ParseTemplate(layout, out bool[,] inside, set);
        for (int c = 0; c < inside.GetLength(0); c++)
            for (int r = 0; r < inside.GetLength(1); r++)
                if (inside[c, r]) mask[(int)SegPos(cols, c + 0.5f), (int)SegPos(rows, r + 0.5f)] = true;
        return mask;
    }

    // Salle sur schéma : ses ouvertures vers les cellules voisines (hors de la salle) sont refermées, sauf 'keep' d'entre elles ;
    // une ouverture reste aussi si la refermer couperait une partie de la structure du reste.
    static void LimitDoors(Maze m, Room room, int keep, System.Random rnd)
    {
        var openings = new List<(int i, int j, int d)>();
        foreach (var c in room.cells)
            for (int d = 0; d < 4; d++)
            {
                int ni = c.x + DI[d], nj = c.y + DJ[d];
                if (!m.InGrid(ni, nj) || m.RoomAt(ni, nj) == room || HasWall(m, c.x, c.y, d)) continue;
                openings.Add((c.x, c.y, d));
            }
        for (int k = openings.Count - 1; k > 0; k--)
        {
            int s = rnd.Next(k + 1);
            (openings[k], openings[s]) = (openings[s], openings[k]);
        }
        int open = openings.Count, parts = StructureParts(m);
        foreach (var (i, j, d) in openings)
        {
            if (open <= keep) break;
            SetWall(m, i, j, d, true);
            if (StructureParts(m) > parts) SetWall(m, i, j, d, false);
            else open--;
        }
    }

    // Nombre de parties de la structure (cellules ni vides ni réservées) reliées sans traverser de mur.
    static int StructureParts(Maze m)
    {
        int n = m.N, parts = 0;
        var seen = new bool[n, n];
        var queue = new Queue<Vector2Int>();
        bool Inside(int i, int j) => m.InGrid(i, j) && m.InStructure(i, j) && !m.Void(i, j) && !m.Reserved(i, j) && !m.Fixed(i, j);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (seen[i, j] || !Inside(i, j)) continue;
                parts++;
                seen[i, j] = true;
                queue.Enqueue(new Vector2Int(i, j));
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = q.x + DI[d], nj = q.y + DJ[d];
                        if (!Inside(ni, nj) || seen[ni, nj] || HasWall(m, q.x, q.y, d)) continue;
                        seen[ni, nj] = true;
                        queue.Enqueue(new Vector2Int(ni, nj));
                    }
                }
            }
        return parts;
    }

    // Une salle meublée selon un schéma, tourné de 'turns' quarts de tour ; chaque case fait environ 2 m (tronçons du schéma). Les
    // objets du bord de la salle (bords du L compris) sont plaqués contre le mur ; ceux qui tomberaient devant une porte ne sont pas
    // posés. Retourne le nombre d'objets du schéma qui n'ont pas pu être posés ; 'dryRun' : compte seulement, sans rien poser.
    static int FurnishFromTemplate(Maze m, Room room, int layout, int turns, Transform props, System.Random rnd, bool dryRun)
    {
        int skipped = 0;
        int[] cols = TemplateCols[layout], rows = TemplateRows[layout];
        int W = SegSquares(cols), H = SegSquares(rows), CW = SegCells(cols), CH = SegCells(rows);
        string[,] grid = ParseTemplate(layout, out bool[,] inside);
        bool In(int c, int r) => c >= 0 && r >= 0 && c < W && r < H && inside[c, r];
        string At(int c, int r) => In(c, r) ? grid[c, r] : null;
        int Run(int c, int r, int dc, int dr, string sym)
        {
            int k = 0;
            while (In(c + dc * (k + 1), r + dr * (k + 1)) && (sym == null || At(c + dc * (k + 1), r + dr * (k + 1)) == sym)) k++;
            return k;
        }

        float left = m.Center(room.rect.xMin, room.rect.yMin).x - Cell * 0.5f;
        float top = m.Center(room.rect.xMin, room.rect.yMax - 1).z + Cell * 0.5f;
        float floorY = room.floor;

        // Point du schéma (en cellules, v vers le sud) -> salle, tourné de 'turns' quarts de tour : (u, v) -> (H - v, u) ; la ligne 0
        // est au nord (+Z).
        Vector3 ToWorld(float u, float v)
        {
            float w = CW, h = CH;
            for (int k = 0; k < turns; k++) { float t = u; u = h - v; v = t; float s = w; w = h; h = s; }
            return new Vector3(left + u * Cell, floorY, top - v * Cell);
        }
        Vector3 TurnDir(int dc, int dr) { for (int k = 0; k < turns; k++) { int t = dc; dc = -dr; dr = t; } return new Vector3(dc, 0f, -dr); }

        // Autels du schéma (les bancs se tournent vers le plus proche).
        var altars = new List<Vector2Int>();
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
                if (At(c, r) == "A") altars.Add(new Vector2Int(c, r));

        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                string sym = At(c, r);
                if (sym == null) continue;
                // Bord le plus proche (dans la forme du schéma) : l'objet lui tourne le dos ; sur une case du bord, il est plaqué
                // contre ce mur.
                int dTop = Run(c, r, 0, -1, null), dBottom = Run(c, r, 0, 1, null), dLeft = Run(c, r, -1, 0, null), dRight = Run(c, r, 1, 0, null);
                int edge = Mathf.Min(Mathf.Min(dTop, dBottom), Mathf.Min(dLeft, dRight));
                int fc, fr;
                if (edge == dTop) { fc = 0; fr = 1; }
                else if (edge == dBottom) { fc = 0; fr = -1; }
                else if (edge == dLeft) { fc = 1; fr = 0; }
                else { fc = -1; fr = 0; }
                bool against = edge == 0;
                Vector3 pos = ToWorld(SegPos(cols, c + 0.5f), SegPos(rows, r + 0.5f));
                float su = SegSize(cols, c), sv = SegSize(rows, r);
                float sx = (turns & 1) == 0 ? su : sv, sz = (turns & 1) == 0 ? sv : su;
                Vector3 facing = TurnDir(fc, fr);
                // Mur derrière l'objet (à l'opposé de son regard) : coordonnée de sa face intérieure, dans la cellule de la case.
                Vector3 back = -facing;
                bool backX = Mathf.Abs(back.x) > 0.5f;
                var cell = new Vector2Int(Mathf.FloorToInt((pos.x - m.origin.x) / Cell), Mathf.FloorToInt((pos.z - m.origin.z) / Cell));
                Vector3 cc = m.Center(cell.x, cell.y);
                float wallCoord = backX ? cc.x + Mathf.Sign(back.x) * (Cell * 0.5f - Wall * 0.5f) : cc.z + Mathf.Sign(back.z) * (Cell * 0.5f - Wall * 0.5f);

                // Pas devant une ouverture de la salle (porte, arche) ; contre un mur plein seulement.
                if (m.RoomAt(cell.x, cell.y) != room || m.keepClear.Contains(cell) || NearOpening(m, room, cell, pos)) { skipped++; continue; }
                if (against)
                {
                    int d = backX ? (back.x > 0f ? 0 : 1) : (back.z > 0f ? 2 : 3);
                    if (!HasWall(m, cell.x, cell.y, d) || m.HasWallHole(cell.x, cell.y, d)) { skipped++; continue; }
                }
                if (dryRun) continue;

                if (sym == "To")
                {
                    Vector3 wp = backX ? new Vector3(wallCoord, pos.y, pos.z) : new Vector3(pos.x, pos.y, wallCoord);
                    AddTorch(props, wp + Vector3.up * 2.4f, facing, true, rnd);
                    continue;
                }
                string model = TemplateModel(sym, rnd);
                if (model == null) continue;
                float size = ModelHeight(model);
                if (model == "Table" || model == "Banc") size = Mathf.Min(size, Mathf.Min(sx, sz));
                GameObject go;
                if (model == "Grille")
                {
                    // Rangée de grilles : chaque grille couvre sa case le long de la rangée (elles se touchent).
                    bool row = At(c - 1, r) == "G" || At(c + 1, r) == "G";
                    bool col = At(c, r - 1) == "G" || At(c, r + 1) == "G";
                    if (row || col)
                    {
                        facing = row ? TurnDir(0, 1) : TurnDir(1, 0);
                        against = false;
                    }
                    go = GameArt.SpawnModel("Grille", props, size);
                    if (go == null) continue;
                    Vector3 run = new Vector3(Mathf.Abs(facing.z), 0f, Mathf.Abs(facing.x));
                    float span = run.x > 0.5f ? sx : sz;
                    Bounds gb = GameArt.LocalBounds(go.transform);
                    if (gb.size.x >= gb.size.z) go.transform.localScale = new Vector3(span / Mathf.Max(0.01f, gb.size.x), 1f, 1f);
                    else { go.transform.localScale = new Vector3(1f, 1f, span / Mathf.Max(0.01f, gb.size.z)); facing = run; }
                }
                else
                {
                    go = GameArt.SpawnModelFloor(model, props, size);
                    if (go == null) continue;
                    DressProp(go, model, rnd);
                    if (sym == "C")
                    {
                        // Chaise tournée vers la table voisine.
                        for (int d = 0; d < 4; d++)
                            if (At(c + DI[d], r - DJ[d]) == "Ta") { facing = TurnDir(DI[d], -DJ[d]); break; }
                    }
                    else if (sym == "Ta")
                    {
                        // Tables bout à bout le long de leur plus longue rangée.
                        bool vertical = Run(c, r, 0, -1, "Ta") + Run(c, r, 0, 1, "Ta") > Run(c, r, -1, 0, "Ta") + Run(c, r, 1, 0, "Ta");
                        Vector3 longAxis = vertical ? TurnDir(0, 1) : TurnDir(1, 0);
                        Bounds tb = GameArt.LocalBounds(go.transform);
                        facing = tb.size.z >= tb.size.x ? longAxis : new Vector3(longAxis.z, 0f, -longAxis.x);
                    }
                    else if (sym == "B")
                    {
                        // Bancs tournés vers l'autel le plus proche (au nord du schéma s'il n'y en a pas).
                        facing = TurnDir(0, -1);
                        int bestD = int.MaxValue;
                        foreach (var a in altars)
                        {
                            int dc = a.x - c, dr = a.y - r, dist = dc * dc + dr * dr;
                            if (dist >= bestD) continue;
                            bestD = dist;
                            facing = Mathf.Abs(dr) >= Mathf.Abs(dc) ? TurnDir(0, dr > 0 ? 1 : -1) : TurnDir(dc > 0 ? 1 : -1, 0);
                        }
                    }
                }
                go.transform.SetPositionAndRotation(pos + (model == "Cadre" ? Vector3.up * 1.8f : Vector3.zero), Quaternion.LookRotation(facing));
                if (against)
                {
                    // Le dos de l'objet contre le mur (un peu d'écart pour les objets "près d'un mur").
                    float gap = model == "Menottes" || model == "Autel" || model == "Tonneau" ? 0.25f : 0.03f;
                    Bounds wb = WorldBounds(go.transform);
                    float reach = Vector3.Dot(wb.center, back) + Mathf.Abs(back.x) * wb.extents.x + Mathf.Abs(back.z) * wb.extents.z;
                    float target = backX ? wallCoord * back.x : wallCoord * back.z;
                    go.transform.position += back * (target - gap - reach);
                }
                if (model == "Grille") SinkGrille(go);
                if (model == "Cadre") AddPortrait(go.transform, rnd);
                else AddPropCollider(go);
            }
        return skipped;
    }

    // Modèle d'un symbole de schéma (null : symbole sans modèle).
    static string TemplateModel(string sym, System.Random rnd)
    {
        switch (sym)
        {
            case "1": return "Cadre";
            case "3": return "Tonneau";
            case "4": return ModelVariant("Caisse", rnd);
            case "A": return "Autel";
            case "B": return "Banc";
            case "C": return "Chaise";
            case "CH": return "CibleHumaine";
            case "Ci": return "Cible";
            case "Cr": return "CroixOrthodoxe";
            case "PT": return ModelVariant("PierreTombale", rnd);
            case "En": return "Enclume";
            case "G": return "Grille";
            case "L": return "Latrines";
            case "Ma": return "Mannequin";
            case "Me": return null;                  // V0.7.20 (Note 09) : plus de menottes, même sur un schéma
            case "P": return "Puit";
            case "S": return "Seau";
            case "Ta": return "Table";
            case "TG": case "Tg": return "TonneauGros";
            default: return null;
        }
    }

    // Le point est à moins de 2,2 m d'une ouverture de la salle (côté de sa cellule sans mur, ou mur percé, donnant hors de la salle).
    static bool NearOpening(Maze m, Room room, Vector2Int cell, Vector3 pos)
    {
        Vector3 c = m.Center(cell.x, cell.y);
        for (int d = 0; d < 4; d++)
        {
            int ni = cell.x + DI[d], nj = cell.y + DJ[d];
            if (m.InGrid(ni, nj) && m.RoomAt(ni, nj) == room) continue;
            if (HasWall(m, cell.x, cell.y, d) && !m.HasWallHole(cell.x, cell.y, d)) continue;
            float toSide = Cell * 0.5f - (DI[d] != 0 ? (pos.x - c.x) * DI[d] : (pos.z - c.z) * DJ[d]);
            if (toSide < 2.2f) return true;
        }
        return false;
    }

    // ---------- Rez-de-chaussée des Tours (V0.7.17) ----------

    // Une enclume, un puits au centre une fois sur deux, quelques tonneaux et caisses au bord de la pièce (contre l'escalier) et un
    // seau ; rien devant la porte ni au pied de l'escalier.
    static void FurnishTower(RoundPlan tower, Transform props, System.Random rnd)
    {
        Vector3 c = tower.center;
        float inner = tower.stairInner - 0.2f;
        if (inner < 2.5f) return;
        var placed = new List<(Vector3 p, float r)>();
        bool Free(Vector3 p, float r)
        {
            Vector3 v = p - c;
            float angle = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            float a = Mathf.DeltaAngle(tower.doorAngle, angle);
            if (a > -40f && a < 70f) return false;
            return !placed.Exists(q => Flat(q.p - p) < q.r + r + 0.3f);
        }
        GameObject Put(string model, Vector3 p, float yaw)
        {
            var go = GameArt.SpawnModelFloor(model, props, ModelHeight(model));
            if (go == null) return null;
            DressProp(go, model, rnd);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            Bounds b = WorldBounds(go.transform);
            placed.Add((new Vector3(b.center.x, c.y, b.center.z), Mathf.Max(b.extents.x, b.extents.z)));
            AddPropCollider(go);
            return go;
        }
        if (rnd.NextDouble() < 0.5)
        {
            var well = Put("Puit", c, Rand(rnd, 0f, 360f));
            if (well != null)
            {
                // Le puits ne déborde pas sur le passage au pied de l'escalier.
                Bounds b = WorldBounds(well.transform);
                float r = Mathf.Max(b.extents.x, b.extents.z), max = inner * 0.45f;
                if (r > max)
                {
                    well.transform.localScale *= max / r;
                    placed[placed.Count - 1] = (placed[placed.Count - 1].p, max);
                }
            }
        }
        var objects = new List<string> { "Enclume" };
        int barrels = 1 + rnd.Next(3), crates = 1 + rnd.Next(2);
        for (int k = 0; k < barrels; k++) objects.Add("Tonneau");
        for (int k = 0; k < crates; k++) objects.Add(ModelVariant("Caisse", rnd));
        GameObject last = null;
        foreach (string model in objects)
        {
            float size = ModelHeight(model) * 0.6f;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float a = Rand(rnd, 0f, 360f);
                // L'enclume dans la pièce, le reste au bord.
                float rad = model == "Enclume" ? Rand(rnd, 2f, Mathf.Max(2.1f, inner - 1.4f)) : inner - size;
                Vector3 p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * rad;
                if (!Free(p, size)) continue;
                var go = Put(model, p, model == "Enclume" ? Rand(rnd, 0f, 360f) : a + 180f);
                if (go != null) last = go;
                break;
            }
        }
        if (last != null) AddBucketNear(null, null, last, props, rnd);
    }

    // V0.7.13 (Note 24) : l'objet posé au sol reste dans sa cellule, sans entrer dans les murs (ni boucher une porte) : il est
    // repoussé à l'intérieur ; s'il est trop grand pour la cellule, il n'est pas posé.
    static bool FitInCell(Maze m, Room room, GameObject go, Vector2Int cell)
    {
        Bounds wb = WorldBounds(go.transform);
        Vector3 c = m.Center(cell.x, cell.y), shift = Vector3.zero;
        float lim = Cell * 0.5f - Wall * 0.5f - 0.08f;
        bool Closed(int d)
        {
            int ni = cell.x + DI[d], nj = cell.y + DJ[d];
            return !m.InGrid(ni, nj) || m.RoomAt(ni, nj) != room || HasWall(m, cell.x, cell.y, d);
        }
        if (Closed(0) && Closed(1) && wb.size.x > lim * 2f || Closed(2) && Closed(3) && wb.size.z > lim * 2f) return false;
        if (Closed(0) && wb.max.x > c.x + lim) shift.x -= wb.max.x - (c.x + lim);
        if (Closed(1) && wb.min.x + shift.x < c.x - lim) shift.x += (c.x - lim) - (wb.min.x + shift.x);
        if (Closed(2) && wb.max.z > c.z + lim) shift.z -= wb.max.z - (c.z + lim);
        if (Closed(3) && wb.min.z + shift.z < c.z - lim) shift.z += (c.z - lim) - (wb.min.z + shift.z);
        go.transform.position += shift;
        return true;
    }

    // Boîte englobante (monde) des rendus d'un objet.
    static Bounds WorldBounds(Transform t)
    {
        var b = new Bounds(t.position, Vector3.zero);
        bool any = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    // Objet posé au sol, quelque part dans la cellule (assez loin des murs et des autres objets).
    static bool PlaceOnFloor(Maze m, Room room, GameObject go, Vector2Int cell, float y, float yaw, List<Vector3> placed, System.Random rnd)
    {
        Vector3 c = m.Center(cell.x, cell.y);
        Vector3 p = new Vector3(c.x + Rand(rnd, -1f, 1f), y, c.z + Rand(rnd, -1f, 1f));
        if (placed.Exists(q => Flat(q - p) < 1.6f)) return false;
        go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
        placed.Add(p);
        return true;
    }

    // Objet contre le mur 'd' de la cellule (mur plein, sans ouverture) : son dos touche la face du mur ('gap' près), à la hauteur 'y'
    // au-dessus du sol de la cellule, tourné vers l'intérieur.
    static bool PlaceAgainstWall(Maze m, GameObject go, Vector2Int cell, int d, float y, float gap, List<Vector3> placed, System.Random rnd, float floorY = 0f)
    {
        if (!HasWall(m, cell.x, cell.y, d) || m.HasWallHole(cell.x, cell.y, d)) return false;
        Vector3 n = new Vector3(DI[d], 0f, DJ[d]), side = new Vector3(DJ[d], 0f, DI[d]);
        Quaternion rot = Quaternion.Euler(0f, d == 0 ? -90f : d == 1 ? 90f : d == 2 ? 180f : 0f, 0f);
        go.transform.rotation = rot;
        Bounds b = GameArt.LocalBounds(go.transform);
        // Étendue de l'objet le long de la normale du mur et le long du mur.
        Vector3 ln = Quaternion.Inverse(rot) * n, ls = Quaternion.Inverse(rot) * side;
        float depth = Mathf.Abs(ln.x) * b.extents.x + Mathf.Abs(ln.z) * b.extents.z, width = Mathf.Abs(ls.x) * b.extents.x + Mathf.Abs(ls.z) * b.extents.z;
        float centerN = Vector3.Dot(rot * b.center, n), centerS = Vector3.Dot(rot * b.center, side);
        float reach = Cell * 0.5f - Wall * 0.5f - 0.15f - width;
        if (reach < 0f) return false;
        Vector3 c = m.Center(cell.x, cell.y);
        float wallPlane = Cell * 0.5f - Wall * 0.5f;
        float along = Rand(rnd, -reach, reach);
        Vector3 p = c + n * (wallPlane - gap - depth - centerN) + side * (along - centerS) + Vector3.up * (floorY + y);
        if (placed.Exists(q => Flat(q - p) < Mathf.Max(1.2f, width * 2f))) return false;
        go.transform.position = p;
        placed.Add(p);
        return true;
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

    // Emplacements de monticules dans les halls (au sol).
    static void CourtyardSpots(Maze m, ZoneRefs refs, HashSet<Vector2Int> courts, System.Random rnd)
    {
        var sorted = new List<Vector2Int>(courts);
        sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        foreach (var c in sorted)
        {
            if (rnd.NextDouble() > 0.35) continue;
            Vector3 p = m.Center(c.x, c.y) + new Vector3(Rand(rnd, -1.5f, 1.5f), 0f, Rand(rnd, -1.5f, 1.5f));
            if (!Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.95f || !hit.transform.IsChildOf(refs.structure)) continue;
            if (hit.point.y > m.origin.y + 0.3f) continue;   // V0.7.17 : pas sur le mobilier (puits, tonneaux...)
            refs.itemSpots.Add(hit.point);
        }
    }
}
