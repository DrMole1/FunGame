using UnityEngine;

// Touches « aller à gauche » (Q) et « aboyer » (A), quelle que soit la disposition du clavier.
// Dans le navigateur, les KeyCode donnent la position physique des touches sur un clavier QWERTY :
// en AZERTY, la touche A arrive comme KeyCode.Q et inversement. On s'en tient donc au caractère réellement tapé
// (Input.inputString) pour savoir quelle touche porte quelle lettre.
public static class KeyLayout
{
#if UNITY_WEBGL && !UNITY_EDITOR
    // Par défaut dans le navigateur : clavier AZERTY (positions physiques QWERTY).
    static KeyCode left = KeyCode.A, bark = KeyCode.Q;
#else
    static KeyCode left = KeyCode.Q, bark = KeyCode.A;
#endif
    static int learnedFrame = -1;

    public static KeyCode Left { get { Learn(); return left; } }
    public static KeyCode Bark { get { Learn(); return bark; } }

    // Dès qu'une des deux touches est enfoncée, la lettre tapée indique son rôle.
    static void Learn()
    {
        if (learnedFrame == Time.frameCount) return;
        learnedFrame = Time.frameCount;
        bool q = Input.GetKeyDown(KeyCode.Q), a = Input.GetKeyDown(KeyCode.A);
        if (q == a) return;
        string typed = Input.inputString.ToLowerInvariant();
        KeyCode pressed = q ? KeyCode.Q : KeyCode.A, other = q ? KeyCode.A : KeyCode.Q;
        if (typed.Contains("q")) { left = pressed; bark = other; }
        else if (typed.Contains("a")) { bark = pressed; left = other; }
    }
}
