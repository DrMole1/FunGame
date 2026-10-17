using UnityEngine;

// Police de tous les textes du jeu (UI et 3D). L'asset Resources/GameFonts est créé par "FunGame/Configurer le jeu"
// et pointe vers Assets/Font/PUSAB___.otf, ce qui l'inclut dans le build.
[CreateAssetMenu(menuName = "FunGame/Polices")]
public class GameFonts : ScriptableObject
{
    public Font main;
}
