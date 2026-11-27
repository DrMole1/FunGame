using UnityEngine;

// V0.7.22 (Note 09) : images des tableaux des Châteaux (Assets/Sprite/Tableaux). L'asset Resources/TableauxArt les référence (elles
// restent à leur place dans le projet) ; il est tenu à jour par l'éditeur (TableauxArtSetup) dès qu'une image du dossier change.
[CreateAssetMenu(menuName = "FunGame/TableauxArt")]
public class TableauxArt : ScriptableObject
{
    public Texture2D[] images;

    static TableauxArt cached;
    static bool loaded;

    public static Texture2D[] Images
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                cached = Resources.Load<TableauxArt>("TableauxArt");
            }
            return cached != null && cached.images != null ? cached.images : System.Array.Empty<Texture2D>();
        }
    }
}
