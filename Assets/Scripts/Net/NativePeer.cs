#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

// Réseau du build Windows (et de l'éditeur) : le même protocole que PeerNet.jslib (PeerJS), réécrit en C# avec le package
// Unity WebRTC. Même serveur de signalisation public (0.peerjs.com), mêmes identifiants de partie, mêmes canaux :
// une connexion PeerJS "json" fiable (chaînes JSON envoyées en binaire, comme le fait PeerJS) et le canal rapide négocié
// "fast" (id 100, non ordonné, sans renvoi). Un joueur du .exe et un joueur du navigateur peuvent donc jouer ensemble.
// Les réponses vont, comme pour le plugin JS, au GameObject "NetworkManager" par SendMessage (OnNetOpen, OnNetError,
// OnPeerConnected, OnPeerDisconnected, OnNetData). L'hôte relaie aussitôt les états des joueurs ("S|...") aux autres.
public class NativePeer : MonoBehaviour
{
    const string ServerHost = "0.peerjs.com", ServerKey = "peerjs", PeerJsVersion = "1.5.4";
    const float HeartbeatInterval = 5f;

    class Conn
    {
        public string peer, id;
        public RTCPeerConnection pc;
        public RTCDataChannel dc, fast;
        public bool opened, closed, remoteSet;
        public readonly List<RTCIceCandidateInit> pendingCandidates = new List<RTCIceCandidateInit>();
    }

    static NativePeer instance;
    static NativePeer Instance
    {
        get
        {
            if (instance == null)
            {
                EnsureWebRtc();
                var go = new GameObject("NativePeer");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<NativePeer>();
            }
            return instance;
        }
    }

