using System.Collections.Generic;
using UnityEngine;

// Navigation des entités (La Dame) : grille fine de 0,5 m posée sur la géométrie réelle des zones (le Hub n'en fait pas partie).
// Chaque point mémorise la hauteur du sol du bâti (sols, dalles, escaliers, passerelles, fonds de fosse). Deux points voisins
// sont reliés si la marche à franchir est petite (escaliers et rampes : oui ; bord de fosse ou de dalle sans escalier : non)
// et si rien ne bloque entre eux (murs, colonnes, caisses, orbes). Les liens qui passent par la porte de la Zone 2
// n'existent que lorsqu'elle est ouverte. Une entité qui suit ces liens ne flotte jamais et ne tombe jamais.
// La grille est calculée petit à petit (BuildStep) et seulement chez l'hôte.
public class MazeNav
{
    public const float Step = 0.5f;
    const float MaxStepHeight = 0.45f; // marche la plus haute franchissable entre deux points voisins
    const float ProbeHeight = 0.6f;    // hauteur de la vérification d'obstacle entre deux points

    // Zones en cellules de 5 m (appartenance d'un point à une zone, cellules exclues comme le Hub).
    class Zone
    {
        public Vector3 origin;
        public int size;
        public float cell;
        public System.Func<int, int, bool> walkable;
        public System.Func<int, int, float> ceiling;
    }

    readonly List<Zone> zones = new List<Zone>();
    public bool DoorOpen;

    // Grille fine (englobe toutes les zones).
    Vector3 origin;
    int nx, nz;
    float[] height;   // NaN : pas de sol praticable
    sbyte[] zoneOf;   // -1 : hors zone
    byte[] links;     // bit k : relié au voisin k (8 directions)
    byte[] doorLinks; // bit k : lien qui traverse la porte de la Zone 2
    int[] region;      // région connexe (porte fermée) ; les fonds de fosse, par exemple, sont des îlots
    int[] mainRegion;  // plus grande région de chaque zone
    Transform structure;
    Collider doorBlocker;
    int buildRow = -1;
    readonly RaycastHit[] hits = new RaycastHit[16];

    static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    static readonly int[] DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    public bool Ready => buildRow >= nx + 2;

