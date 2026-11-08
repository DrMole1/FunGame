using UnityEngine;

// Police de tous les textes du jeu (UI et 3D). L'asset Resources/GameFonts est créé par "FunGame/Configurer le jeu"
// et pointe vers la police choisie dans GameSetup (FontPath), ce qui l'inclut dans le build.
[CreateAssetMenu(menuName = "FunGame/Polices")]
public class GameFonts : ScriptableObject
{
    public Font main;
    // Note 02 du chapitre 6.1 (V0.6.1) : titre "Pavlov's Theory" en police de style soviétique (Ruslan Display, licence OFL).
    public Font title;
    // Patte de chien qui remplace le "o" de "Theory" (Assets/Sprite/DogIcon).
    public Sprite titleIcon;
}
