using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Version simplifiée du modèle de la Balise, affichée de loin (Resources/Art/BaliseLoin) : les sommets proches sont regroupés sur
// une grille, dont le pas est ajusté pour approcher le nombre de triangles voulu.
public static class BeaconFarModelSetup
{
    const string OutputPath = "Assets/Resources/Art/BaliseLoin.asset";
    const int TargetTriangles = 800;
    // Les parties éloignées de la texture ne sont jamais fusionnées : leurs coordonnées de texture moyennées n'auraient aucun sens.
    const float UvBucket = 0.125f;

    [MenuItem("FunGame/Préparer la Balise simplifiée")]
    public static void Run()
    {
        var prefab = Resources.Load<GameObject>("Art/Balise");
        Mesh source = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
        int low = 4, high = 64;
        Mesh best = null;
        while (low <= high)
        {
            int resolution = (low + high) / 2;
            Mesh candidate = Simplify(source, resolution);
            if (candidate.triangles.Length / 3 <= TargetTriangles)
            {
                if (best != null) Object.DestroyImmediate(best);
                best = candidate;
                low = resolution + 1;
            }
            else
            {
                Object.DestroyImmediate(candidate);
                high = resolution - 1;
            }
        }
        best.name = "BaliseLoin";
        AssetDatabase.DeleteAsset(OutputPath);
        AssetDatabase.CreateAsset(best, OutputPath);
        AssetDatabase.SaveAssets();
        Debug.Log("Balise simplifiée : " + source.triangles.Length / 3 + " -> " + best.triangles.Length / 3 + " triangles.");
    }

    static Mesh Simplify(Mesh source, int resolution)
    {
        Vector3[] verts = source.vertices, normals = source.normals;
        Vector2[] uv = source.uv;
        int[] tris = source.triangles;
        Bounds b = source.bounds;
        float cell = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) / resolution;

        Vector3Int CellOf(int v)
        {
            Vector3 p = (verts[v] - b.min) / cell;
            return new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
        }

        // Position commune à tous les sommets d'une case : aucune fente entre les parties de la texture.
        var cellSum = new Dictionary<Vector3Int, (Vector3 sum, int count)>();
        for (int v = 0; v < verts.Length; v++)
        {
            Vector3Int c = CellOf(v);
            cellSum.TryGetValue(c, out var s);
            cellSum[c] = (s.sum + verts[v], s.count + 1);
        }

        var groups = new Dictionary<(Vector3Int cell, int u, int v), int>();
        var position = new List<Vector3>();
        var normalSum = new List<Vector3>();
        var uvSum = new List<Vector2>();
        var counts = new List<int>();
        int GroupOf(int v)
        {
            Vector3Int c = CellOf(v);
            var key = (c, Mathf.FloorToInt(uv[v].x / UvBucket), Mathf.FloorToInt(uv[v].y / UvBucket));
            if (!groups.TryGetValue(key, out int g))
            {
                groups[key] = g = position.Count;
                var s = cellSum[c];
                position.Add(s.sum / s.count);
                normalSum.Add(Vector3.zero);
                uvSum.Add(Vector2.zero);
                counts.Add(0);
            }
            normalSum[g] += normals[v];
            uvSum[g] += uv[v];
            counts[g]++;
            return g;
        }

        var kept = new List<int>();
        var seen = new HashSet<(Vector3Int, Vector3Int, Vector3Int)>();
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            Vector3Int a = CellOf(tris[t]), c1 = CellOf(tris[t + 1]), c2 = CellOf(tris[t + 2]);
            if (a == c1 || c1 == c2 || a == c2) continue;
            // Une rotation du même triangle ne compte qu'une fois.
            var first = Smallest(a, c1) && Smallest(a, c2) ? (a, c1, c2) : Smallest(c1, a) && Smallest(c1, c2) ? (c1, c2, a) : (c2, a, c1);
            if (!seen.Add(first)) continue;
            kept.Add(GroupOf(tris[t]));
            kept.Add(GroupOf(tris[t + 1]));
            kept.Add(GroupOf(tris[t + 2]));
        }

        var finalNormals = new List<Vector3>();
        var finalUv = new List<Vector2>();
        for (int g = 0; g < position.Count; g++)
        {
            finalNormals.Add(normalSum[g].normalized);
            finalUv.Add(uvSum[g] / counts[g]);
        }
        var mesh = new Mesh();
        mesh.SetVertices(position);
        mesh.SetNormals(finalNormals);
        mesh.SetUVs(0, finalUv);
        mesh.SetTriangles(kept, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static bool Smallest(Vector3Int a, Vector3Int b)
    {
        if (a.x != b.x) return a.x < b.x;
        if (a.y != b.y) return a.y < b.y;
        return a.z < b.z;
    }
}