    // Ajoute une zone de 'size' x 'size' cellules ; 'walkable' exclut des cellules (Hub), 'ceiling' donne la hauteur du plafond.
    public void AddZone(Vector3 zoneOrigin, int size, float cell, System.Func<int, int, bool> walkable, System.Func<int, int, float> ceiling)
    {
        zones.Add(new Zone { origin = zoneOrigin, size = size, cell = cell, walkable = walkable, ceiling = ceiling });
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

    float CeilingAt(Vector3 p)
    {
        foreach (Zone zone in zones)
        {
            int i = Mathf.FloorToInt((p.x - zone.origin.x) / zone.cell), j = Mathf.FloorToInt((p.z - zone.origin.z) / zone.cell);
            if (i >= 0 && j >= 0 && i < zone.size && j < zone.size) return zone.ceiling(i, j);
        }
        return 4f;
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
        height = new float[nx * nz];
        zoneOf = new sbyte[nx * nz];
        links = new byte[nx * nz];
        doorLinks = new byte[nx * nz];
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

    void SampleColumn(int x)
    {
        for (int z = 0; z < nz; z++)
        {
            int n = x * nz + z;
            Vector3 p = PointXZ(x, z);
            int zone = ZoneAt(p);
            zoneOf[n] = (sbyte)zone;
            height[n] = float.NaN;
            if (zone < 0) continue;
            // Sol du bâti le plus haut sous le plafond de la cellule (le décor ne compte pas).
            float top = CeilingAt(p) - 0.05f;
            int count = Physics.RaycastNonAlloc(new Vector3(p.x, top, p.z), Vector3.down, hits, top + 14f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int k = 0; k < count; k++)
                if (hits[k].transform.IsChildOf(structure) && hits[k].point.y > best && hits[k].normal.y > 0.5f) best = hits[k].point.y;
            if (!float.IsNegativeInfinity(best)) height[n] = best;
        }
    }

    // Liens de la colonne x vers la colonne x + 1 et vers le point suivant de la colonne (directions 0 et 2) ;
    // les autres directions sont symétriques.
    void LinkStraight(int x)
    {
        for (int z = 0; z < nz; z++)
        {
            int a = x * nz + z;
            if (float.IsNaN(height[a])) continue;
            foreach (int dir in new[] { 0, 2 })
            {
                int bx = x + DX[dir], bz = z + DZ[dir];
                if (bx >= nx || bz >= nz) continue;
                TryLink(a, bx * nz + bz, dir);
            }
        }
    }

    // Diagonales (directions 1 et 7) : seulement si les deux chemins droits existent (pas de coin coupé).
    void LinkDiagonal(int x)
    {
        for (int z = 0; z < nz; z++)
        {
            int a = x * nz + z;
            if (float.IsNaN(height[a]) || x + 1 >= nx) continue;
            if (z + 1 < nz) TryDiagonal(a, x, z, 1);
            if (z - 1 >= 0) TryDiagonal(a, x, z, 7);
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
            if (hits[k].transform.IsChildOf(structure) && hits[k].point.y > best) best = hits[k].point.y;
        return !float.IsNegativeInfinity(best) && Mathf.Abs(best - mid.y) < 0.2f;
    }

    void TryDiagonal(int a, int x, int z, int dir)
    {
        int b = (x + DX[dir]) * nz + (z + DZ[dir]);
        int viaX = (x + 1) * nz + z, viaZ = x * nz + (z + DZ[dir]);
        int dirZ = DZ[dir] > 0 ? 2 : 6, dirX = 0;
        bool ok = Linked(a, dirX) && Linked(viaX, dirZ) && Linked(a, dirZ) && Linked(viaZ, dirX);
        if (ok && b >= 0 && !float.IsNaN(height[b]) && GroundBetween(Position(a), Position(b))) SetLink(a, b, dir);
    }

    bool Linked(int n, int dir) => (links[n] & (1 << dir)) != 0;

    void SetLink(int a, int b, int dir)
    {
        int back = (dir + 4) % 8;
        links[a] |= (byte)(1 << dir);
        links[b] |= (byte)(1 << back);
        if (zoneOf[a] != zoneOf[b])
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
                int x = n / nz, z = n % nz;
                for (int dir = 0; dir < 8; dir++)
                {
                    if ((links[n] & (1 << dir)) == 0 || (doorLinks[n] & (1 << dir)) != 0) continue;
                    int m = (x + DX[dir]) * nz + (z + DZ[dir]);
                    if (region[m] >= 0) continue;
                    region[m] = id;
                    queue.Enqueue(m);
                }
            }
            sizes.Add(size);
            zoneOfRegion.Add(zoneOf[start]);
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
        Vector3 p = PointXZ(node / nz, node % nz);
        p.y = height[node];
        return p;
    }

    public int ZoneOfNode(int node) => zoneOf[node];

    bool Walkable(int n) => n >= 0 && n < height.Length && !float.IsNaN(height[n]) && links[n] != 0;

    // Point praticable le plus proche de 'p' (même niveau de préférence), dans un rayon de 2 m ; -1 sinon.
    public int NodeAt(Vector3 p)
    {
        if (height == null) return -1;
        int cx = Mathf.FloorToInt((p.x - origin.x) / Step), cz = Mathf.FloorToInt((p.z - origin.z) / Step);
        int best = -1;
        float bestScore = float.MaxValue;
        for (int r = 0; r <= 4 && best < 0; r++)
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                    if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Abs(z - cz)) != r) continue;
                    int n = x * nz + z;
                    if (!Walkable(n)) continue;
                    Vector3 q = Position(n);
                    float score = new Vector2(q.x - p.x, q.z - p.z).sqrMagnitude + Mathf.Abs(q.y - p.y) * 4f;
                    if (score < bestScore) { bestScore = score; best = n; }
                }
        return best;
    }

    // Point praticable au hasard de la zone, à au moins 'minDistance' mètres (et au plus 'maxDistance') de 'from'.
    public int RandomNode(int zone, System.Random rnd, Vector3 from, float minDistance, float maxDistance = float.MaxValue)
    {
        if (height == null) return -1;
        int fallback = -1;
        for (int attempt = 0; attempt < 400; attempt++)
        {
            int n = rnd.Next(height.Length);
            if (!Walkable(n) || zoneOf[n] != zone || (mainRegion != null && region[n] != mainRegion[zone])) continue;
            Vector3 d = Position(n) - from;
            d.y = 0f;
            fallback = n;
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
            int x = n / nz, z = n % nz;
            for (int dir = 0; dir < 8; dir++)
            {
                if ((links[n] & (1 << dir)) == 0) continue;
                if (!DoorOpen && (doorLinks[n] & (1 << dir)) != 0) continue;
                int m = (x + DX[dir]) * nz + (z + DZ[dir]);
                float g = gScore[n] + ((dir & 1) == 1 ? 1.4142f : 1f) * Step;
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

    float Heuristic(int n, Vector3 goal)
    {
        Vector3 p = PointXZ(n / nz, n % nz);
        return new Vector2(p.x - goal.x, p.z - goal.z).magnitude;
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
