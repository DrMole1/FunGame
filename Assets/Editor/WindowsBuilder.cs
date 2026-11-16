using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Build Windows (.exe 64 bits) dans le dossier "Build/" à la racine du projet (non versionné). Même scène et mêmes
// réglages que le build WebGL ; le réseau passe par NativePeer (PeerJS en C#), compatible avec la version navigateur.
// La cible active de l'éditeur (WebGL) est rétablie après le build.
public static class WindowsBuilder
{
    const string OutputDir = "Build";
    const string ExeName = "PavlovsTheory.exe";

    [MenuItem("FunGame/Build Windows (.exe)")]
    public static void Build()
    {
        BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;
        BuildTargetGroup previousGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
        PlayerSettings.runInBackground = true;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = true;

        // Passer d'abord en cible Windows : les scripts sont recompilés pour elle (le package Unity WebRTC, absent de WebGL,
        // doit figurer dans la liste des initialisations automatiques du build).
        if (previous != BuildTarget.StandaloneWindows64)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

        string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir));
        Directory.CreateDirectory(outPath);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = Path.Combine(outPath, ExeName),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log($"Build Windows réussi : {Path.Combine(outPath, ExeName)} ({report.summary.totalSize / (1024 * 1024)} Mo)");
        else
            Debug.LogError($"Build Windows échoué : {report.summary.result}, {report.summary.totalErrors} erreur(s)");

        if (EditorUserBuildSettings.activeBuildTarget != previous)
            EditorUserBuildSettings.SwitchActiveBuildTarget(previousGroup, previous);
    }
}
