using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Build WebGL prêt pour GitHub Pages, dans le dossier "docs/" à la racine du projet.
public static class WebGLBuilder
{
    const string OutputDir = "docs";

    [MenuItem("FunGame/Build WebGL pour GitHub Pages")]
    public static void Build()
    {
        // GitHub Pages ne sert pas les en-têtes Content-Encoding : le build est compressé et le navigateur le décompresse lui-même
        // (téléchargement bien plus léger, au prix de quelques secondes de décompression au premier lancement).
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.template = "PROJECT:FunGame";
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;

        string[] scenes = { "Assets/Scenes/SampleScene.unity" };
        string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir));
        // Les fichiers du build précédent ne portent pas le même nom une fois compressés : ils ne doivent pas rester en ligne.
        string buildDir = Path.Combine(outPath, "Build");
        if (Directory.Exists(buildDir)) foreach (string file in Directory.GetFiles(buildDir)) File.Delete(file);

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });

        if (report.summary.result == BuildResult.Succeeded)
        {
            File.WriteAllText(Path.Combine(outPath, ".nojekyll"), "");
            Debug.Log($"Build WebGL réussi : {outPath} ({report.summary.totalSize / (1024 * 1024)} Mo)");
        }
        else
        {
            Debug.LogError($"Build WebGL échoué : {report.summary.result}, {report.summary.totalErrors} erreur(s)");
        }
    }
}
