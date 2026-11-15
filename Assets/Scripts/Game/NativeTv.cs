#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;

// V0.7.6 (14.1) : la Télévision dans le build Windows. Le lecteur YouTube de la version navigateur n'existe pas hors du
// navigateur : la vidéo (360p, avec le son) est téléchargée par yt-dlp et réunie par ffmpeg (outils libres posés à côté du
// .exe ; dans l'éditeur : Tools/), puis lue par un VideoPlayer sur l'écran du poste. Le son s'atténue avec la distance comme
// dans la version navigateur.
public static class NativeTv
{
    const int TimeoutMs = 180000;   // téléchargement compris
    static readonly Dictionary<string, (string url, float at)> cache = new Dictionary<string, (string, float)>();
    static readonly ConcurrentQueue<(string id, string url)> results = new ConcurrentQueue<(string, string)>();
    static readonly HashSet<string> pending = new HashSet<string>();
    const float CacheLifetime = 3600f;   // les adresses YouTube expirent au bout de quelques heures

    public static string ToolPath
    {
        get
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#if UNITY_EDITOR
            return Path.Combine(root, "Tools", "yt-dlp.exe");
#else
            return Path.Combine(root, "yt-dlp.exe");
#endif
        }
    }

    public static bool Available => File.Exists(ToolPath) && File.Exists(Path.Combine(Path.GetDirectoryName(ToolPath), "ffmpeg.exe"));

    // Adresse directe de la vidéo : null tant qu'elle n'est pas connue, "" si elle est introuvable.
    public static string Resolve(string id)
    {
        while (results.TryDequeue(out var r))
        {
            pending.Remove(r.id);
            cache[r.id] = (r.url, Time.realtimeSinceStartup);
        }
        if (cache.TryGetValue(id, out var c) && (c.url.Length == 0 || Time.realtimeSinceStartup - c.at < CacheLifetime)) return c.url;
        if (!Available) return "";
        if (pending.Add(id))
        {
            string tool = ToolPath;
            Task.Run(() => results.Enqueue((id, Run(tool, id))));
        }
        return null;
    }

    // Une vidéo introuvable peut être réessayée (nouveau lien saisi, connexion revenue).
    public static void Forget(string id)
    {
        if (cache.TryGetValue(id, out var c) && c.url.Length == 0) cache.Remove(id);
    }

    // Dossier des vidéos déjà téléchargées (gardées le temps de la session de jeu, effacées au lancement suivant).
    public static string CacheDir;

    // YouTube ne fournit plus l'image et le son ensemble, et Unity ne lit ni un son AAC seul ni les adresses directes des
    // clients mobiles (refusées) : yt-dlp télécharge l'image (MP4 H.264, 360p au plus) et le son (M4A), ffmpeg (posé à côté)
    // les réunit en un MP4 local, lu ensuite par le VideoPlayer. Retourne le chemin du fichier, ou "" en cas d'échec.
    static string Run(string tool, string id)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            string file = Path.Combine(CacheDir, id + ".mp4");
            if (File.Exists(file) && new FileInfo(file).Length > 0) return file;
            var info = new ProcessStartInfo
            {
                FileName = tool,
                Arguments = "-f \"bv*[ext=mp4][vcodec^=avc1][height<=360]+ba[ext=m4a]/b[ext=mp4][vcodec^=avc1][acodec!=none]/bv*[ext=mp4][vcodec^=avc1]+ba[ext=m4a]\" "
                    + "--merge-output-format mp4 --ffmpeg-location \"" + Path.GetDirectoryName(tool) + "\" --max-filesize 300M "
                    + "--no-playlist --no-warnings --quiet --no-progress -o \"" + file + "\" \"https://www.youtube.com/watch?v=" + id + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(info))
            {
                p.StandardOutput.ReadToEndAsync();
                var errors = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(TimeoutMs)) { try { p.Kill(); } catch { } return ""; }
                if (File.Exists(file)) return file;
                UnityEngine.Debug.LogWarning("Télévision : vidéo " + id + " introuvable (" + errors.Result.Trim() + ")");
            }
        }
        catch (Exception e) { UnityEngine.Debug.LogWarning("Télévision : yt-dlp indisponible (" + e.Message + ")"); }
        return "";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void PrepareCache()
    {
        CacheDir = Path.Combine(Application.temporaryCachePath, "Television");
        try { if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true); } catch { }
    }

    // Lecteur posé sur l'écran : l'image va dans une RenderTexture, le son dans une source réglée par TvScreen.
    // 'urls' : une adresse (image et son) ou deux (image, puis son), séparées par un saut de ligne.
    public static NativeTvPlayer CreatePlayer(GameObject host, string urls, Func<double> startNow, out RenderTexture target, out AudioSource audio)
    {
        target = new RenderTexture(854, 480, 0) { name = "VideoTele" };
        audio = host.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;
        var player = host.AddComponent<NativeTvPlayer>();
        player.Init(urls.Split('\n'), target, audio, startNow);
        return player;
    }
}

