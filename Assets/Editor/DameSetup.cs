using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Prépare La Dame à partir des fichiers Meshy de Assets/Models/Dame/ : import Humanoid, textures,
// Animator (Idle, Walk, Run, Attack) et asset Resources/DameArt lu par le jeu.
public static class DameSetup
{
    const string Dir = "Assets/Models/Dame/";
    const float TargetHeight = 2.31f; // plus grande que les personnages (1,7 m)

    [MenuItem("FunGame/Installer La Dame")]
    public static void Setup()
    {
        string riggedPath = Dir + "Dame_Rigged.fbx";
        var rigged = AssetImporter.GetAtPath(riggedPath) as ModelImporter;
        if (rigged == null)
        {
            Debug.LogWarning("La Dame : " + riggedPath + " introuvable, le jeu utilisera une silhouette en primitives.");
            return;
        }
        rigged.animationType = ModelImporterAnimationType.Human;
        rigged.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        rigged.importAnimation = false;
        if (!AssetDatabase.IsValidFolder(Dir + "Textures")) AssetDatabase.CreateFolder(Dir.TrimEnd('/'), "Textures");
        rigged.ExtractTextures(Dir + "Textures");
        AssetDatabase.Refresh();
        rigged.SaveAndReimport();

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(riggedPath).OfType<Avatar>().First();
        if (!avatar.isValid || !avatar.isHuman) Debug.LogError("La Dame : avatar Humanoid invalide.");

        AnimationClip walk = CharacterSetup.ImportClip(Dir + "Dame_Walk.fbx", "Walk", avatar, true, true);
        AnimationClip run = CharacterSetup.ImportClip(Dir + "Dame_Run.fbx", "Run", avatar, true, true);
        AnimationClip attack = AssetImporter.GetAtPath(Dir + "Dame_Attack.fbx") != null
            ? CharacterSetup.ImportClip(Dir + "Dame_Attack.fbx", "Attack", avatar, false, true)
            : walk;

        string controllerPath = Dir + "Dame.controller";
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        // Pas de clip d'attente chez Meshy : la première image de la marche, figée.
        AnimatorState idle = CharacterSetup.AddState(sm, "Idle", walk, 0f);
        CharacterSetup.AddState(sm, "Walk", walk, 1f);
        CharacterSetup.AddState(sm, "Run", run, 1f);
        CharacterSetup.AddState(sm, "Attack", attack, 1f);
        sm.defaultState = idle;

        const string artPath = "Assets/Resources/DameArt.asset";
        var art = AssetDatabase.LoadAssetAtPath<DameArt>(artPath);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<DameArt>();
            AssetDatabase.CreateAsset(art, artPath);
        }
        art.model = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        art.controller = controller;
        art.scale = TargetHeight / CharacterSetup.MeasureHeight(art.model);
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssets();
        Debug.Log("La Dame installée (échelle " + art.scale.ToString("0.###") + ").");
    }
}
