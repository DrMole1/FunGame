using System.Runtime.InteropServices;
using UnityEngine;

// Écran de la Télévision (GDD 7.6, V0.4.7). Par défaut, sans signal : neige de pixels gris et noirs qui clignotent et
// ondulent, avec un grésillement spatialisé à moins de 10 m. Reliée à une Balise chargée et un lien YouTube saisi, elle
// affiche un décompte (3 à 0), puis la vidéo, dont le son s'entend à moins de 10 m. Le signal perdu, la neige revient.
// En WebGL, la vidéo est un lecteur YouTube posé par-dessus le canvas sur les coins de l'écran (YouTubeTv.jslib) ;
// dans l'éditeur, l'écran l'annonce simplement.
public class TvScreen : MonoBehaviour
{
    const int NoiseW = 48, NoiseH = 36;
    const float NoiseRefresh = 0.05f, OverlayRange = 30f;

    Transform screen;
    Material screenMat;
    Texture2D noise;
    Color32[] pixels;
    TextMesh label;
    AudioSource staticLoop;
    float noiseTimer, wave;
    bool signal;
    string videoId = "";
    float countdownStart;   // Time.time du début du décompte
    int overlay;            // lecteur YouTube (0 : aucun)
    System.Random rnd;

    public bool HasVideo => videoId.Length > 0;
    public bool Playing => signal && HasVideo && Time.time - countdownStart >= GameRules.TvCountdown;
    public Vector3 ScreenCenter => screen.position;

