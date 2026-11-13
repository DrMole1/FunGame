using UnityEngine;

// Luminosité hors du Hub, réglée dans le lobby et le menu pause (0 à 100 %, 50 % par défaut = l'éclairage de base).
// V0.6.14 : courbe lissée sur toute la plage. Chaque cran de 10 % multiplie l'éclairage du labyrinthe (néons, lumière
// ambiante, lampe du personnage) par le même facteur, et le brouillard s'éclaircit d'autant : de très sombre à 0 % à très
// lumineux à 100 %, sans palier. Un changement de réglage s'applique en douceur (Tick). Le Hub n'est pas concerné.
public static class Brightness
{
    const string Key = "brightness";
    // Écart, en doublements de lumière, entre 50 % et chaque extrémité : 0 % = base / 2^1,5 (x0,35), 100 % = base x 2^1,5 (x2,8).
    const float Doublings = 1.5f;
    // Temps pour suivre un changement de réglage.
    const float SmoothTime = 0.35f;

    static float current = -1f;
    static float shown = -1f;

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
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
        }
    }

    // Éclairage de base du labyrinthe : +50 % par rapport aux valeurs d'origine.
    const float Base = 1.5f;

    static float Target(float value) => Base * Mathf.Pow(2f, (value - 0.5f) * 2f * Doublings);

    // Multiplicateur de l'éclairage appliqué (lissé) : environ 0,53 à 0 %, 1,5 à 50 %, 4,2 à 100 %.
    public static float Factor
    {
        get
        {
            if (shown < 0f) shown = Value;
            return Target(shown);
        }
    }

    // À chaque image : la valeur appliquée rejoint le réglage en douceur.
    public static void Tick(float deltaTime)
    {
        if (shown < 0f) shown = Value;
        shown = Mathf.MoveTowards(shown, Value, deltaTime / SmoothTime);
    }
}
