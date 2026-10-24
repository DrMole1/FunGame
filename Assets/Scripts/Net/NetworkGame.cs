using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Gère le lobby, la connexion (hôte / client), la liste des joueurs et la synchronisation des déplacements.
// La logique de partie (quota, objets, Terminal...) est dans GameSession.
// Protocole texte, champs séparés par '|' :
//   J|nom|teinte|skin                 client -> hôte : je rejoins la partie
//   P|id|nom|teinte|skin|chenil       hôte -> clients : un joueur est présent (ou a changé de skin)
//   K|skin                            client -> hôte : changement de skin en jeu
//   S|id|x|y|z|yaw|anim|pv|flags|t    état d'un joueur, t = heure d'envoi (canal rapide ; l'hôte relaie ceux des clients, cf. PeerNet.jslib)
//   L|id                              hôte -> clients : un joueur est parti
//   KK / FULL                         hôte -> client : expulsé / partie complète
// Les autres messages sont transmis à GameSession.
public class NetworkGame : MonoBehaviour
{
    public const string ObjectName = "NetworkManager";
    const string IdPrefix = "fungame-cube-";
    const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    const float SendInterval = 1f / 20f;
    // Image plus longue que ceci chez l'hôte : son onglet était probablement en arrière-plan.
    const float HostStallWarning = 3f;
    const float ConnectTimeout = 15f;
    public const int FlagDead = 1, FlagSneak = 2, FlagTeleport = 4, FlagHidden = 8, FlagCannonball = 16, FlagTerminal = 32;

    enum State { Lobby, Hosting, Joining, InGame }

    public class PlayerInfo
    {
        public string name;
        public float hue;
        public int character;
        public int slot;
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static NetworkGame Instance { get; private set; }

    State state = State.Lobby;
    bool isHost;
    string myId, hostId, roomCode, myName;
    // Pseudo laissé vide au lobby : le joueur porte le nom de son skin, et en change avec lui en cours de partie.
    bool nameFromSkin;
    float myHue, sendTimer, connectTimer;
    int mySkin;

    PlayerController localPlayer;
    CharacterLibrary library;
    LobbyUI ui;
    GameSession session;
    readonly Dictionary<string, PlayerInfo> roster = new Dictionary<string, PlayerInfo>();
    readonly Dictionary<string, RemotePlayer> remotes = new Dictionary<string, RemotePlayer>();

