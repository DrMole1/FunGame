using System.Runtime.InteropServices;
using UnityEngine;

// Appels vers le plugin PeerNet.jslib. Les réponses reviennent par SendMessage
// sur le GameObject "NetworkManager" (voir NetworkGame).
// Hors navigateur (éditeur), seul l'hébergement en solo est simulé.
public static class PeerNet
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void PeerNet_Host(string id);
    [DllImport("__Internal")] static extern void PeerNet_Join(string hostId);
    [DllImport("__Internal")] static extern void PeerNet_Send(string to, string msg);
    [DllImport("__Internal")] static extern void PeerNet_Broadcast(string msg);
    [DllImport("__Internal")] static extern void PeerNet_Leave();
    [DllImport("__Internal")] static extern void PeerNet_CopyText(string text);
    [DllImport("__Internal")] static extern void PeerNet_Kick(string id);
    [DllImport("__Internal")] static extern int PeerNet_TimezoneOffset();

    public const bool Available = true;
    public static void Host(string id) => PeerNet_Host(id);
    public static void Join(string hostId) => PeerNet_Join(hostId);
    public static void Send(string to, string msg) => PeerNet_Send(to, msg);
    public static void Broadcast(string msg) => PeerNet_Broadcast(msg);
    public static void Leave() => PeerNet_Leave();
    public static void CopyText(string text) => PeerNet_CopyText(text);
    public static void Kick(string id) => PeerNet_Kick(id);
    // Heure locale du navigateur (DateTime.Now renvoie l'heure UTC en WebGL).
    public static System.DateTime LocalNow => System.DateTime.UtcNow.AddMinutes(-PeerNet_TimezoneOffset());
#else
    public const bool Available = false;
    public static void Host(string id)
    {
        var go = GameObject.Find(NetworkGame.ObjectName);
        if (go != null) go.SendMessage("OnNetOpen", id);
    }
    public static void Join(string hostId)
    {
        var go = GameObject.Find(NetworkGame.ObjectName);
        if (go != null) go.SendMessage("OnNetError", "editor");
    }
    public static void Send(string to, string msg) { }
    public static void Broadcast(string msg) { }
    public static void Leave() { }
    public static void CopyText(string text) => GUIUtility.systemCopyBuffer = text;
    public static void Kick(string id) { }
    public static System.DateTime LocalNow => System.DateTime.Now;
#endif
}
