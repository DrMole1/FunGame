using UnityEngine;

// V0.7.22 (Note 05 du Menu Pause) : réglage "Particules" du menu pause.
// V0.7.23 (Note 01) : quatre options, "Très Peu", "Peu", "Normal" (par défaut) et "Maximal". "Maximal" garde le nombre d'origine des
// particules de la traînée de parfum (l'ancien "Normal"), "Normal" le divise par 2 (l'ancien "Peu"), "Peu" par 4 (l'ancien "Très
// Peu"), "Très Peu" par 8. La poussière blanche en suspension (FX7) est divisée par 1,3 en "Peu" et par 1,6 en "Très Peu".
public static class ParticleDensity
{
    // Nouvelle clé : les indices de l'ancien réglage (3 options) n'ont plus le même sens.
    const string Key = "particleDensity4";
    static readonly float[] Factors = { 0.125f, 0.25f, 0.5f, 1f };
    static readonly float[] DustDividers = { 1.6f, 1.3f, 1f, 1f };
    static readonly string[] Names = { "Très Peu", "Peu", "Normal", "Maximal" };
    const int Default = 2;

    static int current = -1;

    // Change à chaque modification du réglage (la poussière en suspension se met à jour).
    public static event System.Action Changed;

    // 0 : Très Peu, 1 : Peu, 2 : Normal, 3 : Maximal.
    public static int Value
    {
        get
        {
            if (current < 0) current = Mathf.Clamp(PlayerPrefs.GetInt(Key, Default), 0, Factors.Length - 1);
            return current;
        }
        set
        {
            int next = Mathf.Clamp(value, 0, Factors.Length - 1);
            bool changed = next != Value;
            current = next;
            PlayerPrefs.SetInt(Key, current);
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
            if (changed) Changed?.Invoke();
        }
    }

    // Part du nombre d'origine des particules de la traînée de parfum à émettre.
    public static float Factor => Factors[Value];

    // Part du nombre d'origine des particules de poussière blanche en suspension.
    public static float DustFactor => 1f / DustDividers[Value];

    public static string Label => Loc.T(Names[Value]);
}
