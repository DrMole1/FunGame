using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// V0.7.11 (chapitre 11.4) : environnement Blockhaus. Interconnexion d'appartements et de couloirs sur 4 étages de 10 m (de -20 m à
// +20 m : 20 m de hauteur et 20 m de profondeur), damier noir et blanc. 20 % de couloirs pour 80 % de pièces d'appartements.
// - Entre un couloir et un appartement, une porte entrouverte que l'on pousse ; 10 % des portes sont fermées à clé (cadenas).
// - Des cages d'escalier (escaliers en colimaçon) relient les étages ; joueurs et entités les empruntent.
// - 20 % des appartements font 10 m de plus en hauteur (ils rognent l'étage supérieur) ; d'autres, des plateformes chaotiques.
// - Couloirs éclairés par des néons ; 25 % des appartements ne sont pas éclairés. 30 % des couloirs ont un renfoncement en biseau.
// - Un ravin de 40 m de large (12 gabarits) coupe la zone en deux, de -80 m à +80 m ; des fenêtres s'ouvrent sur lui à chaque étage
//   (on le franchit en Canon, en visant une fenêtre d'en face) ; plus haut et plus bas, des fenêtres factices. Y tomber tue.
public static partial class WorldBuilder
{
    const float StoreyHeight = 10f, BlockhausDepth = 20f, RavineHalfWidth = 20f, RavineReach = 80f;
    static readonly float[] StoreyLevels = { -20f, -10f, 0f, 10f };
    const int HubStorey = 2;

