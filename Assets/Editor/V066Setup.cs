using UnityEditor;
using UnityEngine;

// V0.6.6 : pièces Meshy du pont suspendu (parcelle, bâton, corde) et de l'arche, générées sans texture. Mises ici à leurs
// dimensions de jeu, avec les coordonnées de texture qui y dessinent le damier noir et blanc.
// Résultat : Resources/Art/Decor/PontParcelle.asset, PontBaton.asset, PontCorde.asset et Arche.asset (l'arche tient dans un
// cube de 1 m, base en y = 0, centrée en x et z).
public static class V066Setup
{
    const string Raw = "Assets/Models/Meshy/", Out = "Assets/Resources/Art/Decor/";

    [MenuItem("FunGame/Préparer les modèles V0.6.6")]
    public static void Build()
    {
        // Parcelle : longueur du modèle en travers du pont (X), largeur le long du pont (Z), épaisseur en Y.
        Save("PontParcelle", p => new Vector3(p.x * 1.6f, p.z * 0.06f, p.y * 0.46f), p => new Vector2(p.x / 1.6f + 0.5f, 0.25f));
        // Bâton debout, base en y = 0.
        Save("PontBaton", p => new Vector3(p.x * 0.14f, (p.y + 0.5f) * 1.25f, p.z * 0.14f), p => new Vector2(0.25f, p.y / 1.25f * 2f));
        // Corde le long de Z, 1 m de long (étirée à la pose).
        Save("PontCorde", p => new Vector3(p.z * 0.06f, p.y * 0.06f, p.x), p => new Vector2(0.25f, p.z + 0.5f));
        Save("Arche", p => new Vector3(p.x, p.y + 0.5f, p.z), p => Vector2.zero);
        // Tronçon de toboggan : rayon extérieur 1, le long de Z de 0 à 1 (plié le long du trajet à la génération).
        Save("TobogganTroncon", p => new Vector3(p.x * 2f, p.y * 2f, p.z + 0.5f), p => Vector2.zero);
        AssetDatabase.SaveAssets();
        Debug.Log("Modèles V0.6.6 prêts.");
    }

    // 'place' reçoit un point du modèle ramené dans un cube de 1 m centré sur l'origine.
    static void Save(string name, System.Func<Vector3, Vector3> place, System.Func<Vector3, Vector2> uv)
    {
        Mesh source = AssetDatabase.LoadAssetAtPath<GameObject>(Raw + name + ".fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
        Bounds b = source.bounds;
        Vector3[] v = source.vertices, n = source.normals;
        var vertices = new Vector3[v.Length];
        var normals = new Vector3[v.Length];
        var uvs = new Vector2[v.Length];
        Vector3 size = Vector3.Max(b.size, Vector3.one * 0.000001f);
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 unit = Vector3.Scale(v[i] - b.center, new Vector3(1f / size.x, 1f / size.y, 1f / size.z));
            vertices[i] = place(unit);
            uvs[i] = uv(vertices[i]);
            // Normale recalculée par la différence de position après la mise à l'échelle.
            normals[i] = (place(unit + Vector3.Scale(n[i], new Vector3(1f / size.x, 1f / size.y, 1f / size.z)) * 0.0001f) - vertices[i]).normalized;
        }
        var mesh = new Mesh { name = name, vertices = vertices, normals = normals, uv = uvs, triangles = source.triangles };
        mesh.RecalculateBounds();
        string path = Out + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
    }
}
