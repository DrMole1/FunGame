using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Contenu de la V0.4 du GDD : La Tour et La Cloche (modèles Meshy riggés, Assets/Models/<Nom>/), objets du Terminal
// (Téléporteur, Station Curative, tête du Pion : Resources/Art), Balise Améliorée (texture de la Balise virée au rouge)
// et icônes d'inventaire.
public static class EntitySetup
{
    [MenuItem("FunGame/Installer les entités V0.4")]
    public static void Setup()
    {
        GameSetup.ImportArt();
        CreateRedBeacon();
        SetupCreature("Tour", 2.66f, new[] { ("Walk", true), ("Run", true), ("Look", true), ("Stance", true) });
        SetupCreature("Cloche", 1f, new[] { ("Walk", true), ("Run", true) });
        SetupFou();
        SpiderSetup.Setup();
        IconRenderer.Generate();
        AssetDatabase.SaveAssets();
        Debug.Log("Entités et objets de la V0.4 installés.");
    }

    // Le Fou (V0.4.3) : marche cartoon ("Funky Walk"), course, danse et cri d'alerte ; de la taille de La Dame.
    [MenuItem("FunGame/Installer Le Fou")]
    public static void SetupFou()
    {
        SetupCreature("Fou", 2.66f, new[] { ("Walk", true), ("Run", true), ("Dance", true), ("Scream", true) });
        AssetDatabase.SaveAssets();
    }

    // Import Humanoid, Animator (Idle = première image de la marche, puis un état par clip) et asset Resources/<Nom>Art.
    static void SetupCreature(string name, float height, (string clip, bool loop)[] clips)
    {
        string dir = "Assets/Models/" + name + "/";
        string riggedPath = dir + name + "_Rigged.fbx";
        var rigged = AssetImporter.GetAtPath(riggedPath) as ModelImporter;
        if (rigged == null)
        {
            Debug.LogWarning(name + " : " + riggedPath + " introuvable.");
            return;
        }
        rigged.animationType = ModelImporterAnimationType.Human;
        rigged.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        rigged.importAnimation = false;
        if (!AssetDatabase.IsValidFolder(dir + "Textures")) AssetDatabase.CreateFolder(dir.TrimEnd('/'), "Textures");
        rigged.ExtractTextures(dir + "Textures");
        AssetDatabase.Refresh();
        rigged.SaveAndReimport();

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(riggedPath).OfType<Avatar>().First();
        if (!avatar.isValid || !avatar.isHuman) Debug.LogError(name + " : avatar Humanoid invalide.");

        string controllerPath = dir + name + ".controller";
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idle = null;
        foreach (var (clipName, loop) in clips)
        {
            AnimationClip clip = CharacterSetup.ImportClip(dir + name + "_" + clipName + ".fbx", clipName, avatar, loop, true);
            if (idle == null) idle = CharacterSetup.AddState(sm, "Idle", clip, 0f);
            CharacterSetup.AddState(sm, clipName, clip, 1f);
        }
        sm.defaultState = idle;

        string artPath = "Assets/Resources/" + name + "Art.asset";
        var art = AssetDatabase.LoadAssetAtPath<CreatureArt>(artPath);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<CreatureArt>();
            AssetDatabase.CreateAsset(art, artPath);
        }
        art.model = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        art.controller = controller;
        art.scale = height / CharacterSetup.MeasureHeight(art.model);
        EditorUtility.SetDirty(art);
        Debug.Log(name + " installée (échelle " + art.scale.ToString("0.###") + ").");
    }

    // Balise Améliorée : la texture de la Balise dont le cristal bleu devient rouge.
    static void CreateRedBeacon()
    {
        const string source = "Assets/Resources/Art/Balise_Textures/Balise_BaseColor.png";
        const string texDir = "Assets/Resources/Art/BaliseRouge_Textures";
        const string texPath = texDir + "/BaliseRouge_BaseColor.png";
        if (!File.Exists(source)) return;
        if (!AssetDatabase.IsValidFolder(texDir)) AssetDatabase.CreateFolder("Assets/Resources/Art", "BaliseRouge_Textures");
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(source));
        Color[] px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            // Teintes bleues (cristal et lueurs) : le bleu passe dans le rouge.
            if (c.b > c.r + 0.08f && c.b > c.g)
                px[i] = new Color(c.b, c.g * 0.35f, c.r * 0.6f, c.a);
        }
        tex.SetPixels(px);
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();

        const string matPath = "Assets/Resources/Art/BaliseRouge.mat";
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
    }
}