    static readonly Vector2[][] Ravines =
    {
        new[] { new Vector2(0, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 3), new Vector2(1, 3), new Vector2(1, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(3, 2), new Vector2(3, 3), new Vector2(4, 3) },
        new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(3, 2), new Vector2(3, 1), new Vector2(4, 1) },
        new[] { new Vector2(0, 3), new Vector2(2, 3), new Vector2(2, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 3), new Vector2(4, 3) },
        new[] { new Vector2(0, 1), new Vector2(2, 1), new Vector2(2, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 1), new Vector2(4, 1) },
        new[] { new Vector2(1, 4), new Vector2(1, 2), new Vector2(4, 2) },
        new[] { new Vector2(0, 2), new Vector2(3, 2), new Vector2(3, 4) },
        new[] { new Vector2(1, 4), new Vector2(1, 2), new Vector2(3, 2), new Vector2(3, 4) },
    };

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    static IEnumerator BuildBlockhausSteps(ZoneBuild build, Transform parent, int seed, int zone, ZoneRefs previous)
    {
        var rnd = new System.Random(seed * 43 + zone * 5381);
        int n = ZoneCells;
        Maze main = CreateMaze(seed, zone);

        // ---------- Plan commun aux 4 étages : ravin, cages d'escalier ----------
        Vector2[] ravine = Ravines[rnd.Next(Ravines.Length)];
        var ravineCells = new HashSet<Vector2Int>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                // Le ravin ne mord ni sur le Hub et son entrée, ni sur la Neutral Room.
                if (j <= HubRows + 1 || main.neutralRoom.Contains(new Vector2Int(i, j)) || Mathf.Abs(i - main.doorCol) <= 2 && j >= n - 5) continue;
                Vector3 c = main.Center(i, j);
                var p = new Vector2(c.x, c.z);
                for (int k = 0; k + 1 < ravine.Length; k++)
                    if (DistanceToSegment(p, GridPoint(zone, ravine[k]), GridPoint(zone, ravine[k + 1])) <= RavineHalfWidth) { ravineCells.Add(new Vector2Int(i, j)); break; }
            }
        var nrSide = Flood(main, new Vector2Int(main.doorCol, n - 2), ravineCells);
        // Cages d'escalier (2 x 2 cellules) de part et d'autre du ravin.
        var wells = new List<Vector2Int>();
        foreach (bool northSide in new[] { false, true, false, true, rnd.NextDouble() < 0.5 })
            for (int attempt = 0; attempt < 300; attempt++)
            {
                var c = new Vector2Int(rnd.Next(1, n - 2), rnd.Next(HubRows + 1, n - 4));
                bool ok = true;
                for (int i = c.x - 1; i <= c.x + 2 && ok; i++)
                    for (int j = c.y - 1; j <= c.y + 2 && ok; j++)
                    {
                        var q = new Vector2Int(i, j);
                        if (!main.InGrid(i, j) || ravineCells.Contains(q) || main.Reserved(i, j) || main.Fixed(i, j) || q == main.start
                            || nrSide.Contains(q) != northSide || wells.Exists(w => Mathf.Abs(w.x - i) <= 3 && Mathf.Abs(w.y - j) <= 3)) ok = false;
                    }
                if (!ok) continue;
                wells.Add(c);
                break;
            }
        var wellCells = new HashSet<Vector2Int>();
        foreach (var w in wells) for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++) wellCells.Add(new Vector2Int(w.x + a, w.y + b));

        var refs = new ZoneRefs { index = zone, root = new GameObject("Zone " + zone), terrain = TerrainKind.Blockhaus };
        build.Refs = refs;
        Transform root = refs.root.transform;
        root.SetParent(parent, false);
        var colliders = new GameObject("Colliders");
        colliders.transform.SetParent(root, false);
        refs.structure = colliders.transform;
        refs.sideOf = p => main.CellAt(p, out int ci, out int cj) && nrSide.Contains(new Vector2Int(ci, cj)) ? 1 : 0;
        refs.lowestFloor = -RavineReach;

        // ---------- Les 4 étages ----------
        var storeys = new Maze[StoreyLevels.Length];
        bool[,] doubleBelow = new bool[n, n];
        var style = new ZoneStyle
        {
            maxRooms = 400, minRoomSize = 1, maxRoomSize = 3, roomCoverage = 0.8f, pitChance = 0f, minPitDepth = 1.5f, maxPitDepth = 0f,
            maxLayerDepth = 0f, bridgeChance = 0f, maxHeight = StoreyHeight, floors = new[] { 0f }, ceilings = new[] { StoreyHeight },
            chaoticCeilings = new[] { StoreyHeight }, pitCeilings = new[] { StoreyHeight }, fixedRooms = true,
        };
        int doorIds = 0;
        for (int s = 0; s < StoreyLevels.Length; s++)
        {
            float y = StoreyLevels[s];
            Maze m = s == HubStorey ? main : CreateMaze(seed, zone);
            if (s != HubStorey)
            {
                // Hors de l'étage du Hub : le Hub et la Neutral Room sont murés (des blocs pleins), sans porte.
                m = new Maze(zone, n, ZoneOrigin(zone), false, new Vector2Int(HubCol, HubRows));
                var sealedHub = new RectInt(HubMin, 0, HubMax - HubMin + 1, HubRows);
                foreach (RectInt block in new[] { sealedHub, main.neutralRoom })
                {
                    for (int i = block.xMin; i < block.xMax; i++)
                        for (int j = block.yMin; j < block.yMax; j++) m.fixedCell[i, j] = true;
                    AddFixedRoom(m, block, StoreyHeight);
                }
            }
            m.terrain = TerrainKind.Blockhaus;
            m.bevelChance = 0.3f;
            m.voidWallFull = true;
            m.voidCeil = StoreyHeight;
            m.voidCell = new bool[n, n];
            m.silentVoid = new bool[n, n];
            foreach (var c in ravineCells) m.voidCell[c.x, c.y] = true;
            foreach (var c in wellCells) m.voidCell[c.x, c.y] = true;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (doubleBelow[i, j]) { m.voidCell[i, j] = true; m.silentVoid[i, j] = true; }
            // Mur sud : la zone précédente l'a construit jusqu'à sa hauteur (au-dessus du niveau du Hub seulement).
            if (previous != null && y >= 0f)
            {
                m.southHeights = new float[n];
                for (int i = 0; i < n; i++) m.southHeights[i] = Mathf.Max(0f, previous.maze.northHeights[i] - y);
            }
            Generate(m, rnd, style);
            if (s == HubStorey) StampFixedRooms(m);
            foreach (Room room in m.rooms) room.plain = true;
            // Appartements de double hauteur (rognant l'étage du dessus), sauf au dernier étage et sous la Neutral Room / le Hub.
            doubleBelow = new bool[n, n];
            if (s < StoreyLevels.Length - 1)
                foreach (Room room in m.rooms)
                {
                    if (room.cells.Count == 0 || m.Fixed(room.cells[0].x, room.cells[0].y) || rnd.NextDouble() >= 0.2) continue;
                    if (room.cells.Exists(c => main.neutralRoom.Contains(c) || c.x >= HubMin - 1 && c.x <= HubMax + 1 && c.y <= HubRows + 1)) continue;
                    room.ceil = StoreyHeight * 2f;
                    foreach (var c in room.cells) doubleBelow[c.x, c.y] = true;
                }
            // Portes : entre un couloir et un appartement, une par paire voisine ; fenêtres sur le ravin.
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (m.Void(i, j) || m.Reserved(i, j) || m.Fixed(i, j)) continue;
                    foreach (int d in new[] { 0, 2 })
                    {
                        int ni = i + DI[d], nj = j + DJ[d];
                        if (!m.InGrid(ni, nj) || m.Void(ni, nj) || m.Reserved(ni, nj) || m.Fixed(ni, nj) || HasWall(m, i, j, d)) continue;
                        if ((m.RoomAt(i, j) == null) != (m.RoomAt(ni, nj) == null)) m.doorEdges.Add((i, j, d == 2));
                    }
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = i + DI[d], nj = j + DJ[d];
                        if (!ravineCells.Contains(new Vector2Int(ni, nj)) || rnd.NextDouble() > 0.6) continue;
                        var key = d == 0 ? (i, j, false) : d == 1 ? (i - 1, j, false) : d == 2 ? (i, j, true) : (i, j - 1, true);
                        m.wallHoles[key] = (1f, 2.9f);
                    }
                }
            // Cages d'escalier : une porte vers un couloir ou un appartement voisin à chaque étage.
            foreach (var w in wells)
            {
                var sides = new List<(Vector2Int cell, int dir)>();
                foreach (var c in new[] { w, w + Vector2Int.right, w + Vector2Int.up, w + Vector2Int.one })
                    for (int d = 0; d < 4; d++)
                    {
                        var nb = c + new Vector2Int(DI[d], DJ[d]);
                        if (wellCells.Contains(nb) || !m.InGrid(nb.x, nb.y) || m.Void(nb.x, nb.y) || m.Reserved(nb.x, nb.y) || m.Fixed(nb.x, nb.y)) continue;
                        sides.Add((nb, d ^ 1));
                    }
                if (sides.Count == 0) continue;
                var (cell, dir) = sides[rnd.Next(sides.Count)];
                SetWall(m, cell.x, cell.y, dir, false);
            }
            storeys[s] = m;
        }
        yield return null;

        // ---------- Géométrie de chaque étage ----------
        for (int s = 0; s < storeys.Length; s++)
        {
            float y = StoreyLevels[s];
            var storeyRoot = new GameObject("Etage" + s).transform;
            storeyRoot.SetParent(root, false);
            storeyRoot.localPosition = Vector3.up * y;
            var storeyColliders = new GameObject("Colliders_Etage" + s);
            storeyColliders.transform.SetParent(colliders.transform, false);
            storeyColliders.transform.localPosition = Vector3.up * y;
            var geometry = BuildGeometry(storeys[s], storeyRoot, storeyColliders, new System.Random(seed * 97 + zone * 173 + s));
            while (geometry.MoveNext()) yield return null;
            BuildBevels(storeys[s], storeyRoot, new ZoneRefs(), new System.Random(seed * 53 + zone * 929 + s));
            yield return null;
        }
        refs.maze = main;
        refs.entrance = main.Center(main.start.x, main.start.y);

        // ---------- Ravin, cages d'escalier ----------
        var shell = new Kit(root, colliders.transform, "Z" + zone + "_Blockhaus");
        BuildRavine(main, ravineCells, shell, rnd, previous);
        foreach (var w in wells) BuildStairwell(main, storeys, w, shell, rnd);
        shell.Flush();
        yield return null;

        // ---------- Portes, éclairage, mobilier, emplacements des monticules ----------
        var props = new GameObject("Decor_Z" + zone).transform;
        props.SetParent(root, false);
        for (int s = 0; s < storeys.Length; s++)
        {
            Maze m = storeys[s];
            float y = StoreyLevels[s];
            foreach (var (i, j, north) in m.doorEdges)
            {
                Vector3 edge = m.Center(i, j) + (north ? Vector3.forward : Vector3.right) * (Cell * 0.5f) + Vector3.up * y;
                bool locked = rnd.NextDouble() < 0.1;
                var door = ApartmentDoor.Create(props, zone * 100000 + doorIds++, edge, Quaternion.Euler(0f, north ? 0f : 90f, 0f), DoorWidth - 0.1f, DoorHeight - 0.05f, locked);
                if (locked) refs.lockedDoors.Add(door);
            }
            DecorateStorey(m, y, props, refs, rnd, s == HubStorey);
            yield return null;
        }

        // Cloches Décoratives : à l'étage le plus haut, suspendues au plafond.
        Maze top = storeys[storeys.Length - 1];
        var topRooms = top.rooms.FindAll(r => !top.Fixed(r.cells[0].x, r.cells[0].y) && r.cells.Count >= 2);
        Shuffle(topRooms, rnd);
        foreach (Room room in topRooms)
        {
            if (refs.bellSpots.Count >= GameRules.HangingBellsInZone(zone)) break;
            Vector3 c = Vector3.zero;
            foreach (var cell in room.cells) c += top.Center(cell.x, cell.y);
            c /= room.cells.Count;
            const float chain = 1.6f;
            refs.bellSpots.Add((new Vector3(c.x, StoreyLevels[storeys.Length - 1] + room.ceil - chain - GameRules.BellHeight, c.z), chain));
            // 17.3 : l'endroit le plus haut atteignable (cadavre de la Zone 3).
            main.perches.Add((new Vector3(c.x + 2f, StoreyLevels[storeys.Length - 1], c.z), 0f, 3f));
        }

        BuildNeutralRoom(main, root, refs, new System.Random(seed * 59 + zone * 613));
        yield return null;
        Physics.SyncTransforms();
        var supers = FindSuperMoundSpots(main, refs, new System.Random(seed * 67 + zone * 389));
        while (supers.MoveNext()) yield return null;

        refs.nav = new MazeNav();
        refs.nav.AddZone(main.origin, n, Cell, (i, j) => !main.Reserved(i, j), (i, j) => BlockhausDepth + 2f, refs.lowestFloor);
        refs.nav.Prepare(colliders.transform, refs.door != null ? refs.door.Blocker : null);
        AddReflectionProbes(main, root);
        if (zone == GameRules.SecretCorpseZone) PlaceSecretCorpse(main, root);
        // Hauteur du mur nord (la zone suivante complète au-dessus) : 20 m, 80 m au droit du ravin.
        for (int i = 0; i < n; i++) main.northHeights[i] = ravineCells.Contains(new Vector2Int(i, n - 1)) ? RavineReach : BlockhausDepth;
    }

    // Fond du ravin (-80 m), plafond (+80 m), et les façades au-dessus et au-dessous des étages, percées de fenêtres factices.
    static void BuildRavine(Maze m, HashSet<Vector2Int> cells, Kit kit, System.Random rnd, ZoneRefs previous)
    {
        foreach (var c in cells)
        {
            Vector3 center = m.Center(c.x, c.y);
            var rect = Rect.MinMaxRect(center.x - Cell * 0.5f, center.z - Cell * 0.5f, center.x + Cell * 0.5f, center.z + Cell * 0.5f);
            kit.Floor(rect, -RavineReach, 1f);
            kit.walls.WorldQuad(new Vector3(rect.xMin, RavineReach, rect.yMin), new Vector3(rect.xMin, RavineReach, rect.yMax), new Vector3(rect.xMax, RavineReach, rect.yMax), new Vector3(rect.xMax, RavineReach, rect.yMin), Vector3.down, 0.5f);
            kit.Collider(new Vector3(center.x, RavineReach + 0.5f, center.z), new Vector3(Cell, 1f, Cell));
            for (int d = 0; d < 4; d++)
            {
                var nb = new Vector2Int(c.x + DI[d], c.y + DJ[d]);
                if (cells.Contains(nb)) continue;
                bool boundary = !m.InGrid(nb.x, nb.y);
                Vector3 dir = new Vector3(DI[d], 0f, DJ[d]);
                Vector3 edge = center + dir * (Cell * 0.5f);
                // Au-dessus et au-dessous des étages (sur le bord de la zone, toute la hauteur au-dessus de ce qui existe déjà).
                float from = BlockhausDepth;
                if (boundary && d == 3 && previous != null) from = Mathf.Max(from, previous.maze.northHeights[c.x]);
                foreach (var (y0, y1) in new[] { (-RavineReach, -BlockhausDepth), (from, RavineReach) })
                {
                    if (y1 - y0 < 0.05f) continue;
                    Vector3 wc = edge + dir * (Wall * 0.5f) + Vector3.up * ((y0 + y1) * 0.5f);
                    kit.Box(wc, d < 2 ? new Vector3(Wall, y1 - y0, Cell + Wall) : new Vector3(Cell + Wall, y1 - y0, Wall));
                    if (boundary) continue;
                    // Fenêtres factices de barre soviétique (noires, 10 % éclairées).
                    Vector3 side = new Vector3(DJ[d], 0f, DI[d]);
                    for (float wy = y0 + 2f; wy < y1 - 1.5f; wy += 3.4f)
                        for (int k = -1; k <= 1; k += 2)
                        {
                            Vector3 p = edge - dir * 0.02f + side * (k * 1.2f) + Vector3.up * wy;
                            MeshBuilder mb = rnd.NextDouble() < 0.1 ? kit.grey : kit.dark;
                            mb.WorldQuad(p - side * 0.7f, p + side * 0.7f, p + side * 0.7f + Vector3.up * 1.4f, p - side * 0.7f + Vector3.up * 1.4f, -dir, 1f);
                        }
                }
            }
        }
    }

    // Cage d'escalier (2 x 2 cellules) : un colimaçon de -20 m à +20 m autour d'un pilier, des paliers vers les portes de chaque étage.
    static void BuildStairwell(Maze main, Maze[] storeys, Vector2Int w, Kit kit, System.Random rnd)
    {
        Vector3 center = (main.Center(w.x, w.y) + main.Center(w.x + 1, w.y + 1)) * 0.5f;
        float half = Cell;
        kit.Floor(Rect.MinMaxRect(center.x - half, center.z - half, center.x + half, center.z + half), -BlockhausDepth);
        kit.Box(center + Vector3.up * 0f, new Vector3(1.6f, BlockhausDepth * 2f, 1.6f));
        float start = Rand(rnd, 0f, 360f);
        KitSpiral(kit, center, 1f, half - 0.6f, -BlockhausDepth, BlockhausDepth, start);
        // Paliers : à chaque étage, une dalle entre l'escalier et le mur de la cage (le joueur passe de l'un à l'autre).
        for (int s = 0; s < storeys.Length; s++)
        {
            float y = StoreyLevels[s];
            foreach (var corner in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f) })
            {
                Vector3 c = center + corner * (half - 0.9f);
                kit.Box(new Vector3(c.x, y - 0.15f, c.z), new Vector3(1.8f, 0.3f, 1.8f));
            }
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                Vector3 c = center + new Vector3(dx, 0f, dz) * (half - 0.4f);
                kit.Box(new Vector3(c.x, y - 0.15f, c.z), dx != 0 ? new Vector3(0.8f, 0.3f, half * 2f) : new Vector3(half * 2f, 0.3f, 0.8f));
            }
            AddLamp(kit.root, new Vector3(center.x, y + StoreyHeight - 0.1f, center.z), 9f, 1.2f, rnd);
        }
        kit.walls.WorldQuad(new Vector3(center.x - half, BlockhausDepth, center.z - half), new Vector3(center.x - half, BlockhausDepth, center.z + half),
            new Vector3(center.x + half, BlockhausDepth, center.z + half), new Vector3(center.x + half, BlockhausDepth, center.z - half), Vector3.down, 0.5f);
    }

    static readonly string[] BlockhausFurniture = { "Lavabo", "Douche", "Armoire", "Urinoir", "Baignoire" };

    static float BlockhausModelHeight(string model) =>
        model == "Armoire" ? 2f : model == "Baignoire" ? 1.7f : model == "Douche" ? 1f : model == "Urinoir" ? 0.8f : 0.7f;

    // Étage d'un Blockhaus : néons des couloirs, appartements éclairés trois fois sur quatre, mobilier soviétique (modèles Meshy),
    // plateformes chaotiques dans certains appartements, quelques yeux, emplacements des monticules.
    static void DecorateStorey(Maze m, float y, Transform props, ZoneRefs refs, System.Random rnd, bool hubStorey)
    {
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                if (m.Void(i, j) || m.Reserved(i, j) || m.Fixed(i, j)) continue;
                Vector3 c = m.Center(i, j) + Vector3.up * y;
                Room room = m.RoomAt(i, j);
                bool clear = hubStorey && m.keepClear.Contains(new Vector2Int(i, j));
                if (room == null && (i + j) % 2 == 0) AddLamp(props, c + Vector3.up * m.Ceil(i, j), 7f, 1.1f, rnd);
                if (!clear && rnd.NextDouble() < 0.55)
                {
                    Vector3 p = c + new Vector3(Rand(rnd, -1.6f, 1.6f), 0f, Rand(rnd, -1.6f, 1.6f));
                    if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && hit.normal.y > 0.95f && Mathf.Abs(hit.point.y - y) < 0.3f) refs.itemSpots.Add(hit.point);
                }
                if (rnd.NextDouble() < 0.015)
                {
                    int d = rnd.Next(4);
                    if (HasWall(m, i, j, d))
                    {
                        Vector3 n = -new Vector3(DI[d], 0f, DJ[d]);
                        AddEye(props, c - n * (Cell * 0.5f - Wall * 0.5f) + Vector3.up * Rand(rnd, 1.5f, 3f), n, Rand(rnd, 0.6f, 1f));
                    }
                }
            }
        foreach (Room room in m.rooms)
        {
            if (room.cells.Count == 0 || m.Fixed(room.cells[0].x, room.cells[0].y)) continue;
            Vector3 center = Vector3.zero;
            foreach (var cell in room.cells) center += m.Center(cell.x, cell.y);
            center /= room.cells.Count;
            center.y = y;
            // 25 % des appartements ne sont pas éclairés.
            if (rnd.NextDouble() >= 0.25) AddLamp(props, center + Vector3.up * room.ceil, 10f, 1.3f, rnd);
            Vector3 min = m.Center(room.rect.xMin, room.rect.yMin) - new Vector3(Cell, 0f, Cell) * 0.5f, max = m.Center(room.rect.xMax - 1, room.rect.yMax - 1) + new Vector3(Cell, 0f, Cell) * 0.5f;
            int count = 1 + rnd.Next(room.cells.Count + 1);
            for (int k = 0; k < count; k++)
            {
                string model = BlockhausFurniture[rnd.Next(BlockhausFurniture.Length)];
                int d = rnd.Next(4);
                float t = (float)rnd.NextDouble();
                Vector3 p = d == 0 ? new Vector3(max.x - 0.6f, y, Mathf.Lerp(min.z + 1.2f, max.z - 1.2f, t)) : d == 1 ? new Vector3(min.x + 0.6f, y, Mathf.Lerp(min.z + 1.2f, max.z - 1.2f, t))
                    : d == 2 ? new Vector3(Mathf.Lerp(min.x + 1.2f, max.x - 1.2f, t), y, max.z - 0.6f) : new Vector3(Mathf.Lerp(min.x + 1.2f, max.x - 1.2f, t), y, min.z + 0.6f);
                if (!m.CellAt(p, out int ci, out int cj) || m.RoomAt(ci, cj) != room) continue;
                var go = GameArt.SpawnModelFloor(model, props, BlockhausModelHeight(model));
                if (go == null) continue;
                if (model == "Douche" || model == "Lavabo" || model == "Urinoir") p.y += model == "Douche" ? 0.6f : 0.4f;
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(0f, d == 0 ? -90f : d == 1 ? 90f : d == 2 ? 180f : 0f, 0f);
                Bounds b = GameArt.LocalBounds(go.transform);
                var col = go.AddComponent<BoxCollider>();
                col.center = b.center;
                col.size = b.size;
            }
            // Plateformes chaotiques (un appartement sur cinq).
            if (rnd.NextDouble() < 0.2 && room.cells.Count >= 2)
            {
                var spots = new List<Vector3>();
                var storeyMaze = m;
                var platformRoot = new GameObject("Plateformes").transform;
                platformRoot.SetParent(props, false);
                platformRoot.localPosition = Vector3.up * y;
                AddPlatforms(platformRoot, storeyMaze, room, min.x + 1.2f, max.x - 1.2f, min.z + 1.2f, max.z - 1.2f, rnd, spots);
                foreach (var sp in spots) refs.itemSpots.Add(sp + Vector3.up * y);
            }
        }
    }
}
