using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Enseignes "Neutral Room" et "Acquisition Room" (V0.6) : mots en 3D générés par Meshy (police soviétique), nettoyés ici.
// Meshy a ajouté un socle sous chaque mot (retiré, le bas des lettres aplani) et écrit "NEEUTRAL" (le E en trop est retiré). Les mots sont mis
// bout à bout, à la hauteur de capitale 1, lisibles depuis -Z (sens de lecture +X), base en y = 0, centrés en x.
// Résultat : Resources/Art/Decor/EnseigneNeutralRoom.asset et EnseigneAcquisitionRoom.asset. Les FBX bruts sont rangés
// dans Assets/Models/Meshy (hors Resources : ils ne partent pas dans le build).
public static class NeonSignsSetup
{
    const string Raw = "Assets/Resources/Art/Decor/", Stored = "Assets/Models/Meshy/";

    // Mot brut : coupe du socle (y), plage retirée (x, lettre en trop) ; coordonnées du modèle importé (x100).
    struct Word
    {
        public string file;
        public float baseCut, removeFrom, removeTo;
    }

    static readonly Word Neutral = new Word { file = "TexteNeutral", baseCut = -0.118f, removeFrom = 0.492f, removeTo = 0.706f };
    static readonly Word Room = new Word { file = "TexteRoom", baseCut = -0.275f, removeFrom = 1f, removeTo = 1f };
    static readonly Word Acquisition = new Word { file = "TexteAcquisition", baseCut = -0.004f, removeFrom = 1f, removeTo = 1f };

    [MenuItem("FunGame/Préparer les enseignes néon")]
    public static void Build()
    {
        Save(Combine(Neutral, Room), "EnseigneNeutralRoom");
        Save(Combine(Acquisition, Room), "EnseigneAcquisitionRoom");
        if (!AssetDatabase.IsValidFolder("Assets/Models/Meshy")) AssetDatabase.CreateFolder("Assets/Models", "Meshy");
        foreach (var w in new[] { Neutral, Room, Acquisition })
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Raw + w.file + ".fbx") != null)
                AssetDatabase.MoveAsset(Raw + w.file + ".fbx", Stored + w.file + ".fbx");
        AssetDatabase.SaveAssets();
        Debug.Log("Enseignes néon prêtes.");
    }

    static void Save(Mesh mesh, string name)
    {
        string path = Raw + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
    }

    static GameObject Load(Word w) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Raw + w.file + ".fbx") ?? AssetDatabase.LoadAssetAtPath<GameObject>(Stored + w.file + ".fbx");

    // Triangles gardés d'un mot, retournés pour se lire depuis -Z, à la hauteur de capitale 1.
    static (List<Vector3> v, List<Vector3> n, List<int> t, float width) Extract(Word w)
    {
        var mf = Load(w).GetComponentInChildren<MeshFilter>();
        Mesh src = mf.sharedMesh;
        float scale = mf.transform.lossyScale.x;
        Vector3[] sv = src.vertices, sn = src.normals;
        int[] st = src.triangles;
        var p = new Vector3[sv.Length];
        float shift = w.removeTo - w.removeFrom;
        for (int i = 0; i < sv.Length; i++)
        {
            p[i] = sv[i] * scale;
            // La lettre en trop disparaît : ce qui la suit (vers +x) recule d'autant.
            if (p[i].x > w.removeTo) p[i].x -= shift;
        }
        bool Removed(int i) => sv[i].x * scale >= w.removeFrom && sv[i].x * scale <= w.removeTo;

        var map = new Dictionary<int, int>();
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var t = new List<int>();
        for (int k = 0; k + 2 < st.Length; k += 3)
        {
            int a = st[k], b = st[k + 1], c = st[k + 2];
            // Socle (tout le triangle sous la coupe) retiré ; le bas des lettres est aplani sur la coupe (bord net).
            if (p[a].y < w.baseCut && p[b].y < w.baseCut && p[c].y < w.baseCut) continue;
            if (Removed(a) || Removed(b) || Removed(c)) continue;
            foreach (int i in new[] { a, b, c })
            {
                if (!map.TryGetValue(i, out int j))
                {
                    j = map[i] = v.Count;
                    v.Add(new Vector3(p[i].x, Mathf.Max(p[i].y, w.baseCut), p[i].z));
                    n.Add(sn != null && sn.Length == sv.Length ? sn[i] : Vector3.up);
                }
                t.Add(j);
            }
        }
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var q in v) { minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y); }
        float cap = Mathf.Max(0.001f, maxY - minY);
        // Demi-tour autour de y (lecture vers +x, face avant vers -z), base à 0, hauteur 1, début du mot en x = 0.
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 q = v[i];
            v[i] = new Vector3(maxX - q.x, q.y - minY, -q.z) / cap;
            Vector3 m = n[i];
            n[i] = new Vector3(-m.x, m.y, -m.z);
        }
        return (v, n, t, (maxX - minX) / cap);
    }

    static Mesh Combine(Word first, Word second)
    {
        var a = Extract(first);
        var b = Extract(second);
        const float space = 0.35f;
        float offset = a.width + space, total = offset + b.width;
        var v = new List<Vector3>();
        var n = new List<Vector3>(a.n);
        n.AddRange(b.n);
        foreach (var q in a.v) v.Add(q - Vector3.right * total * 0.5f);
        foreach (var q in b.v) v.Add(q + Vector3.right * (offset - total * 0.5f));
        var t = new List<int>(a.t);
        foreach (int i in b.t) t.Add(i + a.v.Count);
        var mesh = new Mesh { name = first.file + second.file };
        if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
