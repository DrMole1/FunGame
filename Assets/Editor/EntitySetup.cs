using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Contenu de la V0.4 du GDD : La Tour et La Cloche (modèles Meshy riggés, Assets/Models/<Nom>/), objets du Terminal
// (Téléporteur, tête du Pion : Resources/Art) et icônes d'inventaire. V0.7.10 : plus de Balise Améliorée ni de
// Balises de Hub recolorées (seul leur orbe prend la couleur du Hub).
public static class EntitySetup
{
    [MenuItem("FunGame/Installer les entités V0.4")]
    public static void Setup()
    {
        GameSetup.ImportArt();
        SetupCreature("Tour", 2.66f, new[] { ("Walk", true), ("Run", true), ("Look", true), ("Stance", true) });
        SetupCreature("Cloche", 1f, new[] { ("Walk", true), ("Run", true) });
        SetupFou();
        SetupRoi();
        SetupCavalier();
        SetupReveil();
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

    // Le Roi (V0.4.4) : marche lente (le bras qui lance la chaîne est orienté par le code) ; de la taille de La Dame.
    [MenuItem("FunGame/Installer Le Roi")]
    public static void SetupRoi()
    {
        SetupCreature("Roi", 2.66f, new[] { ("Walk", true), ("Run", true) });
        AssetDatabase.SaveAssets();
    }

    // Le Cavalier (V0.4.7) : marche, course et frappe à la masse ; de la taille de La Dame (agrandi de 25 % en jeu).
    [MenuItem("FunGame/Installer Le Cavalier")]
    public static void SetupCavalier()
    {
        GameSetup.ImportArt();
        SetupCreature("Cavalier", 3.33f, new[] { ("Walk", true), ("Run", true), ("Attack", false) });
        IconRenderer.Generate();
        AssetDatabase.SaveAssets();
    }

    // La Tête de Cloche (V0.6) : marche, course et coup de marteau (mise à mort) ; de la taille de La Dame. Son marteau
    // (Resources/Art/MarteauTete) est importé avec les autres modèles.
    [MenuItem("FunGame/Installer La Tête de Cloche")]
    public static void SetupTeteDeCloche()
    {
        GameSetup.ImportArt();
        SetupCreature("TeteDeCloche", 2.66f, new[] { ("Walk", true), ("Run", true), ("Attack", false) });
        AssetDatabase.SaveAssets();
    }

    // Le Réveil (V0.4.7) : marche et course sur ses petites pattes ; de la taille du Pion.
    [MenuItem("FunGame/Installer Le Réveil")]
    public static void SetupReveil()
    {
        SetupCreature("Reveil", 1.15f, new[] { ("Walk", true), ("Run", true) });
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
}
