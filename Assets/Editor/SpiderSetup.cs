using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// L'Araignée : le modèle Meshy (Assets/Models/Spider/Spider_Full.fbx) est d'un seul tenant et le rig Meshy ne gère
// que les bipèdes. On le découpe donc en un corps (tout ce qui tient dans un ellipsoïde autour de l'abdomen et de la
// tête) et ses pattes rigides (les gros morceaux restants ; deux pattes qui se touchent forment un seul morceau), chacune avec son pivot à la hanche, animées en code (Spider.cs).
// Résultat : Resources/Art/Spider.prefab (enfants "Corps" et "Patte0", "Patte1"...) et ses maillages dans Spider_Meshes.asset.
public static class SpiderSetup
{
    const string Source = "Assets/Models/Spider/Spider_Full.fbx";
    const string TexturePath = "Assets/Models/Spider/Spider_BaseColor.png";
    const string MeshesPath = "Assets/Resources/Art/Spider_Meshes.asset";
    const string MaterialPath = "Assets/Resources/Art/Spider.mat";
    const string PrefabPath = "Assets/Resources/Art/Spider.prefab";

    // Corps, en unités du modèle mis à l'échelle (mesuré sur les vues de face et de dessus).
    static readonly Vector3 BodyCenter = new Vector3(0f, 0.07f, -0.18f);
    static readonly Vector3 BodyRadii = new Vector3(0.58f, 0.46f, 0.86f);
    // Tête (yeux, crocs) : elle dépasse de l'ellipsoïde du corps, à l'avant.
    const int LegPieces = 10, MinLegTriangles = 120;
    static readonly Vector3 HeadCenter = new Vector3(0f, 0.12f, 0.4f);
    static readonly Vector3 HeadRadii = new Vector3(0.22f, 0.36f, 0.32f);

    [MenuItem("FunGame/Installer l'Araignée")]
    public static void Setup()
    {
        var importer = AssetImporter.GetAtPath(Source) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning("Araignée : " + Source + " introuvable, le jeu utilisera le modèle en primitives.");
            return;
        }
        importer.isReadable = true;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
        var tex = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        if (tex != null && tex.maxTextureSize != 1024)
        {
            tex.maxTextureSize = 1024;
            tex.SaveAndReimport();
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var filter = model.GetComponentInChildren<MeshFilter>();
        Mesh src = filter.sharedMesh;
        // Le prefab du FBX est à l'origine : son échelle (x100) est appliquée aux sommets.
        Matrix4x4 toModel = filter.transform.localToWorldMatrix;
        Vector3[] verts = src.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] = toModel.MultiplyPoint3x4(verts[i]);
        Vector3[] normals = src.normals;
        for (int i = 0; i < normals.Length; i++) normals[i] = toModel.MultiplyVector(normals[i]).normalized;
        Vector2[] uvs = src.uv;
        int[] tris = src.triangles;