    // Sécurité : si l'initialisation automatique du package n'a pas eu lieu (build fait depuis une autre cible), on la fait.
    static void EnsureWebRtc()
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        try
        {
            var webrtc = typeof(WebRTC);
            if (webrtc.GetProperty("Context", Any)?.GetValue(null) != null) return;
            webrtc.GetMethod("RuntimeInitializeOnLoadMethod", Any)?.Invoke(null, null);
            webrtc.Assembly.GetType("Unity.WebRTC.ContextManager")?.GetMethod("Init", Any)?.Invoke(null, null);
            Debug.Log("NativePeer : WebRTC initialisé à la main.");
        }
        catch (Exception e) { Debug.LogWarning("NativePeer : initialisation de WebRTC impossible (" + e.Message + ")"); }
    }

    ClientWebSocket ws;
    CancellationTokenSource cts;
    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    readonly Dictionary<string, Conn> byConnId = new Dictionary<string, Conn>();
    readonly Dictionary<string, Conn> conns = new Dictionary<string, Conn>();    // connexions ouvertes, par pair
    string myId, joinTarget;
    bool isHost, serverOpen;
    float heartbeatAt;
    int session;   // change à chaque Reset : les tâches d'une ancienne session sont ignorées

    // ---------- API (comme PeerNet.jslib) ----------

    public static void Host(string id) => Instance.Begin(id, null);
    public static void Join(string hostId) => Instance.Begin(null, hostId);
    public static void Leave() { if (instance != null) instance.Reset(); }

    public static void Send(string to, string msg)
    {
        if (instance != null && instance.conns.TryGetValue(to, out var c)) instance.SendReliable(c, msg);
    }

    public static void Broadcast(string msg)
    {
        if (instance == null) return;
        foreach (var c in instance.conns.Values) instance.SendReliable(c, msg);
    }

    public static void SendFast(string to, string msg)
    {
        if (instance != null && instance.conns.TryGetValue(to, out var c)) instance.SendFastTo(c, msg);
    }

    public static void BroadcastFast(string msg)
    {
        if (instance == null) return;
        foreach (var c in instance.conns.Values) instance.SendFastTo(c, msg);
    }

    // La connexion est retirée tout de suite (pas de second OnPeerDisconnected), et fermée un peu plus tard pour laisser
    // partir le message d'expulsion.
    public static void Kick(string id)
    {
        if (instance == null || !instance.conns.TryGetValue(id, out var c)) return;
        instance.conns.Remove(id);
        c.opened = false;
        instance.StartCoroutine(instance.CloseLater(c, 0.4f));
    }

    // ---------- Session ----------

    static void Log(string text) => Debug.Log("[NativePeer] " + text);

    static void Emit(string method, string arg)
    {
        var go = GameObject.Find(NetworkGame.ObjectName);
        if (go != null) go.SendMessage(method, arg, SendMessageOptions.DontRequireReceiver);
    }

    void Begin(string hostId, string target)
    {
        Reset();
        isHost = hostId != null;
        joinTarget = target;
        myId = hostId ?? RandomId();
        Log(isHost ? "hébergement " + myId : "connexion à " + target);
        Connect(myId);
    }

    void Reset()
    {
        session++;
        foreach (var c in new List<Conn>(byConnId.Values)) CloseConn(c, false);
        byConnId.Clear();
        conns.Clear();
        serverOpen = false;
        joinTarget = null;
        isHost = false;
        try { cts?.Cancel(); } catch { }
        var old = ws;
        ws = null;
        cts = null;
        if (old != null)
        {
            try { old.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            try { old.Dispose(); } catch { }
        }
        while (incoming.TryDequeue(out _)) { }
    }

    static string RandomId()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var sb = new StringBuilder();
        var rnd = new System.Random();
        for (int i = 0; i < 16; i++) sb.Append(chars[rnd.Next(chars.Length)]);
        return sb.ToString();
    }

    void Connect(string id)
    {
        int s = session;
        ws = new ClientWebSocket();
        cts = new CancellationTokenSource();
        var socket = ws;
        var token = cts.Token;
        string url = "wss://" + ServerHost + ":443/peerjs?key=" + ServerKey + "&id=" + Uri.EscapeDataString(id) + "&token=" + RandomId()
            + "&version=" + PeerJsVersion;
        Task.Run(async () =>
        {
            try
            {
                await socket.ConnectAsync(new Uri(url), token);
                incoming.Enqueue(s + "\n#connected");
                var buffer = new byte[16384];
                var text = new StringBuilder();
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;
                    incoming.Enqueue(s + "\n" + text);
                    text.Clear();
                }
                if (!token.IsCancellationRequested) incoming.Enqueue(s + "\n#closed");
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    Debug.LogWarning("NativePeer : " + e.Message);
                    incoming.Enqueue(s + "\n#error");
                }
            }
        });
    }

    void SendServer(string json)
    {
        var socket = ws;
        if (socket == null || socket.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        // Un envoi à la fois sur un WebSocket : on les enchaîne.
        lock (sendLock) sendChain = sendChain.ContinueWith(_ =>
        {
            try { socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(); }
            catch { }
        });
    }

    readonly object sendLock = new object();
    Task sendChain = Task.CompletedTask;

    // Diagnostic (V0.7.6, Note 06) : trafic de la dernière période, écrit dans le journal toutes les 10 secondes.
    int sentMsgs, sentBytes, recvMsgs, recvBytes, largest;
    float statsAt = 10f;

    void LogStats()
    {
        if (Time.unscaledTime < statsAt) return;
        statsAt = Time.unscaledTime + 10f;
        if (conns.Count == 0) { sentMsgs = sentBytes = recvMsgs = recvBytes = largest = 0; return; }
        Log("trafic sur 10 s : envoyé " + sentMsgs + " messages (" + sentBytes / 1024 + " Ko), reçu " + recvMsgs + " messages ("
            + recvBytes / 1024 + " Ko), plus gros message " + largest + " octets, " + conns.Count + " connexion(s)");
        sentMsgs = sentBytes = recvMsgs = recvBytes = largest = 0;
    }

    void Update()
    {
        LogStats();
        while (incoming.TryDequeue(out string item))
        {
            int nl = item.IndexOf('\n');
            if (nl < 0 || item.Substring(0, nl) != session.ToString()) continue;
            string body = item.Substring(nl + 1);
            if (body == "#connected") { Log("serveur de signalisation joint"); continue; }
            if (body == "#error" || body == "#closed")
            {
                bool wasOpen = serverOpen;
                serverOpen = false;
                // Comme PeerJS : les connexions déjà établies continuent sans le serveur de signalisation.
                if (!wasOpen) Emit("OnNetError", body == "#error" ? "network" : "socket-closed");
                continue;
            }
            HandleServer(body);
        }
        if (serverOpen && Time.unscaledTime >= heartbeatAt)
        {
            heartbeatAt = Time.unscaledTime + HeartbeatInterval;
            SendServer("{\"type\":\"HEARTBEAT\"}");
        }
    }

    void OnDestroy() => Reset();
    void OnApplicationQuit() => Reset();

    // ---------- Signalisation ----------

    void HandleServer(string json)
    {
        var msg = MiniJson.Parse(json) as Dictionary<string, object>;
        if (msg == null) return;
        string type = MiniJson.Str(msg, "type"), src = MiniJson.Str(msg, "src");
        var payload = MiniJson.Obj(msg, "payload");
        if (type != "CANDIDATE") Log("serveur : " + type + (src != null ? " de " + src : ""));
        switch (type)
        {
            case "OPEN":
                serverOpen = true;
                heartbeatAt = Time.unscaledTime + HeartbeatInterval;
                Emit("OnNetOpen", myId);
                if (joinTarget != null) StartCoroutine(Offer(joinTarget));
                break;
            case "ID-TAKEN":
                Emit("OnNetError", "unavailable-id");
                break;
            case "INVALID-KEY":
            case "ERROR":
                Emit("OnNetError", "server-error");
                break;
            case "OFFER":
                if (payload != null && src != null && MiniJson.Str(payload, "type") == "data") StartCoroutine(Answer(src, payload));
                break;
            case "ANSWER":
            {
                string id = MiniJson.Str(payload, "connectionId");
                var sdp = MiniJson.Obj(payload, "sdp");
                if (id != null && sdp != null && byConnId.TryGetValue(id, out var c)) StartCoroutine(SetRemote(c, RTCSdpType.Answer, MiniJson.Str(sdp, "sdp")));
                break;
            }
            case "CANDIDATE":
            {
                string id = MiniJson.Str(payload, "connectionId");
                var cand = MiniJson.Obj(payload, "candidate");
                if (id == null || cand == null || !byConnId.TryGetValue(id, out var c)) break;
                var init = new RTCIceCandidateInit { candidate = MiniJson.Str(cand, "candidate"), sdpMid = MiniJson.Str(cand, "sdpMid") };
                if (cand.TryGetValue("sdpMLineIndex", out var line) && line is double d) init.sdpMLineIndex = (int)d;
                if (c.remoteSet) AddCandidate(c, init);
                else c.pendingCandidates.Add(init);
                break;
            }
            case "LEAVE":
                // Le pair a quitté le serveur : comme PeerJS, ses connexions sont fermées.
                if (src != null) foreach (var c in new List<Conn>(byConnId.Values)) if (c.peer == src) CloseConn(c, true);
                break;
            case "EXPIRE":
                // Offre restée sans destinataire : la partie n'existe pas.
                if (src != null && src == joinTarget && !conns.ContainsKey(src)) Emit("OnNetError", "peer-unavailable");
                break;
        }
    }

    static RTCConfiguration Config()
    {
        // Les mêmes serveurs que PeerJS par défaut (STUN de Google, TURN de PeerJS).
        return new RTCConfiguration
        {
            iceServers = new[]
            {
                new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
                new RTCIceServer
                {
                    urls = new[] { "turn:eu-0.turn.peerjs.com:3478", "turn:us-0.turn.peerjs.com:3478" },
                    username = "peerjs", credential = "peerjsp", credentialType = RTCIceCredentialType.Password,
                },
            },
        };
    }

    Conn NewConn(string peer, string id)
    {
        var config = Config();
        var c = new Conn { peer = peer, id = id, pc = new RTCPeerConnection(ref config) };
        byConnId[id] = c;
        c.pc.OnIceCandidate = cand =>
        {
            if (c.closed || cand == null || string.IsNullOrEmpty(cand.Candidate)) return;
            string line = cand.SdpMLineIndex.HasValue ? cand.SdpMLineIndex.Value.ToString() : "0";
            SendServer("{\"type\":\"CANDIDATE\",\"payload\":{\"candidate\":{\"candidate\":" + MiniJson.Quote(cand.Candidate)
                + ",\"sdpMid\":" + MiniJson.Quote(cand.SdpMid) + ",\"sdpMLineIndex\":" + line
                + (string.IsNullOrEmpty(cand.UserNameFragment) ? "" : ",\"usernameFragment\":" + MiniJson.Quote(cand.UserNameFragment))
                + "},\"type\":\"data\",\"connectionId\":" + MiniJson.Quote(id) + "},\"dst\":" + MiniJson.Quote(peer) + "}");
        };
        c.pc.OnIceConnectionChange = state =>
        {
            Log("ICE " + peer + " : " + state);
            if (state == RTCIceConnectionState.Failed || state == RTCIceConnectionState.Closed) CloseConn(c, true);
        };
        // Le canal rapide, négocié des deux côtés avec le même identifiant (aucune signalisation en plus).
        c.fast = c.pc.CreateDataChannel("fast", new RTCDataChannelInit { negotiated = true, id = 100, ordered = false, maxRetransmits = 0 });
        c.fast.OnMessage = bytes => { if (!c.closed) Receive(c.peer, Encoding.UTF8.GetString(bytes)); };
        return c;
    }

    void SetupChannel(Conn c, RTCDataChannel dc)
    {
        c.dc = dc;
        dc.OnOpen = () => Opened(c);
        dc.OnClose = () => CloseConn(c, true);
        dc.OnMessage = bytes =>
        {
            if (c.closed) return;
            // Sérialisation "json" de PeerJS : une valeur JSON (ici une chaîne) en UTF-8.
            object value = MiniJson.Parse(Encoding.UTF8.GetString(bytes));
            if (value is string text) Receive(c.peer, text);
            else if (value is Dictionary<string, object> o && MiniJson.Obj(o, "__peerData") is Dictionary<string, object> pd
                     && MiniJson.Str(pd, "type") == "close") CloseConn(c, true);
        };
        if (dc.ReadyState == RTCDataChannelState.Open) Opened(c);
    }

    void Opened(Conn c)
    {
        if (c.opened || c.closed) return;
        Log("connecté à " + c.peer);
        c.opened = true;
        conns[c.peer] = c;
        Emit("OnPeerConnected", c.peer);
    }

    // Joueur qui rejoint : il ouvre la connexion vers l'hôte.
    IEnumerator Offer(string target)
    {
        int s = session;
        string id = "dc_" + RandomId();
        var c = NewConn(target, id);
        SetupChannel(c, c.pc.CreateDataChannel(id, new RTCDataChannelInit { ordered = true }));
        var op = c.pc.CreateOffer();
        yield return op;
        if (s != session || c.closed || op.IsError) { Fail(c, s); yield break; }
        var desc = op.Desc;
        var set = c.pc.SetLocalDescription(ref desc);
        yield return set;
        if (s != session || c.closed || set.IsError) { Fail(c, s); yield break; }
        Log("offre envoyée à " + target);
        SendServer("{\"type\":\"OFFER\",\"payload\":{\"sdp\":{\"type\":\"offer\",\"sdp\":" + MiniJson.Quote(desc.sdp) + "},\"type\":\"data\",\"connectionId\":"
            + MiniJson.Quote(id) + ",\"label\":" + MiniJson.Quote(id) + ",\"serialization\":\"json\",\"reliable\":true,\"browser\":\"unity\"},\"dst\":"
            + MiniJson.Quote(target) + "}");
    }

    // Hôte : un joueur (navigateur ou .exe) demande à se connecter.
    IEnumerator Answer(string from, Dictionary<string, object> payload)
    {
        int s = session;
        string id = MiniJson.Str(payload, "connectionId");
        var sdp = MiniJson.Obj(payload, "sdp");
        if (id == null || sdp == null || byConnId.ContainsKey(id)) yield break;
        string label = MiniJson.Str(payload, "label") ?? id;
        var c = NewConn(from, id);
        c.pc.OnDataChannel = ch => { if (ch.Label == label && c.dc == null) SetupChannel(c, ch); };
        yield return SetRemote(c, RTCSdpType.Offer, MiniJson.Str(sdp, "sdp"));
        if (s != session || c.closed || !c.remoteSet) yield break;
        var op = c.pc.CreateAnswer();
        yield return op;
        if (s != session || c.closed || op.IsError) { Fail(c, s); yield break; }
        var desc = op.Desc;
        var set = c.pc.SetLocalDescription(ref desc);
        yield return set;
        if (s != session || c.closed || set.IsError) { Fail(c, s); yield break; }
        SendServer("{\"type\":\"ANSWER\",\"payload\":{\"sdp\":{\"type\":\"answer\",\"sdp\":" + MiniJson.Quote(desc.sdp) + "},\"type\":\"data\",\"connectionId\":"
            + MiniJson.Quote(id) + ",\"browser\":\"unity\"},\"dst\":" + MiniJson.Quote(from) + "}");
    }

    IEnumerator SetRemote(Conn c, RTCSdpType type, string sdp)
    {
        if (c.closed || string.IsNullOrEmpty(sdp)) yield break;
        var desc = new RTCSessionDescription { type = type, sdp = sdp };
        var op = c.pc.SetRemoteDescription(ref desc);
        yield return op;
        if (c.closed) yield break;
        if (op.IsError) { CloseConn(c, true); yield break; }
        c.remoteSet = true;
        foreach (var init in c.pendingCandidates) AddCandidate(c, init);
        c.pendingCandidates.Clear();
    }

    static void AddCandidate(Conn c, RTCIceCandidateInit init)
    {
        try { c.pc.AddIceCandidate(new RTCIceCandidate(init)); }
        catch (Exception e) { Debug.LogWarning("NativePeer : candidat ICE refusé (" + e.Message + ")"); }
    }

    void Fail(Conn c, int s)
    {
        if (s != session) return;
        CloseConn(c, true);
    }

    // ---------- Données ----------

    void Receive(string from, string data)
    {
        recvMsgs++;
        recvBytes += data.Length;
        if (isHost && data.Length > 1 && data[0] == 'S' && data[1] == '|')
            foreach (var kv in conns)
                if (kv.Key != from) SendFastTo(kv.Value, data);
        Emit("OnNetData", from + "\n" + data);
    }

    void SendReliable(Conn c, string msg)
    {
        if (c.closed || c.dc == null || c.dc.ReadyState != RTCDataChannelState.Open) return;
        Count(msg);
        try { c.dc.Send(Encoding.UTF8.GetBytes(MiniJson.Quote(msg))); }
        catch (Exception e) { Debug.LogWarning("NativePeer : envoi impossible (" + e.Message + ")"); }
    }

    // Non fiable si le canal rapide est ouvert, sinon par la connexion fiable.
    void SendFastTo(Conn c, string msg)
    {
        if (c.closed) return;
        if (c.fast != null && c.fast.ReadyState == RTCDataChannelState.Open)
        {
            Count(msg);
            try { c.fast.Send(msg); return; } catch { }
        }
        SendReliable(c, msg);
    }

    void Count(string msg)
    {
        sentMsgs++;
        sentBytes += msg.Length;
        if (msg.Length > largest) largest = msg.Length;
    }

    IEnumerator CloseLater(Conn c, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        CloseConn(c, false);
    }

    void CloseConn(Conn c, bool notify)
    {
        if (c == null || c.closed) return;
        c.closed = true;
        byConnId.Remove(c.id);
        bool wasOpen = c.opened && conns.TryGetValue(c.peer, out var cur) && cur == c;
        if (wasOpen) conns.Remove(c.peer);
        // Fermeture à l'image suivante : on peut être appelé depuis un rappel de cette même connexion.
        if (isActiveAndEnabled) StartCoroutine(DisposeLater(c));
        else DisposeConn(c);
        if (notify && wasOpen) Emit("OnPeerDisconnected", c.peer);
    }

    IEnumerator DisposeLater(Conn c)
    {
        yield return null;
        DisposeConn(c);
    }

    static void DisposeConn(Conn c)
    {
        try { c.dc?.Close(); } catch { }
        try { c.fast?.Close(); } catch { }
        try { c.pc?.Close(); } catch { }
        try { c.dc?.Dispose(); } catch { }
        try { c.fast?.Dispose(); } catch { }
        try { c.pc?.Dispose(); } catch { }
    }
}
#endif
