using UnityEngine;

// Luminosité hors du Hub, réglée dans le lobby et le menu pause (0 à 100 %, 50 % par défaut = l'éclairage de base) :
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

    // Éclairage de base du labyrinthe : +50 % par rapport aux valeurs d'origine.
    const float Base = 1.5f;

    // Multiplicateur de l'éclairage : 1,35 à 1,65 (base ±10 %).
    public static float Factor => Base * (1f + (Value - 0.5f) * 2f * Range);
}
