using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Zone de saisie du Terminal, affichée uniquement chez le joueur qui l'utilise.
// Commandes insensibles à la casse : /buy (ou /buy <ID>), /info (sous-menu : /hello, puis /step1 une fois débloquée),
// /skip round, /sell (cachée depuis l'arrivée de Pogo) et les commandes secrètes /kick, /spawn, /tp, /godmod et /give
// (hôte). Les commandes d'info vident d'abord l'écran, puis leur texte s'affiche caractère par caractère, rapidement.
public class TerminalUI : MonoBehaviour
{
    // /skip round n'apparaît qu'une fois le quota de la manche atteint.
    public static string Menu(bool canSkip) =>
        Loc.T("Bienvenue sur le Terminal !\nTaper une commande pour continuer :") + "\n/buy\n/info" + (canSkip ? "\n/skip round" : "");
    static string Quit => "\n" + Loc.T("Faire ECHAP pour quitter.");
    const int MaxLines = 18;
    const float TypeSpeed = 90f; // caractères par seconde

    enum Mode { Command, Sell, Buy }

    public static bool IsOpen { get; private set; }

    GameSession session;
    GameObject panel;
    Text output;
    InputField input;
    Mode mode;
    readonly List<string> lines = new List<string>();
    bool waitingBuy, waitingSkip;
    // Texte en cours d'écriture (commandes d'info) et nombre de caractères déjà affichés.
    string typing = "";
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

        input = UiKit.Field(panel.transform, "Taper une commande puis Entrée…", "", 40);
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

        panel.SetActive(false);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        mode = Mode.Command;
        waitingBuy = waitingSkip = false;
        typing = "";
        lines.Clear();
        Print(Menu(session.CanSkipRound));
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

    // Écrit 'text' caractère par caractère, à la suite.
    void Type(string text)
    {
        FlushTyping();
        typing = text;
        typed = 0f;
    }

    // Commandes d'info : l'écran est d'abord vidé (texte en cours d'écriture compris), seule la commande tapée reste.
    void ClearScreen(string command)
    {
        typing = "";
        lines.Clear();
        Print("> " + command);
    }

    void FlushTyping()
    {
        if (typing.Length == 0) return;
        string done = typing;
        typing = "";
        Print(done);
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

    void Execute(string text)
    {
        string lower = text.ToLowerInvariant();
        switch (mode)
        {
            case Mode.Sell:
                mode = Mode.Command;
                Sell(lower);
                return;
            case Mode.Buy:
                mode = Mode.Command;
                Buy(lower);
                return;
        }

        if (lower == "/sell")
        {
            mode = Mode.Sell;
            Print(Loc.T("Écrire le numéro de l'objet à vendre (1-2-3-4-5)."));
        }
        else if (lower == "/buy")
        {
            var sb = new System.Text.StringBuilder(Loc.T("Objets achetables :"));
            foreach (var def in Items.Buyable())
                // Une fois achetée, la Boussole disparaît des propositions.
                if (def.type != ItemType.Boussole || !session.CompassUnlocked)
                    sb.Append("\n  ID ").Append(def.terminalId).Append(" - ").Append(def.name).Append(" : ").Append(Items.Price(def.buyPrice));
            sb.Append("\n").Append(Loc.T("Écrire l'ID de l'objet à acheter."));
            Print(sb.ToString());
            mode = Mode.Buy;
        }
        else if (lower.StartsWith("/buy "))
        {
            // Raccourci : /buy <ID> achète directement, sans passer par la liste.
            Buy(lower.Substring(5).Trim());
        }
        else if (lower == "/info")
        {
            ClearScreen(text);
            Print(session.InfoMenu() + Quit);
        }
        else if (lower == "/hello")
        {
            ClearScreen(text);
            Type(session.HelloText() + Quit);
        }
        else if (lower == "/step1" && session.Step1Unlocked)
        {
            ClearScreen(text);
            Type(session.Step1Text() + Quit);
        }
        else if (lower.StartsWith("/give ") && session.IsHost)
        {
            string result = session.Give(text.Substring(6).Trim());
            Print((result ?? Loc.T("Commande invalide.")) + Quit);
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"^/skip\s+round$"))
        {
            if (waitingSkip) return;
            string refused = session.RequestSkipRound();
            if (refused != null) Error(refused + Quit);
            else waitingSkip = true;
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
            Print(kicked != null ? Loc.F("Le joueur {0} a été expulsé de la partie.", kicked) + Quit
                                 : Loc.F("Aucun autre joueur nommé {0}.", target) + Quit);
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"^/godmod\s+(on|off)$") && session.IsHost)
        {
            Print(session.SetGodmode(lower.EndsWith("on")) + Quit);
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

    void Sell(string arg)
    {
        if (arg.Length != 1 || arg[0] < '1' || arg[0] > '5')
        {
            Error(Loc.T("Commande invalide.") + Quit);
            return;
        }
        int slot = arg[0] - '1';
        ItemDef sold = session.SellSlot(slot);
        if (sold == null) Error(Loc.F("Vous n'avez pas d'objet {0} en inventaire !", slot + 1) + Quit);
        else Print(Loc.F("Vous avez vendu {0} pour {1} !", sold.name, Items.Price(sold.sellPrice)) + Quit);
    }

    void Buy(string arg)
    {
        ItemDef def = int.TryParse(arg, out int id) ? Items.ByTerminalId(id) : null;
        if (def == null || def.buyPrice <= 0)
        {
            Error(Loc.T("Commande invalide.") + Quit);
            return;
        }
        if (waitingBuy) return;
        if (def.type == ItemType.Boussole && session.CompassUnlocked)
        {
            Error(Loc.T("Commande invalide.") + Quit);
            return;
        }
        if (def.inventory && !session.HasFreeSlot)
        {
            Error(Loc.F("Votre inventaire est plein ! Impossible d'acheter {0}.", def.name) + Quit);
            return;
        }
        if (session.Cash < def.buyPrice && !session.Godmode)
        {
            Error(Loc.F("Cagnotte insuffisante pour acheter {0} ({1}).", def.name, Items.Price(def.buyPrice)) + Quit);
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
            Print(Loc.F("Vous avez acheté la Boussole pour {0} !\nElle s'affiche désormais en haut de l'écran de tous les joueurs.", Items.Price(session.Godmode ? 0 : def.buyPrice)) + Quit);
        else if (ok)
            Print(Loc.F("Vous avez acheté {0} pour {1} !", def.name, Items.Price(session.Godmode ? 0 : def.buyPrice)) + Quit);
        else if (alreadyOwned)
            Print(Loc.T("La Boussole a déjà été achetée.") + Quit);
        else
            Print(Loc.F("Cagnotte insuffisante pour acheter {0} ({1}).", def.name, Items.Price(def.buyPrice)) + Quit);
    }

    // Réponse de l'hôte à /skip round (bonus : golds ajoutés à la cagnotte, -1 si refusé).
    public void OnSkipResult(int bonus)
    {
        waitingSkip = false;
        if (!IsOpen) return;
        if (bonus < 0) Error(Loc.T("Le quota de la manche n'est pas encore atteint.") + Quit);
        else Print(Loc.N(bonus, "Manche passée ! {0} ajoutée à la cagnotte pour le temps restant.|Manche passée ! {0} ajoutées à la cagnotte pour le temps restant.", Items.Price(bonus)) + Quit);
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