    public bool IsHost => isHost;
    public bool InGame => state == State.InGame;
    public string MyId => myId;
    public string MyName => myName;
    public int MySkin => mySkin;
    public int SkinCount => library.Count;
    // Noms des personnages (Perso 1 à 8, V0.4.8 : Perso 5 à 8), affichés dans les menus de choix du skin.
    static readonly string[] SkinNames = { "Snoopy", "Grumble", "Princess", "Jerk", "Karen", "Peepoo", "Gladys", "Mirkov" };
    public string SkinName(int i) => i >= 0 && i < SkinNames.Length ? SkinNames[i] : "Perso " + (i + 1);
    public PlayerController LocalPlayer => localPlayer;
    public IReadOnlyDictionary<string, PlayerInfo> Roster => roster;
    public IReadOnlyDictionary<string, RemotePlayer> Remotes => remotes;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindObjectOfType<NetworkGame>() == null)
            new GameObject(ObjectName).AddComponent<NetworkGame>();
    }

    void Awake()
    {
        Instance = this;
        gameObject.name = ObjectName;
        Application.runInBackground = true;

        localPlayer = FindObjectOfType<PlayerController>(true);
        library = localPlayer.GetComponent<CharacterLibrary>();
        SetLayerRecursive(localPlayer.gameObject, 2); // Ignore Raycast : invisible pour la visée et la caméra
        if (!localPlayer.TryGetComponent(out PlayerStatus _)) localPlayer.gameObject.AddComponent<PlayerStatus>();
        if (!localPlayer.TryGetComponent(out PlayerInteractor _)) localPlayer.gameObject.AddComponent<PlayerInteractor>();
        var presence = localPlayer.gameObject.AddComponent<PlayerPresence>();
        presence.IsLocal = true;

        session = gameObject.AddComponent<GameSession>();
        session.Init(this, localPlayer);

        mySkin = Mathf.Clamp(PlayerPrefs.GetInt("skin", 0), 0, Mathf.Max(0, library.Count - 1));
        ui = gameObject.AddComponent<LobbyUI>();
        ui.Init(this, PlayerPrefs.GetString("playerName", ""), RoomFromUrl());
        ShowLobbyPreview();
    }

    void Update()
    {
        if (state == State.Hosting || state == State.Joining)
        {
            connectTimer += Time.unscaledDeltaTime;
            if (connectTimer > ConnectTimeout)
                ReturnToLobby("Délai de connexion dépassé. Réessayez.");
            return;
        }
        if (state != State.InGame) return;

        // Le navigateur fige un onglet en arrière-plan : chez l'hôte, La Dame et le relais s'arrêtent pour tous.
        if (isHost && roster.Count > 1 && Time.unscaledDeltaTime > HostStallWarning)
            session.Toast("Votre onglet était en arrière-plan : la partie était figée pour les autres joueurs. Gardez-le au premier plan.");

        Transform t = localPlayer.transform;
        if (t.position.y < -20f) session.RespawnInHub();

        sendTimer += Time.unscaledDeltaTime;
        if (sendTimer >= SendInterval)
        {
            // Rythme régulier : le temps en trop est reporté sur l'envoi suivant.
            sendTimer = Mathf.Min(sendTimer - SendInterval, SendInterval);
            Vector3 p = t.position;
            int flags = (session.LocalDead ? FlagDead : 0) | (localPlayer.Sneaking ? FlagSneak : 0) | (GameSession.Teleporting ? FlagTeleport : 0)
                | (GameSession.InCannon ? FlagHidden : 0) | (GameSession.CannonFlying ? FlagCannonball : 0) | (TerminalUI.IsOpen ? FlagTerminal : 0);
            string msg = "S|" + myId + "|" + F(p.x) + "|" + F(p.y) + "|" + F(p.z) + "|" + F(t.eulerAngles.y)
                + "|" + (int)localPlayer.CurrentAnimState + "|" + session.LocalHp + "|" + flags + "|" + Clock();
            if (isHost) PeerNet.BroadcastFast(msg);
            else PeerNet.SendFast(hostId, msg);
        }
    }

    // ---------- Envoi ----------

    // L'hôte traite ses propres requêtes localement, comme celles des clients.
    public void ToHost(string msg)
    {
        if (isHost) Dispatch(myId, msg);
        else PeerNet.Send(hostId, msg);
    }

    public void ToAll(string msg)
    {
        PeerNet.Broadcast(msg);
        Dispatch(myId, msg);
    }

    public void ToPeer(string id, string msg)
    {
        if (id == myId) Dispatch(myId, msg);
        else PeerNet.Send(id, msg);
    }

    // ---------- Actions du lobby ----------

    public void HostGame(string playerName)
    {
        BeginSession(playerName);
        isHost = true;
        state = State.Hosting;
        roomCode = RandomCode();
        ui.ShowConnecting("Création de la partie…");
        PeerNet.Host(IdPrefix + roomCode);
    }

    public void JoinGame(string playerName, string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            ui.ShowLobby("Entrez le code de la partie à rejoindre.");
            return;
        }
        BeginSession(playerName);
        isHost = false;
        state = State.Joining;
        roomCode = code;
        hostId = IdPrefix + code;
        ui.ShowConnecting("Connexion à la partie " + code + "…");
        PeerNet.Join(hostId);
    }

    public void LeaveGame() => ReturnToLobby("");

    public void CopyInviteLink() => PeerNet.CopyText(InviteLink());

    // Choix du skin : dans le lobby (aperçu) ou en jeu (synchronisé).
    public void SelectSkin(int index)
    {
        if (library.Count == 0) return;
        mySkin = (index % library.Count + library.Count) % library.Count;
        PlayerPrefs.SetInt("skin", mySkin);
        PlayerPrefs.Save();
        localPlayer.GetComponent<CharacterAvatar>().SetCharacter(library, mySkin);
        // "1" : le pseudo suit le skin (l'hôte le renomme).
        if (state == State.InGame) ToHost("K|" + mySkin + "|" + (nameFromSkin ? "1" : "0"));
    }

    // Commande /kick de l'hôte. Retourne le nom exact du joueur expulsé, ou null.
    public string Kick(string playerName)
    {
        if (!isHost) return null;
        foreach (var kv in roster)
        {
            if (kv.Key == myId || !string.Equals(kv.Value.name, playerName, System.StringComparison.OrdinalIgnoreCase)) continue;
            PeerNet.Send(kv.Key, "KK");
            PeerNet.Kick(kv.Key);
            string n = kv.Value.name;
            OnPeerDisconnected(kv.Key);
            return n;
        }
        return null;
    }

    void BeginSession(string playerName)
    {
        // Pseudo laissé vide : le joueur prend le nom de son personnage (skin). Ce nom n'est pas retenu comme pseudo,
        // pour suivre le skin choisi la fois suivante.
        bool blank = string.IsNullOrWhiteSpace(playerName);
        nameFromSkin = blank;
        myName = blank ? SanitizeName(SkinName(mySkin)) : SanitizeName(playerName);
        myHue = Random.value;
        connectTimer = 0f;
        PlayerPrefs.SetString("playerName", blank ? "" : myName);
        PlayerPrefs.Save();
    }

    // ---------- Callbacks du plugin JS (SendMessage) ----------

    void OnNetOpen(string id)
    {
        myId = id;
        if (state == State.Hosting)
        {
            var me = new PlayerInfo { name = myName, hue = myHue, character = mySkin, slot = 0 };
            roster[myId] = me;
            session.StartAsHost();
            EnterGame(me);
            session.OnPlayerJoined(myId);
        }
        // En mode Joining, on attend OnPeerConnected de l'hôte.
    }

    void OnNetError(string type)
    {
        if (state == State.InGame)
        {
            Debug.LogWarning("PeerNet: " + type);
            return;
        }
        if (state == State.Hosting && type == "unavailable-id")
        {
            roomCode = RandomCode();
            PeerNet.Host(IdPrefix + roomCode);
            return;
        }
        if (state == State.Lobby) return;
        ReturnToLobby(ErrorMessage(type));
    }

    void OnPeerConnected(string peerId)
    {
        // Côté hôte, on attend le message J du nouveau joueur.
        // Côté client, on entre en jeu à la réception de notre propre P.
        if (state == State.Joining && peerId == hostId)
            PeerNet.Send(hostId, "J|" + myName + "|" + F(myHue) + "|" + mySkin);
    }

    void OnPeerDisconnected(string peerId)
    {
        if (state != State.InGame) return;
        if (isHost)
        {
            if (roster.TryGetValue(peerId, out var info))
            {
                roster.Remove(peerId);
                PeerNet.Broadcast("L|" + peerId);
                session.OnPlayerLeft(peerId, info.name);
            }
            RemoveRemote(peerId);
        }
        else if (peerId == hostId)
        {
            // Tous morts : l'écran de GAME OVER reste affiché, chacun revient au lobby avec le bouton.
            if (session.WipedOut) return;
            ReturnToLobby("L'hôte a quitté la partie.");
        }
    }

    void OnNetData(string payload)
    {
        int nl = payload.IndexOf('\n');
        if (nl < 0 || (state != State.InGame && state != State.Joining)) return;
        // Un client n'accepte que les messages de l'hôte.
        string from = payload.Substring(0, nl);
        if (!isHost && from != hostId) return;
        Dispatch(from, payload.Substring(nl + 1));
    }

    void Dispatch(string from, string msg)
    {
        string[] f = msg.Split('|');
        switch (f[0])
        {
            case "J" when isHost && from != myId && f.Length >= 4:
                HandleJoin(from, f);
                break;
            case "P" when f.Length >= 6:
            {
                var info = new PlayerInfo { name = f[2], hue = P(f[3]), character = I(f[4]), slot = I(f[5]) };
                if (isHost) break; // l'hôte a déjà mis à jour sa liste
                roster[f[1]] = info;
                if (f[1] != myId)
                {
                    if (remotes.TryGetValue(f[1], out var existing))
                    {
                        existing.SetCharacter(library, info.character);
                        existing.SetName(info.name);
                    }
                    else SpawnRemote(f[1], info);
                }
                else if (state == State.Joining)
                    EnterGame(info);
                else
                    myName = info.name;
                session.OnRosterChanged();
                break;
            }
            case "K" when isHost && f.Length >= 2:
                if (roster.TryGetValue(from, out var pi))
                {
                    pi.character = Mathf.Clamp(I(f[1]), 0, library.Count - 1);
                    // Pseudo tiré du skin : il prend le nom du nouveau skin (numéroté s'il est déjà porté).
                    if (f.Length >= 3 && f[2] == "1") pi.name = UniqueName(SanitizeName(SkinName(pi.character)), from);
                    if (remotes.TryGetValue(from, out var rp0))
                    {
                        rp0.SetCharacter(library, pi.character);
                        rp0.SetName(pi.name);
                    }
                    if (from == myId) myName = pi.name;
                    PeerNet.Broadcast(PlayerMsg(from, pi));
                    session.OnRosterChanged();
                }
                break;
            case "S" when f.Length >= 10:
                // L'hôte n'accepte d'un client que son propre état.
                if (isHost && f[1] != from) break;
                if (remotes.TryGetValue(f[1], out var rp))
                    rp.SetTarget(D(f[9]), new Vector3(P(f[2]), P(f[3]), P(f[4])), P(f[5]), (AnimState)Mathf.Clamp(I(f[6]), 0, (int)AnimState.Dance3), I(f[7]), I(f[8]));
                break;
            case "L" when !isHost && f.Length >= 2:
                if (roster.TryGetValue(f[1], out var left))
                {
                    roster.Remove(f[1]);
                    session.OnPlayerLeft(f[1], left.name);
                }
                RemoveRemote(f[1]);
                break;
            case "KK" when !isHost:
                ReturnToLobby("Vous avez été expulsé de la partie par l'hôte.");
                break;
            case "FULL" when !isHost:
                ReturnToLobby("La partie est complète (" + GameRules.MaxPlayers + " joueurs maximum).");
                break;
            default:
                session.HandleMessage(from, f, msg);
                break;
        }
    }

    void HandleJoin(string from, string[] f)
    {
        if (state != State.InGame || roster.ContainsKey(from)) return;
        if (roster.Count >= GameRules.MaxPlayers)
        {
            PeerNet.Send(from, "FULL");
            PeerNet.Kick(from);
            return;
        }
        var info = new PlayerInfo
        {
            name = UniqueName(SanitizeName(f[1])),
            hue = P(f[2]),
            character = Mathf.Clamp(I(f[3]), 0, Mathf.Max(0, library.Count - 1)),
            slot = FreeSlot(),
        };
        foreach (var kv in roster)
            PeerNet.Send(from, PlayerMsg(kv.Key, kv.Value));
        session.SendSnapshot(from);
        roster[from] = info;
        PeerNet.Broadcast(PlayerMsg(from, info));
        SpawnRemote(from, info);
        session.OnPlayerJoined(from);
    }

    // ---------- Partie ----------

    void ShowLobbyPreview()
    {
        GameObject go = localPlayer.gameObject;
        go.SetActive(true);
        go.GetComponent<CharacterAvatar>().SetCharacter(library, mySkin);
        go.GetComponent<CharacterAvatar>().SetDead(false);
        localPlayer.SetFrozen(true);
        localPlayer.Teleport(new Vector3(0f, library.height * 0.5f, -4f), 0f);
        if (go.TryGetComponent(out NameTag tag)) tag.Show(false);
        PlayerController.InputLocked = true;
        OrbitCamera.ControlsEnabled = false;
    }

    void EnterGame(PlayerInfo me)
    {
        state = State.InGame;
        sendTimer = 0f;
        myName = me.name;

        GameObject go = localPlayer.gameObject;
        go.GetComponent<CharacterAvatar>().SetCharacter(library, me.character);
        // Le joueur ne voit pas son propre pseudo (seulement celui des autres).
        localPlayer.SetFrozen(false);
        PlayerController.InputLocked = false;
        OrbitCamera.ControlsEnabled = true;

        session.OnLocalEnter();
        ui.ShowHud(roomCode, isHost);
        UpdatePlayerCount();
    }

    void ReturnToLobby(string message)
    {
        PeerNet.Leave();
        foreach (var rp in remotes.Values)
            if (rp != null) Destroy(rp.gameObject);
        remotes.Clear();
        roster.Clear();
        state = State.Lobby;
        isHost = false;
        session.ResetSession();
        ShowLobbyPreview();
        ui.ShowLobby(message);
    }

    void SpawnRemote(string id, PlayerInfo info)
    {
        if (remotes.ContainsKey(id)) return;
        remotes[id] = RemotePlayer.Create(id, info.name, HueColor(info.hue), library, info.character, new Vector3(0f, library.height * 0.5f, 0f));
        UpdatePlayerCount();
        session.OnRosterChanged();
    }

    void RemoveRemote(string id)
    {
        if (remotes.TryGetValue(id, out var rp))
        {
            if (rp != null) Destroy(rp.gameObject);
            remotes.Remove(id);
        }
        UpdatePlayerCount();
        session.OnRosterChanged();
    }

    void UpdatePlayerCount() => ui.SetPlayerCount(remotes.Count + 1);

    int FreeSlot()
    {
        for (int s = 0; s < GameRules.MaxPlayers; s++)
        {
            bool used = false;
            foreach (var p in roster.Values) if (p.slot == s) used = true;
            if (!used) return s;
        }
        return 0;
    }

    // Deux joueurs ne peuvent pas porter le même pseudo (utile pour /kick). 'self' : joueur renommé, ignoré.
    string UniqueName(string n, string self = null)
    {
        string candidate = n;
        for (int k = 2; ; k++)
        {
            bool taken = false;
            foreach (var kv in roster)
                if (kv.Key != self && string.Equals(kv.Value.name, candidate, System.StringComparison.OrdinalIgnoreCase)) taken = true;
            if (!taken) return candidate;
            candidate = n + k;
        }
    }

    // ---------- Utilitaires ----------

    string InviteLink()
    {
        string url = Application.absoluteURL;
        if (string.IsNullOrEmpty(url)) url = "";
        int cut = url.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) url = url.Substring(0, cut);
        return url + "?room=" + roomCode;
    }

    static string RoomFromUrl()
    {
        string url = Application.absoluteURL;
        if (string.IsNullOrEmpty(url)) return "";
        int q = url.IndexOf('?');
        if (q < 0) return "";
        foreach (string part in url.Substring(q + 1).Split('&', '#'))
            if (part.StartsWith("room=", System.StringComparison.OrdinalIgnoreCase))
                return part.Substring(5).ToUpperInvariant();
        return "";
    }

    static string RandomCode()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 5; i++) sb.Append(CodeAlphabet[Random.Range(0, CodeAlphabet.Length)]);
        return sb.ToString();
    }

    public static string SanitizeName(string n)
    {
        n = (n ?? "").Replace("|", "").Replace("\n", "").Replace("\r", "").Replace("<", "").Replace(">", "").Trim();
        if (n.Length > 16) n = n.Substring(0, 16);
        return n.Length == 0 ? "Joueur" : n;
    }

    static string ErrorMessage(string type)
    {
        switch (type)
        {
            case "peer-unavailable": return "Partie introuvable. Vérifiez le code.";
            case "editor": return "Rejoindre une partie ne fonctionne que dans la version navigateur.";
            case "peerjs-missing": return "Bibliothèque réseau non chargée. Vérifiez votre connexion internet.";
            case "browser-incompatible": return "Votre navigateur ne supporte pas WebRTC.";
            case "network":
            case "server-error":
            case "socket-error":
            case "socket-closed": return "Impossible de joindre le serveur de connexion.";
            default: return "Erreur réseau (" + type + ").";
        }
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }

    static string PlayerMsg(string id, PlayerInfo info) =>
        "P|" + id + "|" + info.name + "|" + F(info.hue) + "|" + info.character + "|" + info.slot;
    public static Color HueColor(float hue) => Color.HSVToRGB(hue, 0.6f, 0.95f);
    public static string F(float v) => v.ToString("0.###", Inv);
    public static float P(string s) => float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : 0f;
    public static int I(string s) => int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : 0;
    public static double D(string s) => double.TryParse(s, NumberStyles.Float, Inv, out double v) ? v : 0.0;
    // Horloge locale (s) jointe aux états envoyés, pour les rejouer au bon rythme chez les autres.
    public static string Clock() => Time.realtimeSinceStartupAsDouble.ToString("0.###", Inv);
}
