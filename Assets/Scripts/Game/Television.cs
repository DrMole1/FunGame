using System.Collections.Generic;
using UnityEngine;

// Écran de la Télévision (GDD 7.6, V0.4.7) : neige de pixels gris et noirs qui clignotent et ondulent, avec un
// grésillement spatialisé à moins de 10 m. V0.7.8 : plus de vidéo YouTube ni de liaison aux Balises (contraintes
// légales) : la Télévision est un objet de décor.
// V0.7.19 (7.4) : une Télévision en vision directe d'une Balise est chargée (BeaconNetwork) : plus de neige ni de grésillement,
// l'écran montre une cloche qui balance (Assets/Sprite/Cloche_Balance, via Resources/TvArt). Chaque balancement est compté
// (Swings) : un joueur face à l'écran (GameSession) en est obnubilé et entend la cloche.
// V0.7.24 (17.4, S3) : une Cassette lue (PlayCassette) l'interrompt : écran noir, ni neige, ni grésillement, ni cloche (aucun tintement
// compté) ; l'enregistrement se diffuse à 10 m, en français ou en anglais selon la langue du joueur. 50 s plus tard, elle reprend son
// cours (neige et grésillement, ou la cloche si elle est chargée).
public class TvScreen : MonoBehaviour
{
    const int NoiseW = 48, NoiseH = 36;
    const float NoiseRefresh = 0.05f, NoiseRange = 30f;
    // V0.7.19 : l'animation (20 images : la cloche part d'un côté, revient, part de l'autre, revient) ; elle tinte une
    // fois par tour. V0.7.20 (Note 01) : deux fois plus rapide (1,2 s au lieu de 2,4 s). V0.7.22 (Note 04) : le tintement tombe au
    // moment où le battant percute le côté gauche de la cloche (au plus loin de son élan vers la droite, image 15 ; avant : image 6).
    const float BellFps = 20f / 1.2f;
    const int StrikeFrame = 14;

    public static readonly List<TvScreen> All = new List<TvScreen>();
    public bool Charged { get; private set; }
    // Nombre de tintements depuis que la Télévision est chargée.
    public int Swings { get; private set; }
    public Vector3 ScreenPoint => screen.position;
    // L'écran regarde vers l'avant du poste.
    public Vector3 ScreenNormal => transform.forward;
    float chargedAt;
    int lastFrame = -1;
    float cassetteUntil = -1f;
    AudioSource cassetteVoice;
    Texture2D blank;
    public bool PlayingCassette => cassetteUntil > 0f;

    Transform screen;
    Material screenMat;
    Texture2D noise;
    Color32[] pixels;
    AudioSource staticLoop;
    float noiseTimer, wave;
    System.Random rnd;

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
            // Vitre (sans son liseré sombre) mesurée au pixel sur un rendu de face du modèle : 53,8 % de la largeur,
            // 48,4 % de la hauteur, centrée à 42,7 % du bord droit et 63,2 % de la hauteur. La vitre bombée affleure à
            // 8 mm sous l'avant du poste : l'écran se pose juste devant son sommet.
            w = b.size.x * 0.538f;
            h = b.size.y * 0.484f;
            center = new Vector3(b.max.x - b.size.x * 0.427f, b.min.y + b.size.y * 0.632f, b.max.z - 0.004f);
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
        // V0.7.23 (Note 02) : grésillement un peu moins fort (0,45 -> 0,36).
        tv.staticLoop = GameAudio.Loop3D("tv_static", root, tv.screen.localPosition, 0.36f, GameRules.TvSoundRange);
        if (tv.staticLoop != null) tv.staticLoop.Play();
        tv.DrawNoise();
        All.Add(tv);
        return tv;
    }

    // V0.7.19 : reliée (ou non) à une Balise.
    public void SetCharged(bool on)
    {
        if (on == Charged) return;
        Charged = on;
        Swings = 0;
        lastFrame = -1;
        chargedAt = Time.time;
        if (PlayingCassette) return;
        if (staticLoop != null) { if (on) staticLoop.Stop(); else staticLoop.Play(); }
        if (on) ShowBellFrame(0);
        else
        {
            screenMat.mainTexture = noise;
            screenMat.SetTexture("_EmissionMap", noise);
            DrawNoise();
        }
    }

    public void PlayCassette()
    {
        cassetteUntil = Time.time + GameRules.CassetteDuration;
        if (staticLoop != null) staticLoop.Stop();
        if (blank == null)
        {
            blank = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            blank.SetPixel(0, 0, Color.black);
            blank.Apply(false);
        }
        screenMat.mainTexture = blank;
        screenMat.SetTexture("_EmissionMap", blank);
        if (cassetteVoice != null) Destroy(cassetteVoice.gameObject);
        cassetteVoice = GameAudio.Loop3D(Loc.Current == Lang.Fr ? "cassette_fr" : "cassette_en", transform, screen.localPosition, 1f, GameRules.CassetteRange);
        if (cassetteVoice != null)
        {
            cassetteVoice.loop = false;
            cassetteVoice.Play();
        }
    }

    void EndCassette()
    {
        cassetteUntil = -1f;
        if (cassetteVoice != null) Destroy(cassetteVoice.gameObject);
        cassetteVoice = null;
        // Reprise : la cloche depuis le début si elle est chargée, sinon la neige et son grésillement.
        Swings = 0;
        lastFrame = -1;
        chargedAt = Time.time;
        if (Charged) ShowBellFrame(0);
        else
        {
            if (staticLoop != null) staticLoop.Play();
            screenMat.mainTexture = noise;
            screenMat.SetTexture("_EmissionMap", noise);
            DrawNoise();
        }
    }

    void ShowBellFrame(int frame)
    {
        var art = TvArt.Get();
        if (art == null || art.bellFrames == null || art.bellFrames.Length == 0) return;
        Texture2D tex = art.bellFrames[frame % art.bellFrames.Length];
        screenMat.mainTexture = tex;
        screenMat.SetTexture("_EmissionMap", tex);
    }

    void Update()
    {
        if (PlayingCassette)
        {
            if (Time.time >= cassetteUntil) EndCassette();
            else return;
        }
        if (Charged)
        {
            var art = TvArt.Get();
            int count = art != null && art.bellFrames != null && art.bellFrames.Length > 0 ? art.bellFrames.Length : 20;
            int step = Mathf.FloorToInt((Time.time - chargedAt) * BellFps);
            int frame = step % count;
            if (step != lastFrame)
            {
                if (frame == StrikeFrame) Swings++;
                lastFrame = step;
                ShowBellFrame(frame);
            }
            return;
        }
        // Neige : seulement quand quelqu'un peut la voir.
        Camera cam = Camera.main;
        if (cam == null || (cam.transform.position - screen.position).sqrMagnitude > NoiseRange * NoiseRange) return;
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

    void OnDestroy()
    {
        All.Remove(this);
        if (noise != null) Destroy(noise);
        if (blank != null) Destroy(blank);
        if (screenMat != null) Destroy(screenMat);
    }
}