// Lecture d'une vidéo YouTube (une ou deux sources) : les deux lecteurs démarrent ensemble au bon moment, et le son est
// recalé sur l'image s'il dérive.
public class NativeTvPlayer : MonoBehaviour
{
    VideoPlayer video, sound;
    Func<double> startNow;
    bool started, soundFailed;
    float checkAt;

    public bool Ready => started;

    public void Init(string[] urls, RenderTexture target, AudioSource audio, Func<double> start)
    {
        startNow = start;
        bool split = urls.Length >= 2;
        video = NewPlayer(urls[0]);
        video.renderMode = VideoRenderMode.RenderTexture;
        video.targetTexture = target;
        if (split)
        {
            video.audioOutputMode = VideoAudioOutputMode.None;
            sound = NewPlayer(urls[1]);
            sound.renderMode = VideoRenderMode.APIOnly;
            sound.errorReceived += (vp, message) => soundFailed = true;
            Route(sound, audio);
        }
        else Route(video, audio);
        video.Prepare();
        if (sound != null) sound.Prepare();
    }

    VideoPlayer NewPlayer(string url)
    {
        var p = gameObject.AddComponent<VideoPlayer>();
        p.playOnAwake = false;
        p.isLooping = false;
        p.source = VideoSource.Url;
        p.url = url;
        p.skipOnDrop = true;
        p.errorReceived += (vp, message) => UnityEngine.Debug.LogWarning("Télévision : " + message);
        return p;
    }

    static void Route(VideoPlayer p, AudioSource audio)
    {
        p.audioOutputMode = VideoAudioOutputMode.AudioSource;
        p.controlledAudioTrackCount = 1;
        p.EnableAudioTrack(0, true);
        p.SetTargetAudioSource(0, audio);
    }

    void Update()
    {
        if (!started)
        {
            // Un son seul (M4A) n'est pas lisible partout par le VideoPlayer : en erreur, l'image part sans lui.
            if (soundFailed && sound != null) { Destroy(sound); sound = null; }
            if (!video.isPrepared || (sound != null && !sound.isPrepared)) return;
            started = true;
            // Même moment de la vidéo chez tous les joueurs (décompte compris).
            double start = startNow();
            if (start > 0.5 && video.length > 0 && start < video.length)
            {
                video.time = start;
                if (sound != null) sound.time = start;
            }
            video.Play();
            if (sound != null) sound.Play();
            return;
        }
        if (sound == null || Time.unscaledTime < checkAt) return;
        checkAt = Time.unscaledTime + 1f;
        if (video.isPlaying && Math.Abs(sound.time - video.time) > 0.25) sound.time = video.time;
    }

    void OnDestroy()
    {
        if (video != null) Destroy(video);
        if (sound != null) Destroy(sound);
    }
}
#endif
