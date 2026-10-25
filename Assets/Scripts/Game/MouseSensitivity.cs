using UnityEngine;

// Sensibilité de la souris pour la caméra (Note 05 du Menu Pause, V0.5), réglée dans le menu pause sous la luminosité :
// 10 % à 200 %, 100 % par défaut (la sensibilité d'origine).
public static class MouseSensitivity
{
    const string Key = "mouseSensitivity";
    const float Min = 0.1f, Max = 2f;

    static float current = -1f;

    // Multiplicateur de la sensibilité d'origine.
    public static float Value
    {
        get
        {
            if (current < 0f) current = Mathf.Clamp(PlayerPrefs.GetFloat(Key, 1f), Min, Max);
            return current;
        }
        set
        {
            current = Mathf.Clamp(value, Min, Max);
            PlayerPrefs.SetFloat(Key, current);
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
        }
    }
}
