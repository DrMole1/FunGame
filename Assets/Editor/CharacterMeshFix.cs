using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Le maillage Meshy de Princess (Perso3) a une douzaine de triangles « ailettes » : rattachés au corps par un seul côté
// (déjà partagé par deux autres triangles), leurs deux autres côtés sont libres. Ils dépassent de la surface et
// apparaissent comme de petits triangles noirs. À l'import, on les retire : le maillage redevient fermé, sans trou.
// Les sommets sont comparés par position, car l'import les dédouble le long des coutures UV.
public class CharacterMeshFix : AssetPostprocessor
{
    static readonly HashSet<string> Models = new HashSet<string>
    {
        "Assets/Models/Characters/Perso3/Perso3_Rigged.fbx",
        "Assets/Models/Characters/Perso3/Perso3_Walk.fbx",
        "Assets/Models/Characters/Perso3/Perso3_Run.fbx",
        "Assets/Models/Characters/Perso3/Perso3_Jump.fbx",
    };

    public override uint GetVersion() => 1;

    void OnPostprocessModel(GameObject root)
    {
        if (!Models.Contains(assetPath)) return;
        int removed = 0;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (smr.sharedMesh != null) removed += RemoveFins(smr.sharedMesh);
        if (removed > 0) Debug.Log("CharacterMeshFix : " + removed + " triangle(s) isolé(s) retiré(s) de " + assetPath);
    }

    public static int RemoveFins(Mesh mesh)
    {
        Vector3[] verts = mesh.vertices;
        var weld = new int[verts.Length];
        var byPos = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < verts.Length; i++)
        {
            var key = Vector3Int.RoundToInt(verts[i] * 100000f);
            if (!byPos.TryGetValue(key, out int w)) byPos[key] = w = i;
            weld[i] = w;
        }
        long Edge(int a, int b) { a = weld[a]; b = weld[b]; return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; }

        var subs = new int[mesh.subMeshCount][];
        var count = new Dictionary<long, int>();
        for (int s = 0; s < subs.Length; s++)
        {
            subs[s] = mesh.GetTriangles(s);
            int[] t = subs[s];
            for (int k = 0; k < t.Length; k += 3)
                for (int e = 0; e < 3; e++)
                {
                    long key = Edge(t[k + e], t[k + (e + 1) % 3]);
                    count.TryGetValue(key, out int n);
                    count[key] = n + 1;
                }
        }

        int removed = 0;
        for (int s = 0; s < subs.Length; s++)
        {
            int[] t = subs[s];
            var kept = new List<int>(t.Length);
            for (int k = 0; k < t.Length; k += 3)
            {
                int free = 0;
                for (int e = 0; e < 3; e++) if (count[Edge(t[k + e], t[k + (e + 1) % 3])] == 1) free++;
                if (free >= 2) { removed++; continue; }
                kept.Add(t[k]); kept.Add(t[k + 1]); kept.Add(t[k + 2]);
            }
            if (kept.Count != t.Length) mesh.SetTriangles(kept, s);
        }
        return removed;
    }
}
