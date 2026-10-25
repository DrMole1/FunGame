using System.Collections.Generic;
using UnityEngine;

public enum GameAction
{
    Forward, Back, Left, Right, Run, Sneak, Jump, Bark, Interact, Use,
    Slot1, Slot2, Slot3, Slot4, Slot5, Dance1, Dance2, Dance3,
}

// Touches du jeu, modifiables dans le menu pause ("Modifier Contrôles") et retenues d'une partie à l'autre.
// Sans réglage du joueur, les touches par défaut gardent leur comportement d'origine : Z ou W pour avancer, Q et A selon
// la disposition du clavier (KeyLayout), les deux Shift pour courir. Les flèches (déplacement) et le pavé numérique
// (cases 1 à 5) marchent toujours en plus.
public static class Controls
{
    const string Prefix = "ctrl_";
    public static event System.Action Changed;

    static readonly Dictionary<GameAction, KeyCode> custom = new Dictionary<GameAction, KeyCode>();
    static readonly Dictionary<GameAction, string> customLabel = new Dictionary<GameAction, string>();
    static bool loaded;

    public static readonly GameAction[] All = (GameAction[])System.Enum.GetValues(typeof(GameAction));

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        foreach (var a in All)
        {
            int code = PlayerPrefs.GetInt(Prefix + a, -1);
            if (code < 0) continue;
            custom[a] = (KeyCode)code;
            customLabel[a] = PlayerPrefs.GetString(Prefix + a + "_label", ((KeyCode)code).ToString());
        }
    }

    // Touches par défaut (la première donne le nom affiché, sauf Q et A, lues avec KeyLayout).
    static KeyCode[] Defaults(GameAction a)
    {
        switch (a)
        {
            case GameAction.Forward: return new[] { KeyCode.Z, KeyCode.W };
            case GameAction.Back: return new[] { KeyCode.S };
            case GameAction.Left: return new[] { KeyLayout.Left };
            case GameAction.Right: return new[] { KeyCode.D };
            case GameAction.Run: return new[] { KeyCode.LeftShift, KeyCode.RightShift };
            case GameAction.Sneak: return new[] { KeyCode.C };
            case GameAction.Jump: return new[] { KeyCode.Space };
            case GameAction.Bark: return new[] { KeyLayout.Bark };
            case GameAction.Interact: return new[] { KeyCode.E };
            case GameAction.Use: return new[] { KeyCode.Mouse0 };
            case GameAction.Slot1: return new[] { KeyCode.Alpha1 };
            case GameAction.Slot2: return new[] { KeyCode.Alpha2 };
            case GameAction.Slot3: return new[] { KeyCode.Alpha3 };
            case GameAction.Slot4: return new[] { KeyCode.Alpha4 };
            case GameAction.Slot5: return new[] { KeyCode.Alpha5 };
            case GameAction.Dance1: return new[] { KeyCode.I };
            case GameAction.Dance2: return new[] { KeyCode.O };
            default: return new[] { KeyCode.P };
        }
    }

    static string DefaultLabel(GameAction a)
    {
        switch (a)
        {
            case GameAction.Forward: return "Z";
            case GameAction.Left: return "Q";
            case GameAction.Bark: return "A";
            default: return KeyName(Defaults(a)[0]);
        }
    }

    // Touches toujours actives en plus de celle choisie.
    static KeyCode Extra(GameAction a)
    {
        switch (a)
        {
            case GameAction.Forward: return KeyCode.UpArrow;
            case GameAction.Back: return KeyCode.DownArrow;
            case GameAction.Left: return KeyCode.LeftArrow;
            case GameAction.Right: return KeyCode.RightArrow;
            case GameAction.Slot1: return KeyCode.Keypad1;
            case GameAction.Slot2: return KeyCode.Keypad2;
            case GameAction.Slot3: return KeyCode.Keypad3;
            case GameAction.Slot4: return KeyCode.Keypad4;
            case GameAction.Slot5: return KeyCode.Keypad5;
            default: return KeyCode.None;
        }
    }

    public static bool IsCustom(GameAction a)
    {
        Load();
        return custom.ContainsKey(a);
    }

    public static bool Held(GameAction a)
    {
        Load();
        KeyCode extra = Extra(a);
        if (extra != KeyCode.None && Input.GetKey(extra)) return true;
        if (custom.TryGetValue(a, out var key)) return Input.GetKey(key);
        foreach (var k in Defaults(a)) if (Input.GetKey(k)) return true;
        return false;
    }

    public static bool Down(GameAction a)
    {
        Load();
        KeyCode extra = Extra(a);
        if (extra != KeyCode.None && Input.GetKeyDown(extra)) return true;
        if (custom.TryGetValue(a, out var key)) return Input.GetKeyDown(key);
        foreach (var k in Defaults(a)) if (Input.GetKeyDown(k)) return true;
        return false;
    }

    // L'action se déclenche-t-elle par un bouton de la souris ? (Le clic qui reverrouille la souris ne compte pas.)
    public static bool IsMouse(GameAction a)
    {
        Load();
        KeyCode k = custom.TryGetValue(a, out var key) ? key : Defaults(a)[0];
        return k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6;
    }

    // Nom de la touche, dans la langue choisie.
    public static string Label(GameAction a)
    {
        Load();
        string label = customLabel.TryGetValue(a, out var l) ? l : DefaultLabel(a);
        // Une lettre ou un chiffre reste tel quel (sinon "O" deviendrait "W", comme le point cardinal de la boussole).
        return label.Length <= 1 ? label : Loc.T(label);
    }

    // Nouvelle touche pour l'action ; l'action qui l'avait déjà prend l'ancienne touche (échange).
    public static void Bind(GameAction a, KeyCode key, string label)
    {
        Load();
        KeyCode old = custom.TryGetValue(a, out var k) ? k : Defaults(a)[0];
        string oldLabel = custom.ContainsKey(a) ? customLabel[a] : DefaultLabel(a);
        foreach (var other in All)
        {
            if (other == a) continue;
            KeyCode current = custom.TryGetValue(other, out var c) ? c : Defaults(other)[0];
            bool sameDefault = !custom.ContainsKey(other) && System.Array.IndexOf(Defaults(other), key) >= 0;
            if (current == key || sameDefault) Save(other, old, oldLabel);
        }
        Save(a, key, label);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static void ResetAll()
    {
        Load();
        foreach (var a in All)
        {
            PlayerPrefs.DeleteKey(Prefix + a);
            PlayerPrefs.DeleteKey(Prefix + a + "_label");
        }
        custom.Clear();
        customLabel.Clear();
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    static void Save(GameAction a, KeyCode key, string label)
    {
        custom[a] = key;
        customLabel[a] = label;
        PlayerPrefs.SetInt(Prefix + a, (int)key);
        PlayerPrefs.SetString(Prefix + a + "_label", label);
    }

    // Action déclenchée cette image. Par un bouton de la souris, seulement quand la souris pilote la caméra (pas le clic
    // qui la reverrouille, ni un clic dans un menu).
    public static bool Pressed(GameAction a) =>
        Down(a) && (!IsMouse(a) || (OrbitCamera.Looking && Time.frameCount != OrbitCamera.LockFrame));

    // Nom lisible d'une touche (clé française pour celles qui se traduisent).
    public static string KeyName(KeyCode k)
    {
        if (k >= KeyCode.A && k <= KeyCode.Z) return k.ToString();
        if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
        if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "Num " + (int)(k - KeyCode.Keypad0);
        switch (k)
        {
            case KeyCode.Space: return "Espace";
            case KeyCode.Mouse0: return "Clic gauche";
            case KeyCode.Mouse1: return "Clic droit";
            case KeyCode.Mouse2: return "Clic molette";
            case KeyCode.Return: case KeyCode.KeypadEnter: return "Entrée";
            case KeyCode.LeftShift: return "Shift";
            case KeyCode.RightShift: return "Shift D";
            case KeyCode.LeftControl: return "Ctrl";
            case KeyCode.RightControl: return "Ctrl D";
            case KeyCode.LeftAlt: return "Alt";
            case KeyCode.RightAlt: return "Alt Gr";
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.Backspace: return "Retour";
            default: return k.ToString();
        }
    }
}
