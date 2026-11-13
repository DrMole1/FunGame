using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Zone de saisie du Terminal, affichée uniquement chez le joueur qui l'utilise.
// V0.6.10 (légère refonte) : le message d'accueil s'écrit caractère par caractère, puis la liste des objets achetables
// (ID et prix) s'affiche d'un coup ; taper un ID achète l'objet. Plus de /buy, /info, /hello, /step1, /skip round ni /sell.
// Restent les commandes secrètes de l'hôte, insensibles à la casse : /kick, /spawn, /tp, /godmod, /give,
// /timer et /open (V0.6.13).
// Tous les messages, hormis les commandes, sont traduits (Note 03).
public class TerminalUI : MonoBehaviour
{
    static string Welcome => Loc.T("Bienvenue sur le Terminal !\nQue voulez-vous acheter ?");
    static string Quit => "\n" + Loc.T("Faire ECHAP pour quitter.");
    const int MaxLines = 18;
    const float TypeSpeed = 90f; // caractères par seconde

    public static bool IsOpen { get; private set; }

    GameSession session;
    GameObject panel;
    Text output;
    InputField input;
    readonly List<string> lines = new List<string>();
    bool waitingBuy;
    // Texte en cours d'écriture (message d'accueil), nombre de caractères déjà affichés, et ce qui s'affiche d'un coup ensuite.
    string typing = "", afterTyping = "";
    float typed;

    public void Init(GameSession owner)
    {
        session = owner;
        var canvas = UiKit.CreateCanvas(transform, "TerminalCanvas", 80);
        panel = UiKit.Panel(canvas.transform, "Terminal", new Vector2(760, 470), new Color(0.01f, 0.03f, 0.01f, 0.96f));
        UiKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero);
        panel.AddComponent<Outline>().effectColor = new Color(0.2f, 0.9f, 0.3f, 0.6f);

        output = UiKit.Label(panel.transform, "", 18, FontStyle.Normal, new Color(0.45f, 1f, 0.5f), 0, TextAnchor.LowerLeft);
        var ort = output.rectTransform;
        ort.anchorMin = new Vector2(0f, 0f);
        ort.anchorMax = new Vector2(1f, 1f);
        ort.offsetMin = new Vector2(22, 74);
        ort.offsetMax = new Vector2(-22, -18);
        output.verticalOverflow = VerticalWrapMode.Truncate;
        output.supportRichText = false;

        input = UiKit.Field(panel.transform, "", "", 40);
        var irt = (RectTransform)input.transform;
        irt.anchorMin = new Vector2(0f, 0f);
        irt.anchorMax = new Vector2(1f, 0f);
        irt.pivot = new Vector2(0.5f, 0f);
        irt.offsetMin = new Vector2(16, 14);
        irt.offsetMax = new Vector2(-16, 64);
        input.textComponent.color = new Color(0.6f, 1f, 0.6f);
        input.GetComponent<Image>().color = new Color(0.1f, 0.25f, 0.1f, 0.6f);
        input.onEndEdit.AddListener(OnEndEdit);
        input.onValueChanged.AddListener(text => { if (IsOpen && text.Length > 0) GameAudio.Play2D("key", 0.3f, 0.2f); });

        // V0.6.13 (FX6) : façon vieux moniteur (lignes de balayage, bords bombés, image fantôme du texte).
        var crt = new GameObject("VieuxMoniteur", typeof(RectTransform), typeof(RawImage));
        crt.transform.SetParent(panel.transform, false);
        var crtRt = (RectTransform)crt.transform;
        crtRt.anchorMin = Vector2.zero;
        crtRt.anchorMax = Vector2.one;
        crtRt.offsetMin = crtRt.offsetMax = Vector2.zero;
        crtOverlay = crt.GetComponent<RawImage>();
        crtOverlay.texture = PostFx.CrtTexture();
        crtOverlay.raycastTarget = false;
        crtGhost = output.gameObject.AddComponent<Shadow>();
        crtGhost.effectColor = new Color(0.45f, 1f, 0.5f, 0.22f);
        crtGhost.effectDistance = new Vector2(3f, -2f);