    // L'écran occupe la face avant (+Z) du poste, au format 4:3, d'après l'encombrement du modèle. Sur le modèle Meshy,
    // la vitre est sur la gauche (vue de face : vers +X), le panneau de boutons à droite.
    public static TvScreen Create(Transform root, Transform visual, float height, bool meshy)
    {
        Bounds b = GameArt.LocalBounds(root);
        var tv = root.gameObject.AddComponent<TvScreen>();
        tv.rnd = new System.Random(root.GetInstanceID());
        float w = b.size.x * 0.66f, h = Mathf.Min(w * 0.75f, b.size.y * 0.42f);
        w = h / 0.75f;
        Vector3 center = new Vector3(b.center.x, b.min.y + b.size.y * 0.45f, b.max.z + 0.004f);
        if (meshy)
        {
            // Vitre mesurée sur un rendu de face du modèle : 54 % de la largeur, 53 % de la hauteur.
            w = b.size.x * 0.54f;
            h = b.size.y * 0.53f;
            center = new Vector3(b.max.x - b.size.x * 0.42f, b.min.y + b.size.y * 0.64f, b.max.z + 0.004f);
        }
        tv.screen = GameArt.Prim(PrimitiveType.Quad, root, center, new Vector3(w, h, 1f), null).transform;
        tv.screen.name = "Ecran";
        // Le Quad est visible depuis -Z : on le retourne vers l'avant.
        tv.screen.localRotation = Quaternion.Euler(0f, 180f, 0f);
        tv.noise = new Texture2D(NoiseW, NoiseH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tv.pixels = new Color32[NoiseW * NoiseH];
        tv.screenMat = new Material(GameArt.Emissive("TvScreenBase", new Color(0.05f, 0.05f, 0.05f), Color.white)) { name = "EcranTele" };
        tv.screenMat.mainTexture = tv.noise;
        tv.screenMat.SetTexture("_EmissionMap", tv.noise);
        var r = tv.screen.GetComponent<Renderer>();
        r.sharedMaterial = tv.screenMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tv.label = GameArt.CreateText(root, "Decompte", tv.screen.localPosition + Vector3.forward * 0.004f, h * 0.1f, Color.white);
        tv.label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        tv.label.gameObject.SetActive(false);
        tv.staticLoop = GameAudio.Loop3D("tv_static", root, tv.screen.localPosition, 0.45f, GameRules.TvSoundRange);
        if (tv.staticLoop != null) tv.staticLoop.Play();
        tv.DrawNoise();
        return tv;
    }

    public void SetSignal(bool on)
    {
        if (on == signal) return;
        signal = on;
        Refresh();
    }

    // Vidéo reçue de l'hôte : 'elapsed' secondes depuis le début du décompte ("" : plus de vidéo).
    public void SetVideo(string id, float elapsed)
    {
        id = id ?? "";
        if (id == videoId && Mathf.Abs(Time.time - elapsed - countdownStart) < 1f) return;
        CloseOverlay();
        videoId = id;
        countdownStart = Time.time - elapsed;
        Refresh();
    }

    void Refresh()
    {
        bool video = signal && HasVideo;
        if (staticLoop != null) staticLoop.mute = video;
        if (!video) CloseOverlay();
        label.gameObject.SetActive(video);
    }

    void Update()
    {
        if (signal && HasVideo)
        {
            float t = Time.time - countdownStart;
            if (t < GameRules.TvCountdown)
            {
                // Décompte : 3, 2, 1, 0, une seconde chacun, sur fond noir.
                string digit = Mathf.Clamp(Mathf.FloorToInt(GameRules.TvCountdown - t), 0, 3).ToString();
                // Un bip d'amorce de film à chaque chiffre (y compris le premier).
                if (label.text != digit) GameAudio.Play3D("tv_countdown", screen.position, 0.55f, GameRules.TvSoundRange, 0f);
                label.text = digit;
                FillScreen(new Color32(0, 0, 0, 255));
                return;
            }
            if (overlay == 0 && YouTubeTv.Available) overlay = YouTubeTv.Create(videoId, t - GameRules.TvCountdown);
            label.text = YouTubeTv.Available ? "" : "YouTube\n<size=30>" + videoId + "</size>";
            FillScreen(new Color32(8, 8, 12, 255));
            return;
        }
        // Neige : seulement quand quelqu'un peut la voir.
        Camera cam = Camera.main;
        if (cam == null || (cam.transform.position - screen.position).sqrMagnitude > OverlayRange * OverlayRange) return;
        noiseTimer -= Time.deltaTime;
        if (noiseTimer > 0f) return;
        noiseTimer = NoiseRefresh;
        DrawNoise();
    }

    // Pixels gris et noirs au hasard, une bande plus claire qui défile et des lignes qui ondulent.
    void DrawNoise()
    {
        wave += 0.35f;
        for (int y = 0; y < NoiseH; y++)
        {
            float band = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(y * 0.08f - wave * 0.2f, 1.6f) - 0.8f) * 3f);
            int shift = Mathf.RoundToInt(Mathf.Sin(y * 0.45f + wave) * 2f);
            for (int x = 0; x < NoiseW; x++)
            {
                double n = rnd.NextDouble();
                byte v = (byte)(n < 0.45 ? 10 + rnd.Next(30) : 90 + rnd.Next(140));
                v = (byte)Mathf.Min(255, v + band * 60f);
                pixels[y * NoiseW + Mathf.Abs((x + shift) % NoiseW)] = new Color32(v, v, v, 255);
            }
        }
        noise.SetPixels32(pixels);
        noise.Apply(false);
    }

    void FillScreen(Color32 c)
    {
        if (pixels[0].Equals(c) && pixels[pixels.Length - 1].Equals(c)) return;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        noise.SetPixels32(pixels);
        noise.Apply(false);
    }

    // Après la caméra : le lecteur YouTube suit les coins de l'écran, ou se cache.
    void LateUpdate()
    {
        if (overlay == 0) return;
        Camera cam = Camera.main;
        var c = new Vector3[4];
        bool visible = cam != null && OrbitCamera.Looking && !TerminalUI.IsOpen && !PostItUI.IsOpen;
        if (visible)
        {
            Vector3 eye = cam.transform.position, center = screen.position;
            Vector3 front = -screen.forward;
            visible = Vector3.Dot(eye - center, front) > 0f && (eye - center).magnitude < OverlayRange
                && !Physics.Linecast(eye, center + (eye - center).normalized * 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // Haut-gauche, haut-droit, bas-droit, bas-gauche, vus de face.
            Vector3[] local = { new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, -0.5f, 0f) };
            for (int i = 0; i < 4 && visible; i++)
            {
                Vector3 v = cam.WorldToViewportPoint(screen.TransformPoint(local[i]));
                if (v.z < 0.05f) visible = false;
                c[i] = new Vector3(v.x, 1f - v.y, 0f);
            }
        }
        // Le son de la vidéo s'entend à moins de 10 m du personnage (pas de la caméra, placée derrière lui), fort presque
        // jusqu'à la limite.
        Vector3 ear = cam != null ? cam.transform.position : screen.position;
        var orbit = cam != null ? cam.GetComponent<OrbitCamera>() : null;
        if (orbit != null && orbit.target != null) ear = orbit.target.position;
        float d = Mathf.Clamp01(Vector3.Distance(ear, screen.position) / GameRules.TvSoundRange);
        float volume = 100f * (1f - d * d) * GameAudio.SfxVolume * 2f;
        YouTubeTv.Update(overlay, visible, c, volume);
    }

    void CloseOverlay()
    {
        if (overlay != 0) YouTubeTv.Destroy(overlay);
        overlay = 0;
    }

    void OnDestroy()
    {
        CloseOverlay();
        if (noise != null) Destroy(noise);
        if (screenMat != null) Destroy(screenMat);
    }
}

