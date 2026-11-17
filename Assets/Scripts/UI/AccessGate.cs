#if UNITY_WEBGL
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Version WebGL uniquement (GDD 15.1, V0.7.10) : à l'ouverture de l'onglet, un écran noir masque le menu du Lobby. Il affiche, en
// blanc, le nom du jeu, "Version Test" et les droits, au-dessus d'une zone de saisie : le bon mot de passe suivi de la touche
// Entrée fait disparaître l'écran. Mot de passe : "AUBERGINE" suivi du dernier numéro de la version (V0.7.10 -> AUBERGINE10).
// Ce fichier n'est compilé que pour WebGL : le code est absent du programme .exe. Dans l'éditeur, l'écran ne s'affiche pas (sauf
// appel à Show).
public class AccessGate : MonoBehaviour
{
    const string PasswordPrefix = "AUBERGINE";

    public static bool IsOpen { get; private set; }

    InputField input;
    Text feedback;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoShow()
    {
        if (!Application.isEditor) Show();
    }

    public static void Show()
    {
        if (IsOpen) return;
        new GameObject("AccessGate").AddComponent<AccessGate>().Build();
    }

    // "V0.7.10" -> "AUBERGINE10" ; "V0.8" -> "AUBERGINE8".
    public static string Password
    {
        get
        {
            string[] parts = GameRules.Version.TrimStart('V', 'v').Split('.');
            return PasswordPrefix + parts[parts.Length - 1];
        }
    }

    void Build()
    {
        IsOpen = true;
        DontDestroyOnLoad(gameObject);
        var canvas = UiKit.CreateCanvas(transform, "AccessGateCanvas", 1000);
        // Fond noir plein écran, qui arrête aussi les clics destinés au lobby.
        var back = UiKit.Panel(canvas.transform, "Fond", Vector2.zero, Color.black);
        UiKit.Stretch((RectTransform)back.transform);
        back.GetComponent<Image>().raycastTarget = true;

        var column = new GameObject("Contenu", typeof(RectTransform));
        column.transform.SetParent(back.transform, false);
        ((RectTransform)column.transform).sizeDelta = new Vector2(520, 300);
        UiKit.Place(column, new Vector2(0.5f, 0.5f), Vector2.zero);
        UiKit.Column(column, 0, 10);
        Fixed(UiKit.Label(column.transform, "", 44, FontStyle.Bold, Color.white, 56), "Pavlov's Theory");
        Fixed(UiKit.Label(column.transform, "", 22, FontStyle.Normal, Color.white, 30), "Version Test");
        Fixed(UiKit.Label(column.transform, "", 18, FontStyle.Normal, Color.white, 28), "Droits détenus par Bastien Prob");
        UiKit.Spacer(column.transform, 14);
        input = UiKit.Field(column.transform, "", "", 32);
        input.contentType = InputField.ContentType.Password;
        input.onEndEdit.AddListener(OnEndEdit);
        feedback = UiKit.Label(column.transform, "", 16, FontStyle.Italic, new Color(1f, 1f, 1f, 0.6f), 24);
        Focus();
    }

    static void Fixed(Text t, string text) => t.text = text;

    // Le menu du lobby, construit derrière, ne doit pas prendre la saisie au clavier.
    void Update()
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != input.gameObject) Focus();
    }

    void Focus()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(input.gameObject);
        input.ActivateInputField();
    }

    void OnEndEdit(string text)
    {
        if (!(Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))) return;
        if (string.Equals(text.Trim(), Password, System.StringComparison.OrdinalIgnoreCase))
        {
            IsOpen = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Destroy(gameObject);
            return;
        }
        input.text = "";
        feedback.text = "Mot de passe incorrect";
        Focus();
    }
}
#endif
