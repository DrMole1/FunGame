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
        OcclusionSetup.Generate();
        AssetDatabase.SaveAssets();
        Debug.Log("FunGame configuré.");
    }

    // V0.6.1 : police du titre, sons des glyphes et de la Cloche, modèles Meshy (bornes d'arcade, lots, porte blindée) et
    // icônes d'inventaire, sans reconfigurer la scène.
    [MenuItem("FunGame/Installer la V0.6.1")]
    public static void SetupV061()
    {
        CreateFonts();
        CreateSounds();
        ImportArt();
        IconRenderer.Generate();
        AssetDatabase.SaveAssets();
        Debug.Log("V0.6.1 installée.");
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
        CreateMaterial("Ectoplasm", "FunGame/Ectoplasm", null);
        CreateMaterial("GhostAura", "FunGame/GhostAura", null);
        CreateMaterial("BeaconHalo", "FunGame/BeaconHalo", m => m.enableInstancing = true);
        CreateMaterial("EnergyBeam", "FunGame/EnergyBeam", null);
        CreateMaterial("Glow", "FunGame/Glow", null);
        CreateMaterial("Translucent", "FunGame/Translucent", null);
        CreateMaterial("Explosion", "FunGame/Explosion", null);
        CreateMaterial("FilmGrain", "FunGame/FilmGrain", null);

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

    // Sons enregistrés de Assets/Sounds et clé du son synthétisé qu'ils remplacent (voir SoundBank) : tableau du
    // chapitre 12 du GDD (Sound Design). Ces fichiers sont déjà égalisés : ne pas les remplacer par des sons synthétisés.
    static readonly (string key, string path)[] RecordedSounds =
    {
        ("bark", "Assets/Sounds/Aboyer.wav"),
        ("dame_attack", "Assets/Sounds/Dame_Attaque.wav"),
        ("dame_kill", "Assets/Sounds/Dame_Tuer.wav"),
        ("dame_scream", "Assets/Sounds/Dame_Cri.wav"),
        ("death", "Assets/Sounds/Perso_Mort.wav"),
        ("tour_charge", "Assets/Sounds/Tour_Charge.wav"),
        ("tour_channel", "Assets/Sounds/Tour_Canalisation.wav"),
        ("tour_kill", "Assets/Sounds/Tour_Tuer.wav"),
        ("fou_alert", "Assets/Sounds/Fou_Alerte.wav"),
        ("fou_attack", "Assets/Sounds/Fou_TP.wav"),
        ("fou_kill", "Assets/Sounds/Fou_Tuer.wav"),
        ("roi_laugh", "Assets/Sounds/Roi_Rire.wav"),
        ("roi_flash", "Assets/Sounds/Roi_Flash.wav"),
        ("roi_chain", "Assets/Sounds/Roi_Chaine.wav"),
        ("roi_kill", "Assets/Sounds/Roi_Tuer.wav"),
        // V0.4.9
        ("dame_step", "Assets/Sounds/Dame_Deplacement.wav"),
        ("dame_wail", "Assets/Sounds/Dame_Cri2.wav"),
        ("tour_alert", "Assets/Sounds/Tour_Alerte.wav"),
        ("tour_breath", "Assets/Sounds/Tour_Respiration.wav"),
        ("roi_slash", "Assets/Sounds/Roi_Epee.wav"),
        ("roi_ambience", "Assets/Sounds/Roi_Autour.wav"),
        ("skin", "Assets/Sounds/Changer_Skin.wav"),
        ("laser_hit", "Assets/Sounds/Laser_Degat.wav"),
        ("pion_cry", "Assets/Sounds/Pion_Alerte.wav"),
        ("pion_attack", "Assets/Sounds/Pion_Attaque.wav"),
        ("spider_attack", "Assets/Sounds/Araignee_Attaque.wav"),
        ("spider_kill", "Assets/Sounds/Araignee_Tuer.wav"),
        ("cavalier_gallop", "Assets/Sounds/Cavalier_Deplacement.wav"),
        ("cavalier_alert", "Assets/Sounds/Cavalier_Alerte.wav"),
        ("cavalier_terror", "Assets/Sounds/Cavalier_Immobilise.wav"),
        ("cavalier_kill", "Assets/Sounds/Cavalier_Tuer.wav"),
        // V0.6.11 : pause du Cavalier (coup de masse au sol), recharge du Téléporteur Inversé terminée, Cauchemars de la manche.
        ("cavalier_stomp", "Assets/Sounds/Cavalier_Pause.wav"),
        ("teleporter_ready", "Assets/Sounds/Teleporteur_Pret.wav"),
        ("round_spawn", "Assets/Sounds/Spawn_Manche.wav"),
        // V0.4.11 : battement des pulsations cardiaques (un « poum-poum » par pulsation).
        ("heart_thump", "Assets/Sounds/Battement_Coeur.wav"),
        // Musique du chapiteau, en boucle.
        ("hub_music", "Assets/Sounds/Musique_Hub.wav"),
        // GDD 12 (V0.6.1) : sons des glyphes (une fois pour un glyphe neutre, en boucle pour un glyphe d'acquisition).
        ("glyph_sit", "Assets/Sounds/Glyphe01.wav"),
        ("glyph_kennel", "Assets/Sounds/Glyphe02.wav"),
        ("glyph_bark", "Assets/Sounds/Glyphe03.wav"),
        // Cloche : La Cloche qui obnubile (aigu), la Cloche Décorative posée, la Tête de Cloche qui frappe sa tête (grave).
        ("cloche_ring", "Assets/Sounds/Cloche.wav"),
        ("bell_deco", "Assets/Sounds/Cloche.wav"),
        ("tete_bell", "Assets/Sounds/Cloche.wav"),
        // V0.6.9 : mise à mort et obnubilation par la Tête de Cloche, fermeture éclair de fin de chargement.
        ("tete_kill", "Assets/Sounds/TeteDeCloche_Tuer.wav"),
        ("tete_vue", "Assets/Sounds/TeteDeCloche_Vue.wav"),
        ("zipper", "Assets/Sounds/Zip.wav"),
        // V0.6.7 : ordres vocaux des glyphes, selon la langue du joueur.
        ("order_fr_01", "Assets/Sounds/Order_FR_01.wav"), ("order_fr_02", "Assets/Sounds/Order_FR_02.wav"), ("order_fr_03", "Assets/Sounds/Order_FR_03.wav"),
        ("order_eng_01", "Assets/Sounds/Order_ENG_01.wav"), ("order_eng_02", "Assets/Sounds/Order_ENG_02.wav"), ("order_eng_03", "Assets/Sounds/Order_ENG_03.wav"),
        ("order_esp_01", "Assets/Sounds/Order_ESP_01.wav"), ("order_esp_02", "Assets/Sounds/Order_ESP_02.wav"), ("order_esp_03", "Assets/Sounds/Order_ESP_03.wav"),
        ("order_rus_01", "Assets/Sounds/Order_RUS_01.wav"), ("order_rus_02", "Assets/Sounds/Order_RUS_02.wav"), ("order_rus_03", "Assets/Sounds/Order_RUS_03.wav"),
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
    const string TitleImagePath = "Assets/Sprite/Title.png";

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
        // Titre du lobby, importé en sprite.
        if (AssetImporter.GetAtPath(TitleImagePath) is TextureImporter titleImporter && (titleImporter.textureType != TextureImporterType.Sprite || !titleImporter.alphaIsTransparency))
        {
            titleImporter.textureType = TextureImporterType.Sprite;
            titleImporter.spriteImportMode = SpriteImportMode.Single;
            titleImporter.alphaIsTransparency = true;
            titleImporter.mipmapEnabled = false;
            titleImporter.SaveAndReimport();
        }
        fonts.titleImage = AssetDatabase.LoadAssetAtPath<Sprite>(TitleImagePath);
        if (fonts.titleImage == null) Debug.LogError("Image introuvable : " + TitleImagePath);
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
    // Grands décors vus de près (entrée du chapiteau) : texture en 1024.
    static readonly string[] ReadableArt = { "Tringle", "MachinePince", "PogoCouvercle", "PorteChapiteau", "Trampoline", "MasseCavalier" };
    static readonly string[] LargeArt = { "EntreeChapiteau", "Rideau", "Roulette", "MachinePince", "ArcadeDance", "PorteBlindee", "PogoCouvercle", "PorteChapiteau" };

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
            // Textures 2K de Meshy réduites : objets petits à l'écran, build WebGL léger (1024 pour les grands décors).
            int texSize = System.Array.IndexOf(LargeArt, name) >= 0 ? 1024 : 512;
            foreach (string texGuid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
            {
                var texImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(texGuid));
                if (texImporter.maxTextureSize == texSize) continue;
                texImporter.maxTextureSize = texSize;
                texImporter.SaveAndReimport();
            }
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            // V0.6.1 : la vitrine de la Machine à pince est découpée en jeu (maillage lisible).
            if (System.Array.IndexOf(ReadableArt, name) >= 0) importer.isReadable = true;
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
