using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Saisie du message d'un Post-It (20 caractères maximum), sur le client du joueur qui l'accroche.
// Entrée : valider ; Échap : annuler (le Post-It reste dans l'inventaire).
public class PostItUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    System.Action<string> onWritten;
    GameObject panel;
    InputField input;

    public void Init(System.Action<string> written)
    {
        onWritten = written;
        var canvas = UiKit.CreateCanvas(transform, "PostItCanvas", 85);
        panel = UiKit.Panel(canvas.transform, "PostIt", new Vector2(420, 250), new Color(1f, 0.9f, 0.3f, 0.97f));
        UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero);
        UiKit.Column(panel, 22, 12);
        UiKit.Label(panel.transform, "Post-It", 26, FontStyle.Bold, new Color(0.15f, 0.12f, 0.05f), 34);
        UiKit.Label(panel.transform, "Écrivez votre message (" + Items.PostItMaxChars + " caractères max).\nIl ne pourra plus être modifié.",
            15, FontStyle.Normal, new Color(0.2f, 0.17f, 0.08f), 44);
        input = UiKit.Field(panel.transform, "Votre message…", "", Items.PostItMaxChars);
        input.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.55f);
        input.textComponent.color = new Color(0.12f, 0.1f, 0.25f);
        input.placeholder.color = new Color(0.12f, 0.1f, 0.25f, 0.45f);
        input.caretColor = Color.black;
        input.onEndEdit.AddListener(OnEndEdit);
        UiKit.Label(panel.transform, "Entrée : accrocher · Échap : annuler", 14, FontStyle.Italic, new Color(0.25f, 0.2f, 0.1f), 22);
        panel.SetActive(false);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
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