// Appels vers YouTubeTv.jslib (navigateur seulement).
public static class YouTubeTv
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern int YouTubeTv_Create(string videoId, float start);
    [DllImport("__Internal")] static extern void YouTubeTv_Update(int id, int visible, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float volume);
    [DllImport("__Internal")] static extern void YouTubeTv_Destroy(int id);
    [DllImport("__Internal")] static extern void YouTubeTv_ListenPaste(string objectName);

    public const bool Available = true;
    public static int Create(string videoId, float start) => YouTubeTv_Create(videoId, start);
    public static void Update(int id, bool visible, Vector3[] c, float volume) =>
        YouTubeTv_Update(id, visible ? 1 : 0, c[0].x, c[0].y, c[1].x, c[1].y, c[2].x, c[2].y, c[3].x, c[3].y, volume);
    public static void Destroy(int id) => YouTubeTv_Destroy(id);
    public static void ListenPaste(string objectName) => YouTubeTv_ListenPaste(objectName);
#else
    public const bool Available = false;
    public static int Create(string videoId, float start) => 0;
    public static void Update(int id, bool visible, Vector3[] c, float volume) { }
    public static void Destroy(int id) { }
    public static void ListenPaste(string objectName) { }
#endif

    static readonly System.Text.RegularExpressions.Regex Link = new System.Text.RegularExpressions.Regex(
        @"(?:youtube(?:-nocookie)?\.com/(?:watch\?(?:[^#]*&)?v=|embed/|shorts/|live/|v/)|youtu\.be/)([A-Za-z0-9_-]{11})",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Identifiant de la vidéo d'un lien YouTube, ou null si le lien ne mène pas à YouTube.
    public static string VideoId(string url)
    {
        var m = Link.Match(url ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }
}

// Ctrl+V dans le navigateur : le texte collé est inséré dans la zone de saisie active (lien de la Télévision, Terminal,
// Post-It), Unity WebGL n'ayant pas accès au presse-papiers.
public class PasteReceiver : MonoBehaviour
{
    const string ObjectName = "PasteReceiver";

    public static void Ensure()
    {
        if (!YouTubeTv.Available || GameObject.Find(ObjectName) != null) return;
        var go = new GameObject(ObjectName);
        DontDestroyOnLoad(go);
        go.AddComponent<PasteReceiver>();
        YouTubeTv.ListenPaste(ObjectName);
    }

    void OnPaste(string text)
    {
        var selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
        var field = selected != null ? selected.GetComponent<UnityEngine.UI.InputField>() : null;
        if (field == null || !field.isFocused) return;
        text = text.Replace("\r", "").Replace("\n", " ");
        int a = Mathf.Min(field.selectionAnchorPosition, field.selectionFocusPosition);
        int b = Mathf.Max(field.selectionAnchorPosition, field.selectionFocusPosition);
        string current = field.text;
        a = Mathf.Clamp(a, 0, current.Length);
        b = Mathf.Clamp(b, a, current.Length);
        string result = current.Substring(0, a) + text + current.Substring(b);
        if (field.characterLimit > 0 && result.Length > field.characterLimit) result = result.Substring(0, field.characterLimit);
        field.text = result;
        field.caretPosition = field.selectionAnchorPosition = field.selectionFocusPosition = Mathf.Min(result.Length, a + text.Length);
    }
}
