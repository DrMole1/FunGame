using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Gère le lobby, la connexion (hôte / client) et la synchronisation des joueurs.
// Protocole texte, champs séparés par '|' :
//   J|nom|teinte        client -> hôte : je rejoins la partie
//   P|id|nom|teinte     hôte -> clients : un joueur est présent
//   S|id|x|y|z|yaw      état d'un joueur (l'hôte relaie ceux des clients, cf. PeerNet.jslib)
//   L|id                hôte -> clients : un joueur est parti
public class NetworkGame : MonoBehaviour
{
    public const string ObjectName = "NetworkManager";
    const string IdPrefix = "fungame-cube-";
    const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    const float SendInterval = 1f / 15f;
    const float ConnectTimeout = 15f;

    enum State { Lobby, Hosting, Joining, InGame }

    class PlayerInfo
    {
        public string name;
        public float hue;
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    State state = State.Lobby;
    bool isHost;
    string myId, hostId, roomCode, myName;
    float myHue, sendTimer, connectTimer;

    CubeController localPlayer;
    Vector3 spawnOrigin;
    LobbyUI ui;
    readonly Dictionary<string, PlayerInfo> roster = new Dictionary<string, PlayerInfo>();
    readonly Dictionary<string, RemotePlayer> remotes = new Dictionary<string, RemotePlayer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindObjectOfType<NetworkGame>() == null)
            new GameObject(ObjectName).AddComponent<NetworkGame>();
    }

    void Awake()
    {
        gameObject.name = ObjectName;
        Application.runInBackground = true;

        localPlayer = FindObjectOfType<CubeController>(true);
        spawnOrigin = localPlayer.transform.position;
        localPlayer.gameObject.SetActive(false);
        OrbitCamera.ControlsEnabled = false;

        ui = gameObject.AddComponent<LobbyUI>();
        ui.Init(this, PlayerPrefs.GetString("playerName", ""), RoomFromUrl());
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

        Transform t = localPlayer.transform;
        if (t.position.y < -20f) Respawn();

        sendTimer += Time.unscaledDeltaTime;
        if (sendTimer >= SendInterval)
        {
            sendTimer = 0f;
            Vector3 p = t.position;
            string msg = "S|" + myId + "|" + F(p.x) + "|" + F(p.y) + "|" + F(p.z) + "|" + F(t.eulerAngles.y);
            if (isHost) PeerNet.Broadcast(msg);
            else PeerNet.Send(hostId, msg);
        }
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

    void BeginSession(string playerName)
    {
        myName = SanitizeName(playerName);
        myHue = Random.value;
        connectTimer = 0f;
        PlayerPrefs.SetString("playerName", myName);
        PlayerPrefs.Save();
    }

    // ---------- Callbacks du plugin JS (SendMessage) ----------

    void OnNetOpen(string id)
    {
        myId = id;
        if (state == State.Hosting)
        {
            roster[myId] = new PlayerInfo { name = myName, hue = myHue };
            EnterGame();
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
        if (state == State.Joining && peerId == hostId)
        {
            PeerNet.Send(hostId, "J|" + myName + "|" + F(myHue));
            EnterGame();
        }
    }

    void OnPeerDisconnected(string peerId)
    {
        if (state != State.InGame) return;
        if (isHost)
        {
            if (roster.Remove(peerId)) PeerNet.Broadcast("L|" + peerId);
            RemoveRemote(peerId);
        }
        else if (peerId == hostId)
        {
            ReturnToLobby("L'hôte a quitté la partie.");
        }
    }

    void OnNetData(string payload)
    {
        int nl = payload.IndexOf('\n');
        if (nl < 0 || state != State.InGame) return;
        string from = payload.Substring(0, nl);
        string[] f = payload.Substring(nl + 1).Split('|');

        switch (f[0])
        {
            case "J" when isHost && f.Length >= 3:
            {
                var info = new PlayerInfo { name = SanitizeName(f[1]), hue = P(f[2]) };
                foreach (var kv in roster)
                    PeerNet.Send(from, PlayerMsg(kv.Key, kv.Value));
                roster[from] = info;
                PeerNet.Broadcast(PlayerMsg(from, info));
                SpawnRemote(from, info);
                break;
            }
            case "P" when !isHost && f.Length >= 4:
                if (f[1] != myId)
                    SpawnRemote(f[1], new PlayerInfo { name = f[2], hue = P(f[3]) });
                break;
            case "S" when f.Length >= 6:
                // L'hôte n'accepte d'un client que son propre état.
                if (isHost && f[1] != from) break;
                if (remotes.TryGetValue(f[1], out var rp))
                    rp.SetTarget(new Vector3(P(f[2]), P(f[3]), P(f[4])), P(f[5]));
                break;
            case "L" when !isHost && f.Length >= 2:
                RemoveRemote(f[1]);
                break;
        }
    }

    // ---------- Partie ----------

    void EnterGame()
    {
        state = State.InGame;
        sendTimer = 0f;

        GameObject go = localPlayer.gameObject;
        Color color = HueColor(myHue);
        go.GetComponent<Renderer>().material.color = color;
        if (!go.TryGetComponent(out NameTag tag)) tag = go.AddComponent<NameTag>();
        tag.Set(myName, color);
        go.SetActive(true);
        Respawn();

        OrbitCamera.ControlsEnabled = true;
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
        localPlayer.gameObject.SetActive(false);
        OrbitCamera.ControlsEnabled = false;
        ui.ShowLobby(message);
    }

    void Respawn()
    {
        Vector2 c = Random.insideUnitCircle * 3f;
        localPlayer.transform.position = spawnOrigin + new Vector3(c.x, 0.5f, c.y);
        var rb = localPlayer.GetComponent<Rigidbody>();
        rb.position = localPlayer.transform.position;
        rb.velocity = Vector3.zero;
    }

    void SpawnRemote(string id, PlayerInfo info)
    {
        if (remotes.ContainsKey(id)) return;
        var template = localPlayer.GetComponent<Renderer>().sharedMaterial;
        remotes[id] = RemotePlayer.Create(id, info.name, HueColor(info.hue), template, spawnOrigin);
        UpdatePlayerCount();
    }

    void RemoveRemote(string id)
    {
        if (remotes.TryGetValue(id, out var rp))
        {
            if (rp != null) Destroy(rp.gameObject);
            remotes.Remove(id);
        }
        UpdatePlayerCount();
    }

    void UpdatePlayerCount() => ui.SetPlayerCount(remotes.Count + 1);

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

    static string SanitizeName(string n)
    {
        n = (n ?? "").Replace("|", "").Replace("\n", "").Replace("\r", "").Trim();
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

    static string PlayerMsg(string id, PlayerInfo info) => "P|" + id + "|" + info.name + "|" + F(info.hue);
    static Color HueColor(float hue) => Color.HSVToRGB(hue, 0.6f, 0.95f);
    static string F(float v) => v.ToString("0.###", Inv);
    static float P(string s) => float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : 0f;
}
