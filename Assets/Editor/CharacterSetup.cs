using System.Collections.Generic;
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
    // Marche discrète (Meshy "Sneaky Walk", sur place) : un seul clip, avec son propre avatar Humanoid,
    // retransposé sur tous les personnages.
    const string SneakPath = "Assets/Models/Animations/SneakyWalk.fbx";
    // Le clip (2 pas en 2,83 s) est accéléré pour suivre la vitesse de la marche discrète.
    const float SneakStateSpeed = 1.5f;
    // Danse sur un Téléporteur ou une Station Curative : celle du Fou (Humanoid, partagée sans la réimporter).
    const string DancePath = "Assets/Models/Fou/Fou_Dance.fbx";
    // V0.4.6 : danses 2 et 3 (touches O et P) et "Creuser", animations Meshy partagées par tous les personnages.
    const string ExtrasPath = "Assets/Models/Animations/PlayerExtras.fbx";
    static readonly (string take, string state, bool loop)[] Extras =
        { ("FunnyDancing_02", "Dance2", true), ("Hip_Hop_Dance", "Dance3", true), ("Pull_Radish", "Dig", true) };
    // Idles Meshy partagés par tous les personnages, entre lesquels CharacterAvatar alterne en fondu tant que le
    // personnage ne fait rien d'autre : Idle ("Idle 3", attente calme), IdleB ("Idle" : il se tourne et regarde
    // autour de lui) et IdleC ("Idle 1", nonchalant).
    const string IdlesPath = "Assets/Models/Animations/PlayerIdles.fbx";
    static readonly (string take, string state, bool loop)[] Idles =
        { ("Idle_3", "Idle", true), ("Idle", "IdleB", true), ("Idle_02", "IdleC", true) };
    // Le clip "Pull Radish" dure 4,7 s : accéléré pour tenir dans les 1,2 seconde du creusage.
    const float DigStateSpeed = 3.92f;
    // Morts (Meshy "Electrocuted Fall", "Strangled and Fall Forward", "Dying Backwards") : états Death0, Death1, Death2,
    // l'un d'eux tiré au hasard par CharacterAvatar. La chute est intégrée à la pose (sans mouvement de racine).
    static readonly string[] DeathPaths =
    {
        "Assets/Models/Animations/Death_Electrocuted.fbx",
        "Assets/Models/Animations/Death_Strangled.fbx",
        "Assets/Models/Animations/Death_Backwards.fbx",
    };

    [MenuItem("FunGame/Installer les personnages")]
    public static void Setup()
    {
        string[] names = AssetDatabase.GetSubFolders(Root).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        AnimationClip sneak = ImportClip(SneakPath, "Sneak", null, true, true);
        AnimationClip[] deaths = ImportDeaths();
        var entries = names.Select(n => SetupCharacter(n, sneak, deaths)).ToArray();
        SetupPlayer(entries);
        Debug.Log("Personnages installés : " + string.Join(", ", names));
    }

    // Installe seulement les personnages absents de la liste du joueur (V0.4.8 : Perso 5 à 8) et les ajoute à la fin,
    // sans réimporter les autres (Perso3 a ses propres correctifs).
    [MenuItem("FunGame/Ajouter les nouveaux personnages")]
    public static void SetupNew()
    {
        var player = Object.FindObjectOfType<PlayerController>(true);
        var library = player != null ? player.GetComponent<CharacterLibrary>() : null;
        if (library == null) { Debug.LogError("Aucun joueur avec CharacterLibrary dans la scène ouverte."); return; }
        var known = new HashSet<string>(library.characters.Select(c => c.name));
        string[] names = AssetDatabase.GetSubFolders(Root).Select(Path.GetFileName)
            .Where(n => !known.Contains(n)).OrderBy(n => n).ToArray();
        if (names.Length == 0) { Debug.Log("Aucun nouveau personnage."); return; }
        AnimationClip sneak = AssetDatabase.LoadAllAssetsAtPath(SneakPath).OfType<AnimationClip>().First(c => c.name == "Sneak");
        AnimationClip[] deaths = DeathPaths.Select((path, i) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => c.name == "Death" + i)).ToArray();
        library.characters = library.characters.Concat(names.Select(n => SetupCharacter(n, sneak, deaths))).ToArray();
        EditorUtility.SetDirty(library);
        EditorSceneManager.MarkSceneDirty(library.gameObject.scene);
        EditorSceneManager.SaveScene(library.gameObject.scene);
        Debug.Log("Personnages ajoutés : " + string.Join(", ", names));
    }

    static AnimationClip[] ImportDeaths() =>
        DeathPaths.Select((path, i) => ImportClip(path, "Death" + i, null, false, true)).ToArray();

    // Ajoute les morts aux Animators existants, sans réinstaller les personnages ni toucher à la scène.
    [MenuItem("FunGame/Ajouter les animations de mort")]
    public static void AddDeaths()
    {
        AnimationClip[] deaths = ImportDeaths();
        foreach (string name in AssetDatabase.GetSubFolders(Root).Select(Path.GetFileName))
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/" + name + "/" + name + ".controller");
            if (controller == null) continue;
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (var child in sm.states.Where(c => c.state.name.StartsWith("Death")).ToArray()) sm.RemoveState(child.state);
            AddDeathStates(sm, deaths);
            EditorUtility.SetDirty(controller);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Animations de mort ajoutées aux personnages.");
    }

    static void AddDanceState(AnimatorStateMachine sm)
    {
        AnimationClip dance = AssetDatabase.LoadAllAssetsAtPath(DancePath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (dance == null) { Debug.LogError("Danse introuvable : " + DancePath); return; }
        AddState(sm, "Dance", dance, 1f);
        foreach (var (take, state, _) in Extras)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ExtrasPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == state);
            if (clip == null) { Debug.LogError("Animation introuvable : " + state + " (" + ExtrasPath + ")"); continue; }
            AddState(sm, state, clip, state == "Dig" ? DigStateSpeed : 1f);
        }
    }

    static void ImportExtras() => ImportShared(ExtrasPath, Extras);

    // Clips d'un fichier Meshy partagé, en Humanoid (avatar propre au fichier), le mouvement de racine intégré à la pose.
    static void ImportShared(string path, (string take, string state, bool loop)[] table)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = true;
        var clips = new List<ModelImporterClipAnimation>();
        foreach (var c in importer.defaultClipAnimations)
        {
            int k = System.Array.FindIndex(table, e => e.take == c.takeName);
            if (k < 0) continue;
            c.name = table[k].state;
            c.loopTime = c.loopPose = table[k].loop;
            c.lockRootRotation = c.keepOriginalOrientation = true;
            c.lockRootPositionXZ = c.keepOriginalPositionXZ = true;
            c.lockRootHeightY = c.keepOriginalPositionY = true;
            clips.Add(c);
        }
        importer.clipAnimations = clips.ToArray();
        importer.SaveAndReimport();
    }

    // Ajoute la danse aux Animators existants, sans réinstaller les personnages ni toucher à la scène.
    [MenuItem("FunGame/Ajouter la danse aux personnages")]
    public static void AddDance()
    {
        ImportExtras();
        foreach (string name in AssetDatabase.GetSubFolders(Root).Select(Path.GetFileName))
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/" + name + "/" + name + ".controller");
            if (controller == null) continue;
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (var child in sm.states.Where(c => c.state.name.StartsWith("Dance") || c.state.name == "Dig").ToArray()) sm.RemoveState(child.state);
            AddDanceState(sm);
            EditorUtility.SetDirty(controller);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Danse ajoutée aux personnages.");
    }

    // États d'attente : Idle (état par défaut), IdleB et IdleC. Sans le fichier des idles : la première image du saut,
    // figée (pose debout).
    static AnimatorState AddIdleStates(AnimatorStateMachine sm, AnimationClip jump)
    {
        ImportShared(IdlesPath, Idles);
        AnimatorState idle = null;
        foreach (var (_, state, _) in Idles)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(IdlesPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == state);
            if (clip == null) { Debug.LogError("Animation introuvable : " + state + " (" + IdlesPath + ")"); continue; }
            AnimatorState added = AddState(sm, state, clip, 1f);
            if (state == "Idle") idle = added;
        }
        return idle ?? AddState(sm, "Idle", jump, 0f);
    }

    // Remplace l'attente figée des Animators existants par les idles animés, sans réinstaller les personnages.
    [MenuItem("FunGame/Ajouter les idles aux personnages")]
    public static void AddIdles()
    {
        foreach (string name in AssetDatabase.GetSubFolders(Root).Select(Path.GetFileName))
        {
            string dir = Root + "/" + name + "/";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(dir + name + ".controller");
            if (controller == null) continue;
            AnimationClip jump = AssetDatabase.LoadAllAssetsAtPath(dir + name + "_Jump.fbx").OfType<AnimationClip>().FirstOrDefault(c => c.name == "Jump");
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (var child in sm.states.Where(c => c.state.name.StartsWith("Idle")).ToArray()) sm.RemoveState(child.state);
            sm.defaultState = AddIdleStates(sm, jump);
            EditorUtility.SetDirty(controller);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Idles ajoutés aux personnages.");
    }

    static void AddDeathStates(AnimatorStateMachine sm, AnimationClip[] deaths)
    {
        for (int i = 0; i < deaths.Length; i++) AddState(sm, "Death" + i, deaths[i], 1f);
    }

    static CharacterLibrary.Entry SetupCharacter(string name, AnimationClip sneak, AnimationClip[] deaths)
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
        AnimatorState idle = AddIdleStates(sm, jump);
        AddState(sm, "Walk", walk, 1f);
        AddState(sm, "Run", run, 1f);
        AddState(sm, "Jump", jump, JumpStateSpeed);
        AddState(sm, "Sneak", sneak, SneakStateSpeed);
        // Arrêt en marche discrète : même clip, figé (CharacterAvatar choisit l'image).
        AddState(sm, "SneakIdle", sneak, 0f);
        AddDeathStates(sm, deaths);
        ImportExtras();
        AddDanceState(sm);
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

    // 'avatar' nul : le fichier crée son propre avatar (clip partagé entre plusieurs personnages).
    internal static AnimationClip ImportClip(string path, string clipName, Avatar avatar, bool loop, bool bakeHeight)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        if (avatar != null)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
        }
        else importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
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
