using System.Collections.Generic;
using UnityEngine;

// V0.7.14 (chapitre 11) : structures posées dans une zone d'Échiquier. La zone est découpée en 4 x 4 parcelles ; la structure
// (Château ou Blockhaus) occupe les parcelles bleues d'un des 12 gabarits, le reste de la zone est un Échiquier ordinaire. Outils
// communs : cellules de la structure, parties de l'Échiquier qu'elle sépare, distance de chaque cellule à l'Échiquier, passages
// (entrées des remparts, ponts du ravin) choisis sur les bords droits de la structure.
public static partial class WorldBuilder
{
    // Passage entre l'Échiquier et la structure : la cellule 'e' de l'Échiquier, la direction 'd' vers la structure, la partie de
    // l'Échiquier ('group') à laquelle il mène.
    internal struct Crossing
    {
        public Vector2Int e;
        public int d, group;
        public Vector2Int Step => new Vector2Int(DI[d], DJ[d]);
        public Vector2Int At(int k) => e + Step * k;   // k = 1 : première cellule de la structure
    }

    internal class StructureSite
    {
        public TerrainKind kind;
        public int template;
        public int[,] groupOf;   // partie de l'Échiquier (composante) de chaque cellule hors structure ; -1 dans la structure
        public int groups;
        public int[,] dist;      // distance (cellules, 8 directions) à l'Échiquier : 1 = voisine ; 0 hors structure
        public readonly List<Crossing> crossings = new List<Crossing>();
    }