        // Triangles du corps : les trois sommets dans l'ellipsoïde.
        var bodyTris = new List<int>();
        var legTris = new List<int>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            bool inside = Inside(verts[tris[t]]) && Inside(verts[tris[t + 1]]) && Inside(verts[tris[t + 2]]);
            (inside ? bodyTris : legTris).Add(t);
        }

        // Pattes : morceaux connexes (sommets soudés par position, les coutures d'UV dupliquent les sommets).
        var weld = new Dictionary<Vector3Int, int>();
        int[] key = new int[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var q = Vector3Int.RoundToInt(verts[i] * 2000f);
            if (!weld.TryGetValue(q, out int k)) weld[q] = k = weld.Count;
            key[i] = k;
        }
        int[] parent = new int[weld.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        foreach (int t in legTris)
        {
            Union(parent, key[tris[t]], key[tris[t + 1]]);
            Union(parent, key[tris[t]], key[tris[t + 2]]);
        }
        var groups = new Dictionary<int, List<int>>();
        foreach (int t in legTris)
        {
            int root = Find(parent, key[tris[t]]);
            if (!groups.TryGetValue(root, out var list)) groups[root] = list = new List<int>();
            list.Add(t);
        }
        var pieces = new List<List<int>>(groups.Values);
        pieces.Sort((a, b) => b.Count.CompareTo(a.Count));
        var sizes = new List<string>();
        for (int p = 0; p < Mathf.Min(12, pieces.Count); p++) sizes.Add(pieces[p].Count.ToString());
        Debug.Log("Araignée : morceaux hors du corps (triangles) : " + string.Join(", ", sizes));
        // Les plus gros morceaux sont les pattes ; au-delà de LegPieces, ils rejoignent la patte la plus proche.
        // Les petits morceaux (crocs, poils) restent sur le corps.
        while (pieces.Count > 0 && pieces[pieces.Count - 1].Count < MinLegTriangles)
        {
            bodyTris.AddRange(pieces[pieces.Count - 1]);
            pieces.RemoveAt(pieces.Count - 1);
        }
        int legCount = Mathf.Min(LegPieces, pieces.Count);
        var legs = pieces.GetRange(0, legCount);
        var legCenters = new List<Vector3>();
        foreach (var leg in legs) legCenters.Add(Centroid(leg, tris, verts));
        for (int p = legCount; p < pieces.Count; p++)
        {
            Vector3 c = Centroid(pieces[p], tris, verts);
            int best = 0;
            for (int l = 1; l < legCount; l++)
                if ((legCenters[l] - c).sqrMagnitude < (legCenters[best] - c).sqrMagnitude) best = l;
            legs[best].AddRange(pieces[p]);
        }

        var meshes = new List<Mesh>();
        var pivots = new List<Vector3>();
        meshes.Add(Build("Corps", bodyTris, tris, verts, normals, uvs, Vector3.zero));
        pivots.Add(Vector3.zero);
        for (int l = 0; l < legCount; l++)
        {
            // Pivot : le sommet de la patte le plus proche du corps (sa hanche).
            Vector3 hip = Vector3.zero;
            float best = float.MaxValue;
            foreach (int t in legs[l])
                for (int k = 0; k < 3; k++)
                {
                    Vector3 v = verts[tris[t + k]];
                    float d = Ellipsoid(v);
                    if (d < best) { best = d; hip = v; }
                }
            meshes.Add(Build("Patte" + l, legs[l], tris, verts, normals, uvs, hip));
            pivots.Add(hip);
        }

        // Maillages dans un seul asset, matériau et prefab.
        AssetDatabase.DeleteAsset(MeshesPath);
        var container = ScriptableObject.CreateInstance<SpiderMeshes>();
        AssetDatabase.CreateAsset(container, MeshesPath);
        foreach (var m in meshes) AssetDatabase.AddObjectToAsset(m, container);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        mat.color = Color.white;
        mat.SetFloat("_Glossiness", 0.55f);
        EditorUtility.SetDirty(mat);

        var rootGo = new GameObject("Spider");
        for (int i = 0; i < meshes.Count; i++)
        {
            var part = new GameObject(meshes[i].name);
            part.transform.SetParent(rootGo.transform, false);
            part.transform.localPosition = pivots[i];
            part.AddComponent<MeshFilter>().sharedMesh = meshes[i];
            var r = part.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        PrefabUtility.SaveAsPrefabAsset(rootGo, PrefabPath);
        Object.DestroyImmediate(rootGo);
        AssetDatabase.SaveAssets();
        Debug.Log("Araignée installée : corps (" + bodyTris.Count + " triangles) et " + legCount + " pattes.");
    }

    static float Ellipsoid(Vector3 v) => Mathf.Min(Ellipsoid(v, BodyCenter, BodyRadii), Ellipsoid(v, HeadCenter, HeadRadii));

    static float Ellipsoid(Vector3 v, Vector3 center, Vector3 radii)
    {
        Vector3 d = v - center;
        return (d.x / radii.x) * (d.x / radii.x) + (d.y / radii.y) * (d.y / radii.y) + (d.z / radii.z) * (d.z / radii.z);
    }

    static bool Inside(Vector3 v) => Ellipsoid(v) <= 1f;

    static int Find(int[] parent, int i)
    {
        while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
        return i;
    }

    static void Union(int[] parent, int a, int b) => parent[Find(parent, a)] = Find(parent, b);

    static Vector3 Centroid(List<int> triStarts, int[] tris, Vector3[] verts)
    {
        Vector3 sum = Vector3.zero;
        foreach (int t in triStarts) sum += verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]];
        return sum / Mathf.Max(1, triStarts.Count * 3);
    }

    // Sous-maillage des triangles donnés, sommets exprimés depuis 'origin'.
    static Mesh Build(string name, List<int> triStarts, int[] tris, Vector3[] verts, Vector3[] normals, Vector2[] uvs, Vector3 origin)
    {
        var map = new Dictionary<int, int>();
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var uv = new List<Vector2>();
        var idx = new List<int>();
        foreach (int t in triStarts)
            for (int k = 0; k < 3; k++)
            {
                int i = tris[t + k];
                if (!map.TryGetValue(i, out int j))
                {
                    map[i] = j = v.Count;
                    v.Add(verts[i] - origin);
                    n.Add(normals.Length > i ? normals[i] : Vector3.up);
                    uv.Add(uvs.Length > i ? uvs[i] : Vector2.zero);
                }
                idx.Add(j);
            }
        var mesh = new Mesh { name = name };
        if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(idx, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
