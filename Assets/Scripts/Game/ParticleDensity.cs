using UnityEngine;

// V0.7.22 (Note 05 du Menu Pause) : réglage "Particules" : "Très Peu", "Peu" ou "Normal" (par défaut). "Normal" garde le nombre
// d'origine des particules de la traînée de parfum, "Peu" le divise par 2, "Très Peu" par 4.
public static class ParticleDensity
{
    const string Key = "particleDensity";
    static readonly float[] Factors = { 0.25f, 0.5f, 1f };
    static readonly string[] Names = { "Très Peu", "Peu", "Normal" };

    static int current = -1;

    // 0 : Très Peu, 1 : Peu, 2 : Normal.
    public static int Value
    {
        get
        {
            if (current < 0) current = Mathf.Clamp(PlayerPrefs.GetInt(Key, Factors.Length - 1), 0, Factors.Length - 1);
            return current;
        }
        set
        {
            current = Mathf.Clamp(value, 0, Factors.Length - 1);
            PlayerPrefs.SetInt(Key, current);
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
        }
    }

    // Part du nombre d'origine des particules à émettre.
    public static float Factor => Factors[Value];

    public static string Label => Loc.T(Names[Value]);
}
