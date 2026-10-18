using System.Collections.Generic;
using UnityEngine;

// Graphe de déplacement des entités : un nœud par cellule de 5 m des zones du labyrinthe (le Hub n'en fait pas partie),
// un lien entre deux cellules voisines sans mur. La porte de la Zone 2 est un lien qui n'existe que lorsqu'elle est ouverte.
public class MazeNav
{
    class Zone
    {
        public Vector3 origin;
        public int size, offset;
    }

    readonly List<Zone> zones = new List<Zone>();
    readonly List<Vector3> positions = new List<Vector3>();
    readonly List<int> zoneOf = new List<int>();
    readonly List<List<int>> links = new List<List<int>>();
    int doorA = -1, doorB = -1;

    public bool DoorOpen;
    public int NodeCount => positions.Count;

    // Ajoute une zone de 'size' x 'size' cellules ; 'walkable' exclut des cellules (Hub).
    public void AddZone(Vector3 origin, int size, float cell, System.Func<int, int, bool> walkable)
    {
        var z = new Zone { origin = origin, size = size, offset = positions.Count };
        zones.Add(z);
        for (int i = 0; i < size; i++)
            for (int j = 0; j < size; j++)
            {
                positions.Add(walkable(i, j) ? origin + new Vector3((i + 0.5f) * cell, 0f, (j + 0.5f) * cell) : new Vector3(float.NaN, 0f, 0f));
                zoneOf.Add(zones.Count - 1);
                links.Add(new List<int>());
            }
    }

    public int Node(int zone, int i, int j)
    {
        Zone z = zones[zone];
        if (i < 0 || j < 0 || i >= z.size || j >= z.size) return -1;
        int n = z.offset + i * z.size + j;
        return float.IsNaN(positions[n].x) ? -1 : n;
    }

    public void Link(int a, int b)
    {
        if (a < 0 || b < 0) return;
        links[a].Add(b);
        links[b].Add(a);
    }

    public void SetDoor(int a, int b)
    {
        doorA = a;
        doorB = b;
    }

    public Vector3 Position(int node) => positions[node];
    public int ZoneOfNode(int node) => zoneOf[node];

    public int NodeAt(Vector3 p)
    {
        for (int z = 0; z < zones.Count; z++)
        {
            Zone zone = zones[z];
            int i = Mathf.FloorToInt((p.x - zone.origin.x) / CellSize), j = Mathf.FloorToInt((p.z - zone.origin.z) / CellSize);
            int n = Node(z, i, j);
            if (n >= 0) return n;
        }
        return -1;
    }

    // Zone (0 = Zone 1, 1 = Zone 2) qui contient le point, -1 ailleurs (Hub, extérieur).
    public int ZoneAt(Vector3 p)
    {
        int n = NodeAt(p);
        return n >= 0 ? zoneOf[n] : -1;
    }

    public float CellSize = 5f;

    IEnumerable<int> Neighbors(int node)
    {
        foreach (int n in links[node]) yield return n;
        if (!DoorOpen) yield break;
        if (node == doorA) yield return doorB;
        else if (node == doorB) yield return doorA;
    }

    // Plus court chemin (en nombre de cellules) ; liste vide si inatteignable. Le premier nœud est 'from'.
    readonly Dictionary<int, int> cameFrom = new Dictionary<int, int>();
    readonly Queue<int> queue = new Queue<int>();

    public List<int> Path(int from, int to)
    {
        var path = new List<int>();
        if (from < 0 || to < 0) return path;
        cameFrom.Clear();
        queue.Clear();
        cameFrom[from] = from;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int n = queue.Dequeue();
            if (n == to) break;
            foreach (int next in Neighbors(n))
            {
                if (cameFrom.ContainsKey(next)) continue;
                cameFrom[next] = n;
                queue.Enqueue(next);
            }
        }
        if (!cameFrom.ContainsKey(to)) return path;
        for (int n = to; ; n = cameFrom[n])
        {
            path.Add(n);
            if (n == from) break;
        }
        path.Reverse();
        return path;
    }

    // Cellule au hasard de la zone, à au moins 'minDistance' mètres de 'awayFrom'.
    public int RandomNode(int zone, System.Random rnd, Vector3 awayFrom, float minDistance)
    {
        Zone z = zones[zone];
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int n = Node(zone, rnd.Next(z.size), rnd.Next(z.size));
            if (n < 0) continue;
            Vector3 d = positions[n] - awayFrom;
            d.y = 0f;
            if (d.magnitude >= minDistance || attempt > 150) return n;
        }
        return z.offset;
    }
}
