using UnityEngine;

// Écran de la Télévision (GDD 7.6, V0.4.7) : neige de pixels gris et noirs qui clignotent et ondulent, avec un
// grésillement spatialisé à moins de 10 m. V0.7.8 : plus de vidéo YouTube ni de liaison aux Balises (contraintes
// légales) : la Télévision est un objet de décor.
public class TvScreen : MonoBehaviour
{
    const int NoiseW = 48, NoiseH = 36;
    const float NoiseRefresh = 0.05f, NoiseRange = 30f;

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
        tv.staticLoop = GameAudio.Loop3D("tv_static", root, tv.screen.localPosition, 0.45f, GameRules.TvSoundRange);
        if (tv.staticLoop != null) tv.staticLoop.Play();
        tv.DrawNoise();
        return tv;
    }

    void Update()
    {
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
        if (noise != null) Destroy(noise);
        if (screenMat != null) Destroy(screenMat);
    }
}
