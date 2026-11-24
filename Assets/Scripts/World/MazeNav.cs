using System.Collections.Generic;
using UnityEngine;

// Navigation des entités (La Dame) : grille fine de 0,5 m posée sur la géométrie réelle des zones (le Hub n'en fait pas partie).
// Chaque point mémorise la hauteur du sol du bâti (sols, dalles, escaliers, passerelles, fonds de fosse). Deux points voisins
// sont reliés si la marche à franchir est petite (escaliers et rampes : oui ; bord de fosse ou de dalle sans escalier : non)
// et si rien ne bloque entre eux (murs, colonnes, caisses, orbes). Les liens qui passent par la porte de la Zone 2
// n'existent que lorsqu'elle est ouverte. Une entité qui suit ces liens ne flotte jamais et ne tombe jamais.
// V0.7.11 : plusieurs sols par point de la grille (jusqu'à MaxLayers, du plus haut au plus bas, chacun avec assez de hauteur
// libre au-dessus de lui) : étages des Blockhaus, étage et chemin de ronde des remparts du Château, étages et sommet des Tours,
// sol sous une plateforme. Chaque lien relie un sol à celui du point voisin dont la hauteur est la plus proche.
// La grille est calculée petit à petit (BuildStep) et seulement chez l'hôte.
public class MazeNav
{
    public const float Step = 0.5f;
    const float MaxStepHeight = 0.45f; // marche la plus haute franchissable entre deux points voisins
    const float ProbeHeight = 0.6f;    // hauteur de la vérification d'obstacle entre deux points
    const int MaxLayers = 6;
    const float Headroom = 1.9f;       // hauteur libre minimale au-dessus d'un sol (jusqu'au sol suivant)
    // Note 08 des Notes Diverses (V0.7.2) : une entité ne s'approche pas à moins de 1,4 m d'un mur (elle ne doit pas être vue
    // à travers). Les points trop près d'un mur coûtent bien plus cher aux chemins (A*), et ne sont jamais tirés comme
    // destination ; on ne les retire pas : un passage plus étroit (porte, tunnel) reste franchissable, par son milieu.
    public const float WallClearance = 1.4f;
    const float NearWallCost = 8f;

    // Zones en cellules de 5 m (appartenance d'un point à une zone, cellules exclues comme le Hub).
    class Zone
    {
        public Vector3 origin;
        public int size;
        public float cell;
        public System.Func<int, int, bool> walkable;
        public System.Func<int, int, float> ceiling;
        public float lowest;
    }

    readonly List<Zone> zones = new List<Zone>();
    public bool DoorOpen;

    // Grille fine (englobe toutes les zones) : 'nodes' = colonnes x MaxLayers.
    Vector3 origin;
    int nx, nz;
    float[] height;   // NaN : pas de sol praticable
    sbyte[] zoneOf;   // par colonne ; -1 : hors zone
    byte[] links;     // bit k : relié au voisin k (8 directions)
    sbyte[] target;   // couche du voisin relié, pour chaque direction (node * 8 + k)
    byte[] doorLinks; // bit k : lien qui traverse la porte de la Zone 2
    float[] wallCost; // coût d'entrée dans le point : 1 au large, plus près d'un mur (jusqu'à 1 + NearWallCost)
    int[] region;      // région connexe (porte fermée) ; le dessus d'un bloc, par exemple, est un îlot
    int[] mainRegion;  // plus grande région de chaque zone
    Transform structure;
    Collider doorBlocker;
    int buildRow = -1;
    readonly RaycastHit[] hits = new RaycastHit[48];
    readonly List<float> surfaces = new List<float>(16);

    static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    static readonly int[] DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    public bool Ready => buildRow >= nx + 2;

