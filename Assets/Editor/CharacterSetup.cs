using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Prépare les personnages Meshy de Assets/Models/Characters/ (import Humanoid, textures, Animator)
// et remplace le cube de la scène par un joueur qui les utilise.
public static class CharacterSetup
{
    const string Root = "Assets/Models/Characters";
    const float TargetHeight = 1.7f;
    const float JumpStateSpeed = 0.5f;

    [MenuItem("FunGame/Installer les personnages")]
    public static void Setup()
    {
        string[] names = AssetDatabase.GetSubFolders(Root).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        var entries = names.Select(SetupCharacter).ToArray();
        SetupPlayer(entries);
        Debug.Log("Personnages installés : " + string.Join(", ", names));
    }

    static CharacterLibrary.Entry SetupCharacter(string name)
    {
        string dir = Root + "/" + name + "/";
        string riggedPath = dir + name + "_Rigged.fbx";

        // Modèle : avatar Humanoid généré à partir du squelette Meshy, textures extraites.
        var rigged = (ModelImporter)AssetImporter.GetAtPath(riggedPath);
        rigged.animationType = ModelImporterAnimationType.Human;
        rigged.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        rigged.importAnimation = false;
        string texDir = dir + "Textures";
        if (!AssetDatabase.IsValidFolder(texDir)) AssetDatabase.CreateFolder(Root + "/" + name, "Textures");
        rigged.ExtractTextures(texDir);
        AssetDatabase.Refresh();
        rigged.SaveAndReimport();

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(riggedPath).OfType<Avatar>().First();
        if (!avatar.isValid || !avatar.isHuman)
            Debug.LogError(name + " : avatar Humanoid invalide.");

        // Animations : avatar copié du modèle, mouvement de racine intégré à la pose sauf la hauteur du saut.
        AnimationClip walk = ImportClip(dir + name + "_Walk.fbx", "Walk", avatar, true, true);
        AnimationClip run = ImportClip(dir + name + "_Run.fbx", "Run", avatar, true, true);
        AnimationClip jump = ImportClip(dir + name + "_Jump.fbx", "Jump", avatar, false, false);

        string controllerPath = dir + name + ".controller";
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        // Pas de clip "idle" chez Meshy : la première image du saut est une pose debout.
        AnimatorState idle = AddState(sm, "Idle", jump, 0f);
        AddState(sm, "Walk", walk, 1f);
        AddState(sm, "Run", run, 1f);
        AddState(sm, "Jump", jump, JumpStateSpeed);
        sm.defaultState = idle;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(riggedPath);
        return new CharacterLibrary.Entry
        {
            name = name,
            model = model,
            controller = controller,
            scale = TargetHeight / MeasureHeight(model),
        };
    }

    internal static AnimationClip ImportClip(string path, string clipName, Avatar avatar, bool loop, bool bakeHeight)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = true;

        ModelImporterClipAnimation clip = importer.defaultClipAnimations[0];
        clip.name = clipName;
        clip.loopTime = loop;
        clip.loopPose = loop;
        clip.lockRootRotation = true;
        clip.keepOriginalOrientation = true;
        clip.lockRootPositionXZ = true;
        clip.keepOriginalPositionXZ = true;
        clip.lockRootHeightY = bakeHeight;
        clip.keepOriginalPositionY = true;
        importer.clipAnimations = new[] { clip };
        importer.SaveAndReimport();

        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => c.name == clipName);
    }

    internal static AnimatorState AddState(AnimatorStateMachine sm, string stateName, AnimationClip clip, float speed)
    {
        AnimatorState state = sm.AddState(stateName);
        state.motion = clip;
        state.speed = speed;
        state.writeDefaultValues = true;
        return state;
    }

    // Hauteur du mesh dans la pose de repos.
    internal static float MeasureHeight(GameObject model)
    {
        float height = 0f;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            height = Mathf.Max(height, smr.sharedMesh.bounds.size.y * smr.transform.lossyScale.y);
        return height > 0.01f ? height : TargetHeight;
    }

    static void SetupPlayer(CharacterLibrary.Entry[] entries)
    {
        var player = Object.FindObjectOfType<PlayerController>(true);
        if (player == null)
        {
            Debug.LogError("Aucun PlayerController dans la scène ouverte.");
            return;
        }
        GameObject go = player.gameObject;

        if (!go.TryGetComponent(out CharacterLibrary library)) library = go.AddComponent<CharacterLibrary>();
        library.characters = entries;
        if (!go.TryGetComponent(out CharacterAvatar _)) go.AddComponent<CharacterAvatar>();

        // Le cube devient une capsule invisible : le modèle est instancié au lancement de la partie.
        if (go.TryGetComponent(out BoxCollider box)) Object.DestroyImmediate(box);
        if (go.TryGetComponent(out MeshRenderer mr)) Object.DestroyImmediate(mr);
        if (go.TryGetComponent(out MeshFilter mf)) Object.DestroyImmediate(mf);
        CharacterAvatar.EnsureCapsule(go, library);
        Vector3 p = go.transform.position;
        go.transform.position = new Vector3(p.x, library.height * 0.5f, p.z);

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
    }
}
