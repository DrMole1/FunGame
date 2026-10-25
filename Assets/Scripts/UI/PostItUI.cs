using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Saisie du message d'un Post-It (20 caractères maximum), sur le client du joueur qui l'accroche.
// Entrée : valider ; Échap : annuler (le Post-It reste dans l'inventaire).
// La même fenêtre sert à saisir le lien YouTube d'une Télévision (V0.4.7, "semblable à l'interface du Post-It").
public class PostItUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    const string PostItHint = "Écrivez votre message ({0} caractères max).\nIl ne pourra plus être modifié.";

    System.Action<string> onPostIt, onWritten;
    GameObject panel;
    Text title, hint, footer;
    InputField input;

    public void Init(System.Action<string> written)
    {
        onPostIt = written;
        var canvas = UiKit.CreateCanvas(transform, "PostItCanvas", 85);
        panel = UiKit.Panel(canvas.transform, "PostIt", new Vector2(420, 250), new Color(1f, 0.9f, 0.3f, 0.97f));
        UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero);
        UiKit.Column(panel, 22, 12);
        title = UiKit.Label(panel.transform, "Post-It", 26, FontStyle.Bold, new Color(0.15f, 0.12f, 0.05f), 34);
        // Texte d'aide rempli à l'ouverture (déjà traduit) : pas de traduction automatique sur ce libellé.
        hint = UiKit.Label(panel.transform, "", 15, FontStyle.Normal, new Color(0.2f, 0.17f, 0.08f), 44);
        input = UiKit.Field(panel.transform, "Votre message…", "", Items.PostItMaxChars);
        input.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.55f);
        input.textComponent.color = new Color(0.12f, 0.1f, 0.25f);
        input.placeholder.color = new Color(0.12f, 0.1f, 0.25f, 0.45f);
        input.caretColor = Color.black;
        input.onEndEdit.AddListener(OnEndEdit);
        footer = UiKit.Label(panel.transform, "Entrée : accrocher · Échap : annuler", 14, FontStyle.Italic, new Color(0.25f, 0.2f, 0.1f), 22);
        panel.SetActive(false);
    }

    public void Open() => Open("Post-It", Loc.F(PostItHint, Items.PostItMaxChars), "Votre message…", "Entrée : accrocher · Échap : annuler",
        Items.PostItMaxChars, onPostIt);

    // heading, placeholder et keys : textes français (traduits ici) ; help : déjà traduit.
    public void Open(string heading, string help, string placeholder, string keys, int maxChars, System.Action<string> written)
    {
        if (IsOpen) return;
        IsOpen = true;
        onWritten = written;
        Loc.Bind(title, heading);
        hint.text = help;
        Loc.Bind(footer, keys);
        Loc.Bind((Text)input.placeholder, placeholder);
        input.characterLimit = maxChars;
        input.text = "";
        panel.SetActive(true);
        OrbitCamera.LockCursor(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(input.gameObject);
        input.ActivateInputField();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        panel.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            OrbitCamera.ResumeGame();
        }
    }

    void OnEndEdit(string text)
    {
        if (!IsOpen || !(Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))) return;
        if (text.Trim().Length == 0)
        {
            input.ActivateInputField();
            return;
        }
        Close();
        onWritten?.Invoke(text);
    }
}