    // Ajoute une zone de 'size' x 'size' cellules ; 'walkable' exclut des cellules (Hub), 'ceiling' donne la hauteur du plafond ;
    // 'lowest' : plus bas sol possible (V0.7.11).
    public void AddZone(Vector3 zoneOrigin, int size, float cell, System.Func<int, int, bool> walkable, System.Func<int, int, float> ceiling, float lowest = -14f)
    {
        zones.Add(new Zone { origin = zoneOrigin, size = size, cell = cell, walkable = walkable, ceiling = ceiling, lowest = lowest });
    }

    // Zone (0 = Zone 1, 1 = Zone 2) qui contient le point, -1 ailleurs (Hub, extérieur).
    public int ZoneAt(Vector3 p)
    {
        for (int z = 0; z < zones.Count; z++)
        {
            Zone zone = zones[z];
            int i = Mathf.FloorToInt((p.x - zone.origin.x) / zone.cell), j = Mathf.FloorToInt((p.z - zone.origin.z) / zone.cell);
            if (i >= 0 && j >= 0 && i < zone.size && j < zone.size && zone.walkable(i, j)) return z;
        }
        return -1;
    }

    Zone ZoneOfPoint(Vector3 p)
    {
        foreach (Zone zone in zones)
        {
            int i = Mathf.FloorToInt((p.x - zone.origin.x) / zone.cell), j = Mathf.FloorToInt((p.z - zone.origin.z) / zone.cell);
            if (i >= 0 && j >= 0 && i < zone.size && j < zone.size) return zone;
        }
        return null;
    }

    float CeilingAt(Vector3 p)
    {
        Zone zone = ZoneOfPoint(p);
        if (zone == null) return 4f;
        int i = Mathf.FloorToInt((p.x - zone.origin.x) / zone.cell), j = Mathf.FloorToInt((p.z - zone.origin.z) / zone.cell);
        return zone.ceiling(i, j);
    }

    // ================= Construction (hôte) =================

