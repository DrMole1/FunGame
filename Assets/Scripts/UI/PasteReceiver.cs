using System.Runtime.InteropServices;
using UnityEngine;

// Ctrl+V dans le navigateur : le texte collé est inséré dans la zone de saisie active (Terminal, Post-It), Unity WebGL
// n'ayant pas accès au presse-papiers (Paste.jslib).
public class PasteReceiver : MonoBehaviour
{
    const string ObjectName = "PasteReceiver";

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void FunGame_ListenPaste(string objectName);
    const bool Available = true;
#else
    static void FunGame_ListenPaste(string objectName) { }
    const bool Available = false;
#endif

    public static void Ensure()
    {
        if (!Available || GameObject.Find(ObjectName) != null) return;
        var go = new GameObject(ObjectName);
        DontDestroyOnLoad(go);
        go.AddComponent<PasteReceiver>();
        FunGame_ListenPaste(ObjectName);
    }

    void OnPaste(string text)
    {
        var selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
        var field = selected != null ? selected.GetComponent<UnityEngine.UI.InputField>() : null;
        if (field == null || !field.isFocused) return;
        text = text.Replace("\r", "").Replace("\n", " ");
        int a = Mathf.Min(field.selectionAnchorPosition, field.selectionFocusPosition);
        int b = Mathf.Max(field.selectionAnchorPosition, field.selectionFocusPosition);
        string current = field.text;
        a = Mathf.Clamp(a, 0, current.Length);
        b = Mathf.Clamp(b, a, current.Length);
        string result = current.Substring(0, a) + text + current.Substring(b);
        if (field.characterLimit > 0 && result.Length > field.characterLimit) result = result.Substring(0, field.characterLimit);
        field.text = result;
        field.caretPosition = field.selectionAnchorPosition = field.selectionFocusPosition = Mathf.Min(result.Length, a + text.Length);
    }
}
