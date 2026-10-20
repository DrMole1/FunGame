using UnityEngine;

// Luminosité hors du Hub, réglée dans le lobby et le menu pause (0 à 100 %, 50 % par défaut = les valeurs du jeu) :
// à 0 %, l'éclairage du labyrinthe (néons, lumière ambiante, lampe du personnage) baisse de 10 % et le brouillard
// s'épaissit d'autant ; à 100 %, l'inverse. Le Hub n'est pas concerné.
public static class Brightness
{
    const string Key = "brightness";
    const float Range = 0.1f;

    static float current = -1f;

    // 0 à 1.
    public static float Value
    {
        get
        {
            if (current < 0f) current = Mathf.Clamp01(PlayerPrefs.GetFloat(Key, 0.5f));
            return current;
        }
        set
        {
            current = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(Key, current);
        }
    }

    // Multiplicateur de l'éclairage : 0,9 à 1,1.
    public static float Factor => 1f + (Value - 0.5f) * 2f * Range;
}
