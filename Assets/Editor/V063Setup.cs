using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Ressources de la V0.6.3 (chapitre 10.3 et Canard Coloré), à relancer si les modèles Meshy changent.
//   Tunnel : allégé, centré, longueur et diamètre ramenés à 1, UV cylindriques pour le damier (Resources/Art/Decor/TunnelCanin.asset).
//   Canard Coloré : texture et icône du canard désaturées, teintées en jeu par la couleur du Hub.
public static class V063Setup
{
    const string TunnelSource = "Assets/Models/Meshy/TunnelCanin.fbx";
    const string TunnelAsset = "Assets/Resources/Art/Decor/TunnelCanin.asset";
    const string StatueTexture = "Assets/Resources/Art/Decor/StatuePavlov_base_color.png";
    const string DuckTexture = "Assets/Resources/Art/Duck_Textures/Duck_BaseColor.png", DuckGrey = "Assets/Resources/Art/DuckGris.png";
    const string DuckIcon = "Assets/Resources/Icons/Canard.png", DuckIconGrey = "Assets/Resources/Icons/CanardGris.png";
    const int TunnelTriangles = 4000;
    // Cases du damier : 12 sur le tour et 6 sur la longueur (la texture en contient 2 par unité).
    const float TunnelU = 6f, TunnelV = 3f;

    [MenuItem("FunGame/Préparer les modèles V0.6.3")]
    public static void Build()
    {
        BuildTunnel();
        BuildGrey(DuckTexture, DuckGrey, false);
        BuildGrey(DuckIcon, DuckIconGrey, true);
        var statue = (TextureImporter)AssetImporter.GetAtPath(StatueTexture);
        statue.maxTextureSize = 1024;
        statue.SaveAndReimport();
        AssetDatabase.SaveAssets();
        Debug.Log("Modèles V0.6.3 prêts.");
    }

    static void BuildTunnel()
    {
        Mesh source = AssetDatabase.LoadAssetAtPath<GameObject>(TunnelSource).GetComponentInChildren<MeshFilter>().sharedMesh;
        // Copie lisible : l'outil d'allègement ne traite pas les maillages importés non lisibles.
        var working = new Mesh { name = "TunnelCanin", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = source.vertices, normals = source.normals, triangles = source.triangles };
        MeshDecimator.Simplify(working, TunnelTriangles);
        working.RecalculateBounds();
        working.RecalculateNormals();
        Bounds b = working.bounds;
        Vector3[] sv = working.vertices, sn = working.normals;
        int[] st = working.triangles;

        // Centré ; l'axe du tube (X) et son diamètre (Y, Z) ramenés à 1.
        var v = new List<Vector3>(sv.Length);
        var n = new List<Vector3>(sv);
        var uv = new List<Vector2>(sv.Length);
        n.Clear();
        var theta = new float[sv.Length];
        for (int i = 0; i < sv.Length; i++)
        {
            Vector3 p = sv[i] - b.center;
            p = new Vector3(p.x / b.size.x, p.y / b.size.y, p.z / b.size.z);
            v.Add(p);
            n.Add(new Vector3(sn[i].x * b.size.x, sn[i].y * b.size.y, sn[i].z * b.size.z).normalized);
            theta[i] = Mathf.Atan2(p.z, p.y) / (Mathf.PI * 2f) + 0.5f;
            uv.Add(new Vector2(theta[i] * TunnelU, (p.x + 0.5f) * TunnelV));
        }

        // Triangles à cheval sur la couture du cylindre : leurs sommets de l'autre côté sont dupliqués, un tour plus loin.
        var duplicate = new Dictionary<int, int>();
        for (int k = 0; k + 2 < st.Length; k += 3)
        {
            float t0 = theta[st[k]], t1 = theta[st[k + 1]], t2 = theta[st[k + 2]];
            if (Mathf.Max(t0, t1, t2) - Mathf.Min(t0, t1, t2) < 0.5f) continue;
            for (int c = 0; c < 3; c++)
            {
                int index = st[k + c];
                if (theta[index] >= 0.5f) continue;
                if (!duplicate.TryGetValue(index, out int copy))
                {
                    copy = v.Count;
                    duplicate[index] = copy;
                    v.Add(v[index]);
                    n.Add(n[index]);
                    uv.Add(uv[index] + new Vector2(TunnelU, 0f));
                }
                st[k + c] = copy;
            }
        }

        var mesh = new Mesh { name = "TunnelCanin" };
        if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(v);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(st, 0);
        mesh.RecalculateBounds();
        Object.DestroyImmediate(working);
        AssetDatabase.DeleteAsset(TunnelAsset);
        AssetDatabase.CreateAsset(mesh, TunnelAsset);
    }

    // Copie désaturée (la transparence est conservée) ; 'sprite' : même réglage que l'icône d'origine.
    static void BuildGrey(string from, string to, bool sprite)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(from));
        Color[] px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++)
        {
            float l = Mathf.Clamp01(px[i].grayscale * 1.2f);
            px[i] = new Color(l, l, l, px[i].a);
        }
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(to, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceUpdate);
        if (!sprite) return;
        var original = (TextureImporter)AssetImporter.GetAtPath(from);
        var importer = (TextureImporter)AssetImporter.GetAtPath(to);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = original.mipmapEnabled;
        importer.maxTextureSize = original.maxTextureSize;
        importer.SaveAndReimport();
    }
}