    public void Prepare(Transform structureRoot, Collider zoneDoorBlocker)
    {
        structure = structureRoot;
        doorBlocker = zoneDoorBlocker;
        Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue), max = new Vector3(float.MinValue, 0f, float.MinValue);
        foreach (Zone zone in zones)
        {
            min = Vector3.Min(min, zone.origin);
            max = Vector3.Max(max, zone.origin + new Vector3(zone.size * zone.cell, 0f, zone.size * zone.cell));
        }
        origin = min;
        nx = Mathf.RoundToInt((max.x - min.x) / Step);
        nz = Mathf.RoundToInt((max.z - min.z) / Step);
        int nodes = nx * nz * MaxLayers;
        height = new float[nodes];
        zoneOf = new sbyte[nx * nz];
        links = new byte[nodes];
        target = new sbyte[nodes * 8];
        doorLinks = new byte[nodes];
        wallCost = new float[nodes];
        for (int i = 0; i < nodes; i++) height[i] = float.NaN;
        buildRow = 0;
    }

    // Avance la construction de 'rows' colonnes de la grille, en décalé : hauteurs de la colonne x, liens droits
    // de la colonne x - 1, diagonales de la colonne x - 2 (elles s'appuient sur les liens droits voisins).
    // Retourne true quand la grille est prête.
    public bool BuildStep(int rows)
    {
        if (height == null) return false;
        if (Ready) return true;
        bool blocker = doorBlocker != null && doorBlocker.enabled;
        if (doorBlocker != null) doorBlocker.enabled = false; // la porte est gérée par DoorOpen
        for (int r = 0; r < rows && !Ready; r++, buildRow++)
        {
            if (buildRow < nx) SampleColumn(buildRow);
            if (buildRow >= 1 && buildRow - 1 < nx) LinkStraight(buildRow - 1);
            if (buildRow >= 2 && buildRow - 2 < nx) LinkDiagonal(buildRow - 2);
        }
        if (doorBlocker != null) doorBlocker.enabled = blocker;
        if (Ready) ComputeRegions();
        return Ready;
    }

    public void BuildAll()
    {
        while (!BuildStep(64)) { }
    }

    int Cell(int x, int z) => x * nz + z;

    void SampleColumn(int x)
    {
        for (int z = 0; z < nz; z++)
        {
            int c = Cell(x, z);
            Vector3 p = PointXZ(x, z);
            int zone = ZoneAt(p);
            zoneOf[c] = (sbyte)zone;
            if (zone < 0) continue;
            Zone info = zones[zone];
            // Sols du bâti sous le plafond de la cellule (le décor ne compte pas), du plus haut au plus bas.
            float top = CeilingAt(p) - 0.05f;
            int count = Physics.RaycastNonAlloc(new Vector3(p.x, top, p.z), Vector3.down, hits, top - info.lowest + 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            surfaces.Clear();
            for (int k = 0; k < count; k++)
                if (Floor(hits[k]) && hits[k].normal.y > 0.5f) surfaces.Add(hits[k].point.y);
            surfaces.Sort((a, b) => b.CompareTo(a));
            int layer = 0;
            float above = top + Headroom;
            for (int k = 0; k < surfaces.Count && layer < MaxLayers; k++)
            {
                float y = surfaces[k];
                // Un sol sans hauteur libre au-dessus de lui (dessous d'un escalier, sol pris sous une dalle) ne compte pas.
                if (above - y < Headroom) { above = Mathf.Min(above, y); continue; }
                int n = c * MaxLayers + layer;
                height[n] = y;
                wallCost[n] = 1f + NearWallCost * (1f - WallDistance(new Vector3(p.x, y, p.z)) / WallClearance);
                above = y;
                layer++;
            }
        }
    }

    // Distance au mur le plus proche (paroi presque verticale du bâti), dans 8 directions, plafonnée à WallClearance.
    float WallDistance(Vector3 ground)
    {
        Vector3 from = ground + Vector3.up * ProbeHeight;
        float nearest = WallClearance;
        for (int d = 0; d < 8; d++)
        {
            var dir = new Vector3(DX[d], 0f, DZ[d]).normalized;
            int count = Physics.RaycastNonAlloc(from, dir, hits, nearest, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++)
                if (hits[k].transform.IsChildOf(structure) && Mathf.Abs(hits[k].normal.y) < 0.3f && hits[k].distance < nearest)
                    nearest = hits[k].distance;
        }
        return nearest;
    }

    // Point à l'écart des murs (au moins WallClearance).
    bool Clear(int n) => wallCost == null || wallCost[n] <= 1.001f;

    // Liens de la colonne x vers la colonne x + 1 et vers le point suivant de la colonne (directions 0 et 2) ;
    // les autres directions sont symétriques.
    void LinkStraight(int x)
    {
        for (int z = 0; z < nz; z++)
            for (int l = 0; l < MaxLayers; l++)
            {
                int a = Cell(x, z) * MaxLayers + l;
                if (float.IsNaN(height[a])) break;
                foreach (int dir in new[] { 0, 2 })
                {
                    int bx = x + DX[dir], bz = z + DZ[dir];
                    if (bx >= nx || bz >= nz) continue;
                    int b = Closest(Cell(bx, bz), height[a]);
                    if (b >= 0) TryLink(a, b, dir);
                }
            }
    }

    // Sol de la colonne 'cell' le plus proche de la hauteur 'y' (à une marche près), -1 sinon.
    int Closest(int cell, float y)
    {
        int best = -1;
        float bestDiff = MaxStepHeight + 0.001f;
        for (int l = 0; l < MaxLayers; l++)
        {
            int n = cell * MaxLayers + l;
            if (float.IsNaN(height[n])) break;
            float d = Mathf.Abs(height[n] - y);
            if (d < bestDiff) { bestDiff = d; best = n; }
        }
        return best;
    }

    // Diagonales (directions 1 et 7) : seulement si les deux chemins droits existent (pas de coin coupé).
    void LinkDiagonal(int x)
    {
        for (int z = 0; z < nz; z++)
            for (int l = 0; l < MaxLayers; l++)
            {
                int a = Cell(x, z) * MaxLayers + l;
                if (float.IsNaN(height[a])) break;
                if (x + 1 >= nx) continue;
                if (z + 1 < nz) TryDiagonal(a, 1);
                if (z - 1 >= 0) TryDiagonal(a, 7);
            }
    }

    void TryLink(int a, int b, int dir)
    {
        if (float.IsNaN(height[b]) || Mathf.Abs(height[a] - height[b]) > MaxStepHeight) return;
        Vector3 pa = Position(a), pb = Position(b);
        float y = Mathf.Max(pa.y, pb.y) + ProbeHeight;
        if (Physics.Linecast(new Vector3(pa.x, y, pa.z), new Vector3(pb.x, y, pb.z), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
        if (Physics.Linecast(new Vector3(pb.x, y, pb.z), new Vector3(pa.x, y, pa.z), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
        if (!GroundBetween(pa, pb)) return;
        SetLink(a, b, dir);
    }

    // Le sol, à mi-chemin, est à la hauteur moyenne des deux points : pas de rebord ni de vide entre eux.
    bool GroundBetween(Vector3 pa, Vector3 pb)
    {
        Vector3 mid = (pa + pb) * 0.5f;
        float start = Mathf.Max(pa.y, pb.y) + 0.5f;
        int count = Physics.RaycastNonAlloc(new Vector3(mid.x, start, mid.z), Vector3.down, hits, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int k = 0; k < count; k++)
            if (Floor(hits[k]) && hits[k].point.y > best) best = hits[k].point.y;
        return !float.IsNegativeInfinity(best) && Mathf.Abs(best - mid.y) < 0.2f;
    }

    // Sol praticable : le bâti, et (V0.7.14, Note 08) le tablier des ponts suspendus.
    bool Floor(RaycastHit hit) => hit.transform.IsChildOf(structure) || SuspendedBridge.IsDeck(hit.collider);

    // V0.7.14 : retire de la navigation les points d'un sol disparu (pont cassé) : plus aucun lien vers eux.
    public void Cut(System.Func<Vector3, bool> inside)
    {
        if (height == null) return;
        for (int n = 0; n < height.Length; n++)
        {
            if (float.IsNaN(height[n]) || links[n] == 0 || !inside(Position(n))) continue;
            for (int dir = 0; dir < 8; dir++)
            {
                if (!Linked(n, dir)) continue;
                int m = Neighbor(n, dir), back = (dir + 4) % 8;
                links[m] &= (byte)~(1 << back);
                doorLinks[m] &= (byte)~(1 << back);
            }
            links[n] = 0;
            doorLinks[n] = 0;
        }
    }

    void TryDiagonal(int a, int dir)
    {
        int dirZ = DZ[dir] > 0 ? 2 : 6;
        int viaX = Neighbor(a, 0), viaZ = Neighbor(a, dirZ);
        if (viaX < 0 || viaZ < 0) return;
        int b1 = Neighbor(viaX, dirZ), b2 = Neighbor(viaZ, 0);
        if (b1 < 0 || b1 != b2) return;
        if (GroundBetween(Position(a), Position(b1))) SetLink(a, b1, dir);
    }

    bool Linked(int n, int dir) => (links[n] & (1 << dir)) != 0;

    // Point relié à 'n' dans la direction 'dir' (-1 : aucun).
    int Neighbor(int n, int dir)
    {
        if (!Linked(n, dir)) return -1;
        int c = n / MaxLayers, x = c / nz, z = c % nz;
        return Cell(x + DX[dir], z + DZ[dir]) * MaxLayers + target[n * 8 + dir];
    }

    void SetLink(int a, int b, int dir)
    {
        int back = (dir + 4) % 8;
        links[a] |= (byte)(1 << dir);
        links[b] |= (byte)(1 << back);
        target[a * 8 + dir] = (sbyte)(b % MaxLayers);
        target[b * 8 + back] = (sbyte)(a % MaxLayers);
        if (zoneOf[a / MaxLayers] != zoneOf[b / MaxLayers])
        {
            doorLinks[a] |= (byte)(1 << dir);
            doorLinks[b] |= (byte)(1 << back);
        }
    }

    // Régions connexes, porte fermée : les points tirés au hasard (apparition, errance) restent dans la grande région
    // de leur zone, jamais sur un îlot inatteignable.
    void ComputeRegions()
    {
        region = new int[height.Length];
        for (int i = 0; i < region.Length; i++) region[i] = -1;
        var sizes = new List<int>();
        var zoneOfRegion = new List<int>();
        var queue = new Queue<int>();
        for (int start = 0; start < region.Length; start++)
        {
            if (region[start] >= 0 || !Walkable(start)) continue;
            int id = sizes.Count, size = 0;
            region[start] = id;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                size++;
                for (int dir = 0; dir < 8; dir++)
                {
                    if ((doorLinks[n] & (1 << dir)) != 0) continue;
                    int m = Neighbor(n, dir);
                    if (m < 0 || region[m] >= 0) continue;
                    region[m] = id;
                    queue.Enqueue(m);
                }
            }
            sizes.Add(size);
            zoneOfRegion.Add(zoneOf[start / MaxLayers]);
        }
        mainRegion = new int[zones.Count];
        for (int z = 0; z < zones.Count; z++)
        {
            mainRegion[z] = -1;
            for (int r = 0; r < sizes.Count; r++)
                if (zoneOfRegion[r] == z && (mainRegion[z] < 0 || sizes[r] > sizes[mainRegion[z]])) mainRegion[z] = r;
        }
    }

    // ================= Requêtes =================

    Vector3 PointXZ(int x, int z) => origin + new Vector3((x + 0.5f) * Step, 0f, (z + 0.5f) * Step);

    public Vector3 Position(int node)
    {
        int c = node / MaxLayers;
        Vector3 p = PointXZ(c / nz, c % nz);
        p.y = height[node];
        return p;
    }

    public int ZoneOfNode(int node) => zoneOf[node / MaxLayers];

    bool Walkable(int n) => n >= 0 && n < height.Length && !float.IsNaN(height[n]) && links[n] != 0;

    // Point praticable le plus proche de 'p' (même niveau de préférence), dans un rayon de 2 m ; -1 sinon.
    public int NodeAt(Vector3 p)
    {
        if (height == null) return -1;
        int cx = Mathf.FloorToInt((p.x - origin.x) / Step), cz = Mathf.FloorToInt((p.z - origin.z) / Step);
        int best = -1, any = -1;
        float bestScore = float.MaxValue, anyScore = float.MaxValue;
        for (int r = 0; r <= 4 && best < 0; r++)
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                    if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Abs(z - cz)) != r) continue;
                    for (int l = 0; l < MaxLayers; l++)
                    {
                        int n = Cell(x, z) * MaxLayers + l;
                        if (float.IsNaN(height[n])) break;
                        if (!Walkable(n)) continue;
                        Vector3 q = Position(n);
                        float score = new Vector2(q.x - p.x, q.z - p.z).sqrMagnitude + Mathf.Abs(q.y - p.y) * 4f;
                        if (score < anyScore) { anyScore = score; any = n; }
                        // Un sol bien au-dessus ou au-dessous (autre étage) n'est pas celui du point, sauf s'il n'y en a pas d'autre.
                        if (Mathf.Abs(q.y - p.y) > 2.5f) continue;
                        if (score < bestScore) { bestScore = score; best = n; }
                    }
                }
        return best >= 0 ? best : any;
    }

    // Point praticable au hasard de la zone, à au moins 'minDistance' mètres (et au plus 'maxDistance') de 'from'.
    // V0.7.11 : 'filter' (facultatif) restreint les points possibles (un côté du rempart ou du ravin).
    public int RandomNode(int zone, System.Random rnd, Vector3 from, float minDistance, float maxDistance = float.MaxValue, System.Func<Vector3, bool> filter = null)
    {
        if (height == null) return -1;
        int fallback = -1;
        for (int attempt = 0; attempt < (filter != null ? 1200 : 400); attempt++)
        {
            int n = rnd.Next(height.Length);
            if (!Walkable(n) || zoneOf[n / MaxLayers] != zone || (mainRegion != null && region[n] != mainRegion[zone])) continue;
            Vector3 q = Position(n);
            if (filter != null && !filter(q)) continue;
            Vector3 d = q - from;
            d.y = 0f;
            if (fallback < 0 || Clear(n)) fallback = n;
            if (!Clear(n)) continue;
            if (d.magnitude >= minDistance && d.magnitude <= maxDistance) return n;
        }
        return fallback;
    }

    // ================= Chemins (A*) =================

    float[] gScore;
    int[] cameFrom, visited;
    int stamp;
    readonly List<(float f, int n)> open = new List<(float, int)>();

    // Chemin de 'from' vers 'to' (liste de points, 'from' inclus). Si 'to' est inatteignable, chemin vers le point
    // exploré le plus proche de 'to' ('maxExpansions' limite la recherche).
    public List<int> Path(int from, int to, int maxExpansions = 20000)
    {
        var path = new List<int>();
        if (height == null || from < 0 || to < 0) return path;
        int count = height.Length;
        if (gScore == null) { gScore = new float[count]; cameFrom = new int[count]; visited = new int[count]; }
        stamp++;
        open.Clear();
        Vector3 goal = Position(to);
        gScore[from] = 0f;
        cameFrom[from] = from;
        visited[from] = stamp;
        Push(Heuristic(from, goal), from);
        int closest = from;
        float closestH = Heuristic(from, goal);
        int expansions = 0;
        while (open.Count > 0 && expansions++ < maxExpansions)
        {
            int n = Pop();
            if (n == to) { closest = n; break; }
            float h = Heuristic(n, goal);
            if (h < closestH) { closestH = h; closest = n; }
            for (int dir = 0; dir < 8; dir++)
            {
                if (!Linked(n, dir)) continue;
                if (!DoorOpen && (doorLinks[n] & (1 << dir)) != 0) continue;
                int m = Neighbor(n, dir);
                float g = gScore[n] + ((dir & 1) == 1 ? 1.4142f : 1f) * Step * wallCost[m];
                if (visited[m] == stamp && g >= gScore[m]) continue;
                visited[m] = stamp;
                gScore[m] = g;
                cameFrom[m] = n;
                Push(g + Heuristic(m, goal), m);
            }
        }
        for (int n = closest; ; n = cameFrom[n])
        {
            path.Add(n);
            if (n == from) break;
        }
        path.Reverse();
        return path;
    }

    // Distance à vol d'oiseau (la différence de hauteur compte : les étages ne se confondent pas).
    float Heuristic(int n, Vector3 goal)
    {
        Vector3 p = Position(n);
        return new Vector3(p.x - goal.x, (p.y - goal.y) * 0.5f, p.z - goal.z).magnitude;
    }

    // Tas binaire minimal sur f.
    void Push(float f, int n)
    {
        open.Add((f, n));
        int i = open.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (open[parent].f <= open[i].f) break;
            (open[parent], open[i]) = (open[i], open[parent]);
            i = parent;
        }
    }

    int Pop()
    {
        int result = open[0].n;
        open[0] = open[open.Count - 1];
        open.RemoveAt(open.Count - 1);
        int i = 0;
        while (true)
        {
            int l = i * 2 + 1, r = l + 1, smallest = i;
            if (l < open.Count && open[l].f < open[smallest].f) smallest = l;
            if (r < open.Count && open[r].f < open[smallest].f) smallest = r;
            if (smallest == i) break;
            (open[smallest], open[i]) = (open[i], open[smallest]);
            i = smallest;
        }
        return result;
    }
}