    // Marque les cellules de la structure dans la grille (jamais le Hub, ni l'entrée de la zone) et calcule les parties de
    // l'Échiquier et les distances.
    static StructureSite MarkStructure(Maze m, TerrainKind kind, int template)
    {
        int n = m.N;
        var site = new StructureSite { kind = kind, template = template };
        m.structure = new bool[n, n];
        m.structureKind = kind;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                m.structure[i, j] = Terrains.InTemplate(template, Terrains.ParcelCol(i), Terrains.ParcelRow(j)) && !m.Reserved(i, j)
                    && Mathf.Abs(i - m.start.x) + Mathf.Abs(j - m.start.y) > 2;

        // Parties de l'Échiquier séparées par la structure (le Hub compte avec l'entrée).
        site.groupOf = new int[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) site.groupOf[i, j] = -1;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (m.structure[i, j] || site.groupOf[i, j] >= 0) continue;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(new Vector2Int(i, j));
                site.groupOf[i, j] = site.groups;
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = c.x + DI[d], nj = c.y + DJ[d];
                        if (!m.InGrid(ni, nj) || m.structure[ni, nj] || site.groupOf[ni, nj] >= 0) continue;
                        site.groupOf[ni, nj] = site.groups;
                        queue.Enqueue(new Vector2Int(ni, nj));
                    }
                }
                site.groups++;
            }

        // Distance à l'Échiquier (8 directions : distance de Tchebychev en cellules).
        site.dist = new int[n, n];
        var front = new Queue<Vector2Int>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (m.structure[i, j]) site.dist[i, j] = int.MaxValue;
                else front.Enqueue(new Vector2Int(i, j));
        while (front.Count > 0)
        {
            var c = front.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int ni = c.x + dx, nj = c.y + dz;
                    if (!m.InGrid(ni, nj) || site.dist[ni, nj] <= site.dist[c.x, c.y] + 1) continue;
                    site.dist[ni, nj] = site.dist[c.x, c.y] + 1;
                    front.Enqueue(new Vector2Int(ni, nj));
                }
        }
        return site;
    }

    // Cellule ordinaire de l'Échiquier (ni structure, ni Hub, ni Neutral Room).
    static bool EchiquierCell(Maze m, Vector2Int c) => m.InGrid(c.x, c.y) && !m.InStructure(c.x, c.y) && !m.Reserved(c.x, c.y) && !m.Fixed(c.x, c.y);

    // Passages : sur un bord droit de la structure (les voisines de part et d'autre, le long du bord, sont dans la même situation),
    // une ligne droite de 'depth' cellules entre dans la structure, la k-ième à la distance k de l'Échiquier, la dernière plus loin.
    // GameRules.StructureCrossings par partie de l'Échiquier, espacés d'au moins GameRules.CrossingSpacing (moins s'il le faut).
    static void PickCrossings(Maze m, StructureSite site, int depth, System.Func<Crossing, bool> ok, System.Random rnd)
    {
        var byGroup = new List<Crossing>[site.groups];
        for (int g = 0; g < site.groups; g++) byGroup[g] = new List<Crossing>();
        for (int i = 0; i < m.N; i++)
            for (int j = 0; j < m.N; j++)
            {
                var e = new Vector2Int(i, j);
                if (!EchiquierCell(m, e) || Mathf.Abs(i - m.start.x) + Mathf.Abs(j - m.start.y) <= 2) continue;
                for (int d = 0; d < 4; d++)
                {
                    var c = new Crossing { e = e, d = d, group = site.groupOf[i, j] };
                    var side = new Vector2Int(DJ[d], DI[d]);
                    if (!EchiquierCell(m, e + side) || !EchiquierCell(m, e - side)) continue;
                    bool fits = true;
                    for (int k = 1; k <= depth && fits; k++)
                    {
                        var q = c.At(k);
                        if (!m.InStructure(q.x, q.y) || m.Fixed(q.x, q.y) || m.keepClear.Contains(q)) { fits = false; break; }
                        int want = k;
                        if (k < depth)
                        {
                            if (site.dist[q.x, q.y] != want) fits = false;
                            foreach (var s in new[] { side, -side })
                            {
                                var l = q + s;
                                if (!m.InStructure(l.x, l.y) || site.dist[l.x, l.y] != want) fits = false;
                            }
                        }
                        else if (site.dist[q.x, q.y] < depth) fits = false;
                    }
                    if (fits && (ok == null || ok(c))) byGroup[c.group].Add(c);
                }
            }
        foreach (var list in byGroup)
        {
            Shuffle(list, rnd);
            var chosen = new List<Crossing>();
            foreach (float spacing in new[] { GameRules.CrossingSpacing, GameRules.CrossingSpacing * 0.6f, Cell * 1.5f })
            {
                foreach (var c in list)
                {
                    if (chosen.Count >= GameRules.StructureCrossings) break;
                    Vector3 p = m.Center(c.e.x, c.e.y);
                    if (chosen.Exists(o => o.e == c.e || Vector3.Distance(m.Center(o.e.x, o.e.y), p) < spacing)) continue;
                    chosen.Add(c);
                }
                if (chosen.Count >= GameRules.StructureCrossings) break;
            }
            site.crossings.AddRange(chosen);
            // L'Échiquier devant chaque passage reste libre (un couloir de plain-pied).
            foreach (var c in chosen) m.keepClear.Add(c.e);
        }
    }

    // Rectangles (x, z) couvrant les cases vraies d'un masque sur une grille régulière : suites sur chaque rangée, fusionnées avec
    // la rangée suivante quand elles ont les mêmes bords.
    static List<Rect> MaskRects(bool[,] mask, float x0, float z0, float unit)
    {
        int w = mask.GetLength(0), h = mask.GetLength(1);
        var open = new Dictionary<(int, int), Rect>();
        var result = new List<Rect>();
        for (int z = 0; z < h; z++)
        {
            var next = new Dictionary<(int, int), Rect>();
            for (int x = 0; x < w;)
            {
                if (!mask[x, z]) { x++; continue; }
                int s = x;
                while (x < w && mask[x, z]) x++;
                var key = (s, x);
                Rect row = Rect.MinMaxRect(x0 + s * unit, z0 + z * unit, x0 + x * unit, z0 + (z + 1) * unit);
                if (open.TryGetValue(key, out Rect r)) { next[key] = Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, row.yMax); open.Remove(key); }
                else next[key] = row;
            }
            result.AddRange(open.Values);
            open = next;
        }
        result.AddRange(open.Values);
        return result;
    }

    // Arêtes (x, z) entre une case vraie d'un masque et une case fausse voisine (filtre 'other' sur la case voisine), fusionnées le
    // long de chaque ligne : (début, fin, direction vers la case vraie).
    static List<(Vector2 a, Vector2 b, Vector2 inward)> MaskEdges(bool[,] mask, float x0, float z0, float unit, System.Func<int, int, bool> other)
    {
        int w = mask.GetLength(0), h = mask.GetLength(1);
        var result = new List<(Vector2, Vector2, Vector2)>();
        bool Edge(int x, int z, int dx, int dz) => mask[x, z] && other(x + dx, z + dz);
        // Arêtes verticales (le long de Z), à l'est (dx = 1) ou à l'ouest (dx = -1) de chaque case.
        foreach (int dx in new[] { 1, -1 })
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h;)
                {
                    if (!Edge(x, z, dx, 0)) { z++; continue; }
                    int s = z;
                    while (z < h && Edge(x, z, dx, 0)) z++;
                    float line = x0 + (dx > 0 ? x + 1 : x) * unit;
                    result.Add((new Vector2(line, z0 + s * unit), new Vector2(line, z0 + z * unit), new Vector2(-dx, 0f)));
                }
        foreach (int dz in new[] { 1, -1 })
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w;)
                {
                    if (!Edge(x, z, 0, dz)) { x++; continue; }
                    int s = x;
                    while (x < w && Edge(x, z, 0, dz)) x++;
                    float line = z0 + (dz > 0 ? z + 1 : z) * unit;
                    result.Add((new Vector2(x0 + s * unit, line), new Vector2(x0 + x * unit, line), new Vector2(0f, -dz)));
                }
        return result;
    }

    // Plafond gris (béton) de la structure à 'height' (et sa collision : rien ne passe au-dessus), et l'enceinte qui la ferme au-dessus
    // de l'Échiquier voisin et au bord de la zone, jusqu'à ce plafond.
    static void StructureShell(Maze m, Kit kit, float height, bool above = true)
    {
        int n = m.N;
        var mask = new bool[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) mask[i, j] = m.InStructure(i, j);
        foreach (Rect r in MaskRects(mask, m.origin.x, m.origin.z, Cell))
        {
            kit.grey.WorldQuad(new Vector3(r.xMin, height, r.yMin), new Vector3(r.xMin, height, r.yMax), new Vector3(r.xMax, height, r.yMax), new Vector3(r.xMax, height, r.yMin), Vector3.down, 0.1f);
            kit.Collider(new Vector3(r.center.x, height + 0.5f, r.center.y), new Vector3(r.width, 1f, r.height));
        }
        if (!above) return;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (!m.InStructure(i, j)) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (m.InStructure(ni, nj)) continue;
                    // Au bord de la zone, une cellule vide a déjà son mur jusqu'en haut (BuildGeometry), si ses cellules vides montent
                    // jusqu'au plafond de la structure (sinon, on complète au-dessus : Château, V0.7.15).
                    if (!m.InGrid(ni, nj) && m.Void(i, j) && m.voidCeil >= height - 0.01f) continue;
                    float from = m.InGrid(ni, nj) ? m.Ceil(ni, nj) : m.Ceil(i, j);
                    if (!m.InGrid(ni, nj) && d == 3 && m.southHeights != null) from = Mathf.Max(from, m.southHeights[i]);
                    if (height - from < 0.05f) continue;
                    Vector3 c = m.Center(i, j) + new Vector3(DI[d], 0f, DJ[d]) * (Cell * 0.5f) + Vector3.up * ((from + height) * 0.5f);
                    // V0.7.20 : 2 cm plus mince que les murs qu'elle prolonge : là où elle les double, ses faces passent 1 cm derrière les leurs
                    // (elles se superposaient : briques et damier scintillaient en haut des murs et sur la tranche des toits).
                    const float thin = Wall - 0.02f;
                    kit.Box(c, d < 2 ? new Vector3(thin, height - from, Cell + Wall) : new Vector3(Cell + Wall, height - from, thin));
                }
            }
    }
}
