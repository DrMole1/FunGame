using System.Collections.Generic;
using UnityEngine;

// Boule de Disco (V0.7.2, O11) : arc-en-ciel et brillante, trouvée dans les Super Monticules. Posée au sol ou au plafond,
// elle tourne lentement et projette des lueurs aux couleurs de l'arc-en-ciel. La musique disco qu'elle diffuse est gérée
// par GameSession (une seule à la fois).
// V0.7.3 (Note 06) : sortie d'un Super Monticule, elle reste éteinte (ni lumière, ni musique, ni rotation) ; elle ne s'active
// que lorsqu'un joueur la pose. Ses lumières sont des lumières ponctuelles qui tournent avec elle (pas de projecteurs, dont
// le premier rendu figeait le jeu), créées seulement à l'activation.
public class DiscoBall : MonoBehaviour
{
    static readonly Color[] Rainbow =
    {
        new Color(1f, 0.25f, 0.25f), new Color(1f, 0.65f, 0.15f), new Color(1f, 0.95f, 0.3f),
        new Color(0.3f, 1f, 0.4f), new Color(0.3f, 0.6f, 1f), new Color(0.75f, 0.35f, 1f),
    };
    const float SpotIntensity = 1.6f, HueSpeed = 0.35f, LightOrbit = 0.9f, LightRange = 5f;

    Transform ball, center;
    bool hanging;
    readonly List<Light> spots = new List<Light>();

    public bool Active { get; private set; }

    // 'ball' tourne sur lui-même ; les lueurs tournent autour de 'center'. Éteinte tant qu'elle n'est pas activée.
    public static DiscoBall Attach(GameObject device, Transform ball, Transform center, bool hanging)
    {
        var d = device.AddComponent<DiscoBall>();
        d.ball = ball;
        d.center = center;
        d.hanging = hanging;
        d.enabled = false;
        return d;
    }

    public void Activate()
    {
        if (Active || center == null) return;
        Active = true;
        enabled = true;
        for (int k = 0; k < 3; k++)
        {
            var spot = new GameObject("Lueur" + k).AddComponent<Light>();
            spot.transform.SetParent(center, false);
            spot.type = LightType.Point;
            spot.range = LightRange;
            spot.intensity = SpotIntensity;
            spot.shadows = LightShadows.None;
            // Autour de la boule, un peu vers le bas au plafond, un peu vers le haut au sol.
            float a = k * Mathf.PI * 2f / 3f;
            spot.transform.localPosition = new Vector3(Mathf.Cos(a) * LightOrbit, hanging ? -0.4f : 0.4f, Mathf.Sin(a) * LightOrbit);
            spots.Add(spot);
        }
    }

    void Update()
    {
        if (ball == null || !Active) return;
        ball.Rotate(0f, 35f * Time.deltaTime, 0f, Space.World);
        float k = 0.75f + 0.25f * Mathf.Sin(Time.time * 5f);
        for (int i = 0; i < spots.Count; i++)
        {
            // Chaque faisceau parcourt l'arc-en-ciel, décalé des autres.
            float t = Mathf.Repeat(Time.time * HueSpeed + i / (float)spots.Count, 1f) * Rainbow.Length;
            int a = Mathf.FloorToInt(t) % Rainbow.Length;
            spots[i].color = Color.Lerp(Rainbow[a], Rainbow[(a + 1) % Rainbow.Length], t - Mathf.Floor(t));
            spots[i].intensity = SpotIntensity * k;
        }
    }
}

// Un canard ou une peluche, près d'une Boule de Disco posée, danse au rythme de sa musique : il se balance et son volume
// bat la mesure. Tout est local : rien ne passe par le réseau.
public class DiscoDancer : MonoBehaviour
{
    public const float Bpm = 118f;
    const float SwayDegrees = 14f, PulseAmount = 0.16f, FadeSpeed = 4f, CheckEvery = 0.3f;
    // Boules posées et temps de la musique (mis à jour à chaque image par la session).
    public static readonly List<Vector3> Balls = new List<Vector3>();
    public static float MusicTime;

    Vector3 baseScale;
    Quaternion baseRotation;
    float amount, checkTimer;
    bool near;

    // Sur le modèle (pivot à sa base), pas sur le visuel qu'anime déjà le flottement ou la pose.
    public static void Attach(Transform visual)
    {
        if (visual.childCount > 0) visual.GetChild(0).gameObject.AddComponent<DiscoDancer>();
    }

    void Start()
    {
        baseScale = transform.localScale;
        baseRotation = transform.localRotation;
        checkTimer = Random.value * CheckEvery;
    }

    void Update()
    {
        checkTimer -= Time.deltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = CheckEvery;
            near = false;
            float range = GameRules.DiscoMusicRange * GameRules.DiscoMusicRange;
            foreach (var ball in Balls)
                if ((ball - transform.position).sqrMagnitude < range) { near = true; break; }
        }
        if (!near && amount <= 0f) return;
        amount = Mathf.MoveTowards(amount, near ? 1f : 0f, Time.deltaTime * FadeSpeed);
        float beat = MusicTime * Bpm / 60f * Mathf.PI;
        transform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, Mathf.Sin(beat) * SwayDegrees * amount);
        transform.localScale = baseScale * (1f + PulseAmount * amount * Mathf.Pow(Mathf.Abs(Mathf.Cos(beat)), 4f));
    }
}

public partial class GameSession
{
    // Musique disco : une seule à la fois, depuis la boule la plus proche (à moins de 10 m) ; elle remplace la musique
    // d'ambiance.
    AudioSource discoMusic;
    Beacon discoSource;

    void UpdateDisco()
    {
        Vector3 me = player.transform.position;
        Beacon nearest = null;
        float best = GameRules.DiscoMusicRange;
        // Sans musique à proximité, les objets dansent quand même en mesure, sur l'horloge du jeu.
        DiscoDancer.MusicTime = discoMusic != null && discoMusic.isPlaying ? discoMusic.time : Time.time;
        DiscoDancer.Balls.Clear();
        foreach (var b in beacons.Placed)
        {
            if (b == null || !b.DiscoActive) continue;
            DiscoDancer.Balls.Add(b.DiscoCenter);
            float d = Vector3.Distance(b.DiscoCenter, me);
            if (d < best) { best = d; nearest = b; }
        }
        GameAudio.DiscoNear = nearest != null && net.InGame;
        if (nearest == discoSource && (discoMusic != null || nearest == null)) return;
        if (discoMusic != null) Destroy(discoMusic.gameObject);
        discoMusic = null;
        discoSource = nearest;
        if (nearest == null) return;
        discoMusic = GameAudio.Loop3D("disco_music", nearest.transform, nearest.DiscoCenter - nearest.transform.position, 0.6f, GameRules.DiscoMusicRange, true);
        if (discoMusic != null) discoMusic.Play();
    }
}
