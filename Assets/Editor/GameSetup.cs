using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Prépare le projet pour le GDD V0.1 : matériaux de base (Resources), import des modèles Meshy
// (Resources/Art) et nettoyage de la scène (le Hub et le labyrinthe sont générés au lancement).
public static class GameSetup
{
    const string MaterialsDir = "Assets/Resources/Materials";
    const string ArtDir = "Assets/Resources/Art";
    const string DecorDir = ArtDir + "/Decor";

    [MenuItem("FunGame/Configurer le jeu")]
    public static void Setup()
    {
        CreateMaterials();
        CreateFonts();
        CreateSounds();
        ImportArt();
        ImportDecor();
        DameSetup.Setup();
        IconRenderer.Generate();
        SetupScene();
        AssetDatabase.SaveAssets();
        Debug.Log("FunGame configuré.");
    }

    static void CreateMaterials()
    {
        EnsureFolder(MaterialsDir);
        CreateMaterial("Lit", "Standard", null);
        CreateMaterial("LitEmissive", "Standard", m =>
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });
        CreateMaterial("Unlit", "Unlit/Color", null);
        CreateMaterial("Outline", "FunGame/Outline", null);
        CreateMaterial("Text3D", "FunGame/Text3D", null);
        CreateMaterial("InfestedWall", "FunGame/InfestedWall", null);
        CreateMaterial("LaserBeam", "FunGame/LaserBeam", null);
        CreateMaterial("Spark", "FunGame/Spark", null);
        CreateMaterial("DustyLimb", "FunGame/DustyLimb", m => m.enableInstancing = true);
        CreateMaterial("Mist", "FunGame/Mist", null);
        CreateMaterial("Ectoplasm", "FunGame/Ectoplasm", null);
        CreateMaterial("GhostAura", "FunGame/GhostAura", null);
        CreateMaterial("EnergyBeam", "FunGame/EnergyBeam", null);

        // Sol du chapiteau : terre gravillonnée (texture 4K réduite pour le build WebGL).
        const string groundPath = "Assets/Materials/Basemap/zGround022_4K-PNG_Color.png";
        var groundImporter = (TextureImporter)AssetImporter.GetAtPath(groundPath);
        if (groundImporter != null && groundImporter.maxTextureSize != 1024)
        {
            groundImporter.maxTextureSize = 1024;
            groundImporter.SaveAndReimport();
        }
        var ground = AssetDatabase.LoadAssetAtPath<Texture2D>(groundPath);
        if (ground == null) Debug.LogError("Texture de sol introuvable : " + groundPath);
        CreateMaterial("HubFloor", "Standard", m =>
        {
            m.mainTexture = ground;
            m.color = new Color(1f, 0.92f, 0.82f);
            m.SetFloat("_Glossiness", 0.08f);
            m.SetFloat("_Metallic", 0f);
        });
    }

    // Sons enregistrés de Assets/Sounds et clé du son synthétisé qu'ils remplacent (voir SoundBank).
    static readonly (string key, string path)[] RecordedSounds =
    {
        ("dame_attack", "Assets/Sounds/AttackDame.wav"),
        ("bark", "Assets/Sounds/Aboyer.wav"),
    };

    static void CreateSounds()
    {
        const string path = "Assets/Resources/GameSounds.asset";
        var sounds = AssetDatabase.LoadAssetAtPath<GameSounds>(path);
        if (sounds == null)
        {
            sounds = ScriptableObject.CreateInstance<GameSounds>();
            AssetDatabase.CreateAsset(sounds, path);
        }
        var entries = new System.Collections.Generic.List<GameSounds.Entry>();
        foreach (var (key, clipPath) in RecordedSounds)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (clip == null)
            {
                Debug.LogError("Son introuvable : " + clipPath);
                continue;
            }
            entries.Add(new GameSounds.Entry { key = key, clip = clip });
        }
        sounds.sounds = entries.ToArray();
        EditorUtility.SetDirty(sounds);
    }

    // Police du jeu, à l'essai (l'ancienne : Assets/Font/PUSAB___.otf).
    const string FontPath = "Assets/Font/Nexa-Heavy.ttf";

    static void CreateFonts()
    {
        const string path = "Assets/Resources/GameFonts.asset";
        var fonts = AssetDatabase.LoadAssetAtPath<GameFonts>(path);
        if (fonts == null)
        {
            fonts = ScriptableObject.CreateInstance<GameFonts>();
            AssetDatabase.CreateAsset(fonts, path);
        }
        fonts.main = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (fonts.main == null) Debug.LogError("Police introuvable : " + FontPath);
        EditorUtility.SetDirty(fonts);
    }

    static void CreateMaterial(string name, string shaderName, System.Action<Material> configure)
    {
        string path = MaterialsDir + "/" + name + ".mat";
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("Shader introuvable : " + shaderName);
            return;
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        configure?.Invoke(mat);
        EditorUtility.SetDirty(mat);
    }

    // Modèles statiques Meshy : pas d'animation, matériaux et textures extraits à côté du modèle.
    internal static void ImportArt()
    {
        if (!AssetDatabase.IsValidFolder(ArtDir)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(DecorDir)) continue;
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            string name = Path.GetFileNameWithoutExtension(path);
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            string texDir = ArtDir + "/" + name + "_Textures";
            EnsureFolder(texDir);
            string texPath = texDir + "/" + name + "_BaseColor.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null) importer.ExtractTextures(texDir);
            AssetDatabase.Refresh();

            // Tous les FBX Meshy nomment leur matériau "Material.001" et leur texture "texture_0" :
            // on donne à chaque modèle sa propre texture et son propre matériau.
            string extracted = texDir + "/texture_0.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(extracted) != null && AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
                AssetDatabase.MoveAsset(extracted, texPath);
            string matPath = ArtDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            mat.color = Color.white;
            mat.SetFloat("_Glossiness", 0.25f);
            EditorUtility.SetDirty(mat);
            foreach (var source in importer.GetExternalObjectMap().Keys)
                if (source.type == typeof(Material)) importer.RemoveRemap(source);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Material.001"), mat);
            // Textures 2K de Meshy réduites : objets petits à l'écran, build WebGL léger.
            foreach (string texGuid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
            {
                var texImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(texGuid));
                if (texImporter.maxTextureSize == 512) continue;
                texImporter.maxTextureSize = 512;
                texImporter.SaveAndReimport();
            }
            importer.SaveAndReimport();
        }
    }

    // Décors Meshy sans texture (membres, pièces d'échecs) : le jeu leur applique ses propres matériaux.
    static void ImportDecor()
    {
        if (!AssetDatabase.IsValidFolder(DecorDir)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { DecorDir }))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            if (importer.materialImportMode == ModelImporterMaterialImportMode.None && !importer.importAnimation) continue;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }
    }

    static void SetupScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (string objName in new[] { "Arena", "Directional Light" })
        {
            var go = GameObject.Find(objName);
            if (go != null) Object.DestroyImmediate(go);
        }

        // Brouillard activé dans la scène pour que ses variantes de shader soient incluses dans le build.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.black;
        RenderSettings.fogDensity = 0.03f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.4f, 0.35f, 0.3f);
        RenderSettings.skybox = null;

        var player = Object.FindObjectOfType<PlayerController>(true);
        if (player != null)
        {
            player.gameObject.layer = 2;
            if (player.TryGetComponent(out Collider col) && col.sharedMaterial == null)
                col.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicMaterial>("Assets/Materials/NoFriction.physicMaterial");
            EditorUtility.SetDirty(player.gameObject);
        }
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            EditorUtility.SetDirty(cam);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