        panel.SetActive(false);
    }

    RawImage crtOverlay;
    Shadow crtGhost;

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        waitingBuy = false;
        typing = afterTyping = "";
        lines.Clear();
        output.text = "";
        Type(Welcome, ItemList());
        panel.SetActive(true);
        OrbitCamera.LockCursor(false);
        Focus();
    }

    public void Close(bool notify = true)
    {
        if (!IsOpen) return;
        IsOpen = false;
        panel.SetActive(false);
        input.text = "";
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (notify) session.ReleaseTerminal();
    }

    void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            OrbitCamera.ResumeGame();
            return;
        }
        if (!IsOpen || typing.Length == 0) return;
        int before = Mathf.FloorToInt(typed);
        typed += TypeSpeed * Time.unscaledDeltaTime;
        int shown = Mathf.Min(typing.Length, Mathf.FloorToInt(typed));
        // Un petit clic de temps en temps pendant que le texte s'écrit.
        if (shown / 6 != before / 6) GameAudio.Play2D("key", 0.15f, 0.2f, 1.4f);
        if (shown >= typing.Length) FlushTyping();
        else Show(typing.Substring(0, shown));
    }

    // Écrit 'text' caractère par caractère, puis affiche 'then' d'un coup.
    void Type(string text, string then)
    {
        FlushTyping();
        typing = text;
        afterTyping = then;
        typed = 0f;
    }

    void FlushTyping()
    {
        if (typing.Length == 0) return;
        string done = typing, then = afterTyping;
        typing = afterTyping = "";
        Print(done);
        if (then.Length > 0) Print(then);
    }

    // Objets achetables, avec leur ID et leur prix. La Boussole n'y est plus une fois achetée, ni le Téléporteur Inversé
    // une fois présent dans le Hub de ce Terminal.
    string ItemList()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var def in Items.Buyable())
        {
            if (!session.ForSale(def)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("  ID ").Append(def.terminalId).Append(" - ").Append(def.name).Append(" : ").Append(Items.Price(def.buyPrice));
        }
        return sb.ToString();
    }

    void Focus()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(input.gameObject);
        input.ActivateInputField();
    }

    void OnEndEdit(string text)
    {
        if (!IsOpen) return;
        if (!(Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))) return;
        input.text = "";
        Focus();
        text = text.Trim();
        if (text.Length == 0) return;
        GameAudio.Play2D("beep", 0.35f);
        Print("> " + text);
        Execute(text);
    }

    // Un ID achète l'objet ; une commande (commençant par "/") est une commande secrète de l'hôte.
    void Execute(string text)
    {
        string lower = text.ToLowerInvariant();
        if (!lower.StartsWith("/"))
        {
            Buy(lower);
            return;
        }
        if (lower.StartsWith("/give ") && session.IsHost)
        {
            string result = session.Give(text.Substring(6).Trim());
            Print((result ?? Loc.T("Commande invalide.")) + Quit);
        }
        else if (lower.StartsWith("/tp ") && session.IsHost)
        {
            string result = session.Teleport(text.Substring(4).Trim());
            if (result == null) Error(Loc.T("Commande invalide.") + Quit);
            else
            {
                GameAudio.Play2D("beep", 0.35f, 0f, 1.3f);
                Close();
                session.Toast(result);
            }
        }
        else if (lower.StartsWith("/kick ") && session.IsHost)
        {
            string target = text.Substring(6).Trim();
            string kicked = session.Kick(target);
            Print(kicked != null ? Loc.F("Le sujet {0} a été expulsé de la partie.", kicked) + Quit
                                 : Loc.F("Aucun autre sujet nommé {0}.", target) + Quit);
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"^/godmod\s+(on|off)$") && session.IsHost)
        {
            Print(session.SetGodmode(lower.EndsWith("on")) + Quit);
        }
        else if (lower == "/timer" && session.IsHost)
        {
            Print(session.ToggleTimer() + Quit);
        }
        else if (lower.StartsWith("/open ") && session.IsHost)
        {
            string result = int.TryParse(text.Substring(6).Trim(), out int zone) ? session.OpenZoneDoors(zone) : null;
            if (result == null) Error(Loc.T("Commande invalide.") + Quit);
            else Print(result + Quit);
        }
        else if (lower.StartsWith("/spawn ") && session.IsHost)
        {
            string arg = text.Substring(7).Trim();
            if (int.TryParse(arg, out int id)) Print(session.SpawnEntity(id) + Quit);
            else Error(Loc.T("Commande invalide.") + Quit);
        }
        else
        {
            Error(Loc.T("Commande Incorrecte.") + Quit);
        }
    }

    void Buy(string arg)
    {
        ItemDef def = int.TryParse(arg, out int id) ? Items.ByTerminalId(id) : null;
        if (def == null || def.buyPrice <= 0 || !session.ForSale(def))
        {
            Error(Loc.T("Commande invalide.") + Quit);
            return;
        }
        if (waitingBuy) return;
        if (def.inventory && !session.HasFreeSlot)
        {
            Error(Loc.F("Votre inventaire est plein ! Impossible d'acheter {0}.", def.name) + Quit);
            return;
        }
        if (session.Cash < def.buyPrice && !session.Godmode)
        {
            Error(Loc.F("Gamelle insuffisante pour acheter {0} ({1}).", def.name, Items.Price(def.buyPrice)) + Quit);
            return;
        }
        waitingBuy = true;
        session.Buy(def);
    }

    // Réponse de l'hôte à une demande d'achat.
    public void OnBuyResult(bool ok, ItemDef def, int cash, bool alreadyOwned)
    {
        waitingBuy = false;
        if (!IsOpen || def == null) return;
        if (ok && def.type == ItemType.Boussole)
            Print(Loc.F("Vous avez acheté la Boussole pour {0} !\nElle s'affiche désormais en haut de l'écran de tous les sujets.", Items.Price(session.Godmode ? 0 : def.buyPrice)) + Quit);
        else if (ok && def.type == ItemType.TeleporteurInverse)
            Print(Loc.F("Vous avez acheté le Téléporteur Inversé pour {0} !\nIl est apparu au centre du Hub.", Items.Price(session.Godmode ? 0 : def.buyPrice)) + Quit);
        else if (ok)
            Print(Loc.F("Vous avez acheté {0} pour {1} !", def.name, Items.Price(session.Godmode ? 0 : def.buyPrice)) + Quit);
        else if (alreadyOwned)
            Print((def.type == ItemType.TeleporteurInverse ? Loc.T("Ce Hub possède déjà un Téléporteur Inversé.") : Loc.T("La Boussole a déjà été achetée.")) + Quit);
        else
            Print(Loc.F("Gamelle insuffisante pour acheter {0} ({1}).", def.name, Items.Price(def.buyPrice)) + Quit);
    }

    void Error(string text)
    {
        GameAudio.Play2D("error", 0.35f);
        Print(text);
    }

    // Le texte tronque par le bas : on retire les lignes les plus anciennes jusqu'à ce que tout tienne
    // (retours à la ligne automatiques compris), pour que le message le plus récent reste toujours visible.
    void Print(string text)
    {
        // Un texte encore en cours d'écriture s'affiche d'abord en entier.
        FlushTyping();
        lines.AddRange(text.Split('\n'));
        if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        output.text = string.Join("\n", lines);
        while (lines.Count > 1 && TextHeight(output.text) > output.rectTransform.rect.height)
        {
            lines.RemoveAt(0);
            output.text = string.Join("\n", lines);
        }
    }

    // Lignes déjà écrites suivies du début du texte en cours d'écriture (sans le retenir).
    void Show(string partial)
    {
        var shown = new List<string>(lines);
        shown.AddRange(partial.Split('\n'));
        if (shown.Count > MaxLines) shown.RemoveRange(0, shown.Count - MaxLines);
        output.text = string.Join("\n", shown);
        while (shown.Count > 1 && TextHeight(output.text) > output.rectTransform.rect.height)
        {
            shown.RemoveAt(0);
            output.text = string.Join("\n", shown);
        }
    }

    float TextHeight(string text)
    {
        TextGenerationSettings settings = output.GetGenerationSettings(new Vector2(output.rectTransform.rect.width, 0f));
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        return output.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / output.pixelsPerUnit;
    }
}
