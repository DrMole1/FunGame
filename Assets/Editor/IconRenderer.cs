using System.IO;
using UnityEditor;
using UnityEngine;

// Icônes d'inventaire (Resources/Icons/<ItemType>.png, importées en sprites) :
// photographie des modèles Meshy sur fond transparent. Les icônes sans modèle (Post-It, Clé, Chargeur) sont des images
// générées, déposées telles quelles dans le dossier ; ce menu ne fait que (ré)importer leurs réglages.
public static class IconRenderer
{
    const string IconDir = "Assets/Resources/Icons";
    const int Size = 256;

    [MenuItem("FunGame/Générer les icônes d'inventaire")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(IconDir)) AssetDatabase.CreateFolder("Assets/Resources", "Icons");
        // Certains modèles Meshy ont un socle : on cadre la partie haute (l'objet lui-même).
        // V0.5.1 : la Cuisse de poulet remplace la Croquette (objet 1).
        Render("CuisseDePoulet", ItemType.CuisseDePoulet, new Vector3(20f, -45f, -35f), 0f);
        Render("DuckSolo", ItemType.Canard, new Vector3(8f, -140f, 0f), 0f);
        Render("Balise", ItemType.Balise, new Vector3(10f, -150f, 0f), 0f);
        Render("Balise", ItemType.BaliseAmelioree, new Vector3(10f, -150f, 0f), 0f, "BaliseRouge");
        Render("Teleporteur", ItemType.Teleporteur, new Vector3(45f, -150f, 0f), 0f);
        Render("StationCurative", ItemType.StationCurative, new Vector3(10f, -150f, 0f), 0f);
        // V0.4.6 : l'Os (monticules) et le Canon (Terminal).
        Render("Os", ItemType.Os, new Vector3(25f, -20f, 0f), 0f);
        Render("CanonSolo", ItemType.Canon, new Vector3(20f, -130f, 0f), 0f);
        // V0.4.7 : objets uniques.
        Render("Television", ItemType.Television, new Vector3(10f, -160f, 0f), 0f);
        Render("Trampoline", ItemType.Trampoline, new Vector3(30f, -150f, 0f), 0f);
        Render("ClocheDecorative", ItemType.ClocheDecorative, new Vector3(10f, -150f, 0f), 0f);
        AssetDatabase.Refresh();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconDir }))
            ConfigureSprite(AssetDatabase.GUIDToAssetPath(guid));
        Debug.Log("Icônes d'inventaire générées.");
    }

    static void Render(string model, ItemType type, Vector3 viewAngles, float skipBottom, string material = null)
    {
        var prefab = Resources.Load<GameObject>("Art/" + model);
        if (prefab == null)
        {
            Debug.LogWarning("Icône : modèle " + model + " introuvable.");
            return;
        }
        // Scène isolée, loin du reste.
        var root = new GameObject("IconStudio");
        root.transform.position = new Vector3(0f, -500f, 0f);
        var go = Object.Instantiate(prefab, root.transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = prefab.transform.localRotation; // rotation d'import conservée (FBX Meshy remaillés)
        // Variante d'un modèle (Balise Améliorée) : même maillage, autre matériau.
        var swap = material != null ? Resources.Load<Material>("Art/" + material) : null;
        if (swap != null)
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = swap;

        Bounds all = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) all.Encapsulate(r.bounds);
        // Cadrage sur les sommets au-dessus du socle ('skipBottom' : part de la hauteur ignorée).
        float minY = all.min.y + all.size.y * skipBottom;
        Bounds b = new Bounds();
        bool any = false;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            foreach (var v in mf.sharedMesh.vertices)
            {
                Vector3 w = mf.transform.TransformPoint(v);
                if (w.y < minY) continue;
                if (!any) { b = new Bounds(w, Vector3.zero); any = true; }
                else b.Encapsulate(w);
            }
        if (!any) b = all;

        var light = new GameObject("Key").AddComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        var fill = new GameObject("Fill").AddComponent<Light>();
        fill.transform.SetParent(root.transform, false);
        fill.type = LightType.Directional;
        fill.intensity = 0.5f;
        fill.transform.rotation = Quaternion.Euler(10f, 150f, 0f);

        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.transform.SetParent(root.transform, false);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.orthographic = true;
        cam.orthographicSize = b.extents.magnitude * 1.02f;
        Quaternion rot = Quaternion.Euler(viewAngles);
        cam.transform.SetPositionAndRotation(b.center - rot * Vector3.forward * (b.extents.magnitude * 4f), rot);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = b.extents.magnitude * 8f;

        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        var oldAmbient = RenderSettings.ambientLight;
        var oldFog = RenderSettings.fog;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);
        RenderSettings.fog = false;
        cam.Render();
        RenderSettings.ambientLight = oldAmbient;
        RenderSettings.fog = oldFog;

        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(IconDir, type + ".png"), tex.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(root);
    }

    static void ConfigureSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 256;
        importer.SaveAndReimport();
    }
}
