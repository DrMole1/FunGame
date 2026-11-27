using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// V0.7.22 (Note 09) : tient à jour Resources/TableauxArt.asset avec toutes les images de Assets/Sprite/Tableaux (tableaux des Châteaux).
// Refait automatiquement dès qu'une image du dossier est ajoutée, modifiée, déplacée ou supprimée ; aussi par le menu.
public class TableauxArtSetup : AssetPostprocessor
{
    const string Folder = "Assets/Sprite/Tableaux";
    const string AssetPath = "Assets/Resources/TableauxArt.asset";

    [MenuItem("FunGame/Mettre à jour les tableaux des Châteaux")]
    public static void Refresh()
    {
        var images = new List<Texture2D>();
        if (AssetDatabase.IsValidFolder(Folder))
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            // Ordre stable (le tirage au hasard des tableaux doit être le même chez tous les joueurs).
            paths.Sort(System.StringComparer.Ordinal);
            foreach (string path in paths)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) images.Add(tex);
            }
        }
        var art = AssetDatabase.LoadAssetAtPath<TableauxArt>(AssetPath);
        if (art == null)
        {
            art = ScriptableObject.CreateInstance<TableauxArt>();
            AssetDatabase.CreateAsset(art, AssetPath);
        }
        art.images = images.ToArray();
        EditorUtility.SetDirty(art);
        AssetDatabase.SaveAssets();
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool Touches(string[] list)
        {
            foreach (string p in list) if (p.StartsWith(Folder + "/")) return true;
            return false;
        }
        if (Touches(imported) || Touches(deleted) || Touches(moved) || Touches(movedFrom)) EditorApplication.delayCall += Refresh;
    }
}
