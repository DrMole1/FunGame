using UnityEngine;

// Réglage "Éclairage" du menu pause : nombre de lampes calculées précisément par objet. Bas : 2 (le plus léger pour la carte
// graphique, de légères ruptures de lumière sur les murs), Standard : 4, Haut : 6 (le rendu d'origine, valeur par défaut).
public static class LightQuality
{
    const string Key = "lightQuality";
    static readonly int[] PixelLights = { 2, 4, 6 };
    static readonly string[] Names = { "Bas", "Standard", "Haut" };

    static int current = -1;

    // 0 : Bas, 1 : Standard, 2 : Haut.
    public static int Value
    {
        get
        {
            if (current < 0) current = Mathf.Clamp(PlayerPrefs.GetInt(Key, PixelLights.Length - 1), 0, PixelLights.Length - 1);
            return current;
        }
        set
        {
            current = Mathf.Clamp(value, 0, PixelLights.Length - 1);
            PlayerPrefs.SetInt(Key, current);
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
            Apply();
        }
    }

    public static string Label => Loc.T(Names[Value]);

    public static void Apply() => QualitySettings.pixelLightCount = PixelLights[Value];
}
