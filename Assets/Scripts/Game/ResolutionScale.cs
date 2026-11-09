using UnityEngine;

// Réglage "Résolution" du menu pause : part de la résolution de l'écran utilisée pour dessiner le jeu (dans le navigateur). 100 % par
// défaut ; plus bas, l'image est moins nette mais la carte graphique travaille beaucoup moins.
public static class ResolutionScale
{
    const string Key = "resolutionScale";
    static readonly int[] Percents = { 50, 75, 100 };

    static int current = -1;

    // Indice dans Percents.
    public static int Value
    {
        get
        {
            if (current < 0) current = Mathf.Clamp(PlayerPrefs.GetInt(Key, Percents.Length - 1), 0, Percents.Length - 1);
            return current;
        }
        set
        {
            current = Mathf.Clamp(value, 0, Percents.Length - 1);
            PlayerPrefs.SetInt(Key, current);
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
            Apply();
        }
    }

    public static string Label => Percents[Value] + " %";

    public static void Apply() => PeerNet.SetRenderScale(Percents[Value] / 100f);
}
