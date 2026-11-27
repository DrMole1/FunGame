using System.Collections.Generic;
using UnityEngine;

// V0.7.19 (11.4) : objets vivants du Blockhaus.

// Pommeau de douche au-dessus d'une baignoire : un clic ouvre ou coupe l'eau (aucun texte d'aide, aucun contour). L'état est tenu
// par l'hôte (message SW, puis SH à tous) ; chaque joueur montre l'eau et les bulles de savon de son côté.
// V0.7.20 (Note 11) : cliquable depuis la baignoire ou à côté (il est en hauteur : portée de 3,5 m) ; l'eau sort des trous du pommeau
// (la face du disque du modèle Douche, tournée vers l'avant et un peu vers le bas) en fins filets blancs qui retombent en arc.
// V0.7.21 (Note 15) : contour blanc quand on le vise, de près, sous tous les angles ; l'eau naît sur la face percée et s'arrête
// dans la baignoire (collisions des gouttes).
public class ShowerHead : Interactable
{
    public static readonly List<ShowerHead> All = new List<ShowerHead>();
    public int Id { get; private set; }
    public bool On { get; private set; }
    Vector3 nozzle;     // centre de la face percée du pommeau (monde)
    Vector3 sprayDir;   // vers où regarde la face percée (monde)
    float roseRadius, floorY;
    Vector3 wetCenter;  // milieu de la zone arrosée, au sol
    ParticleSystem water;
    AudioSource sound;

    // Face percée du pommeau dans le repère du modèle Douche ramené à 1 (sa plus grande dimension), le mur derrière (-Z) : centre,
    // direction et rayon de la zone des trous, mesurés sur le maillage.
    static readonly Vector3 RoseCenter = new Vector3(-0.008f, 0.457f, 0.396f), RoseNormal = new Vector3(-0.028f, -0.438f, 0.899f);
    // V0.7.21 (Note 15) : l'eau naît sur la face percée elle-même (avant : 6,4 % de la taille du modèle devant elle, à côté du pommeau).
    const float RoseFaceOffset = 0.008f, RoseHoles = 0.15f;
    // V0.7.21 (Note 15) : zone de sélection élargie autour du pommeau, pour le viser sous tous les angles.
    const float TriggerPadding = 0.35f;
    const float SprayRadius = 0.6f, JetSpeed = 1.3f;

    public override string Prompt => "";
    // V0.7.21 (Note 15) : contour blanc quand on le vise (avant : aucun contour).
    protected override bool OutlineVisible => true;
    public override float Range => 3.5f;

    public static ShowerHead Attach(GameObject head, int id, Vector3 outward, float floorY)
    {
        var shower = head.AddComponent<ShowerHead>();
        shower.Id = id;
        shower.floorY = floorY;
        Bounds b = GameArt.LocalBounds(head.transform);
        float s = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        // Le modèle est posé au sol de son support (SpawnModelFloor) et centré : son repère est celui de la mesure, à l'échelle s.
        Vector3 local = new Vector3(b.center.x, b.min.y, b.center.z) + new Vector3(RoseCenter.x, RoseCenter.y, RoseCenter.z) * s
            + RoseNormal.normalized * (RoseFaceOffset * s);
        shower.nozzle = head.transform.TransformPoint(local);
        shower.sprayDir = head.transform.TransformDirection(RoseNormal.normalized);
        shower.roseRadius = RoseHoles * s;
        // Où retombe l'eau : chute depuis le pommeau, lancée à JetSpeed le long de sa face.
        float vy = shower.sprayDir.y * JetSpeed, drop = Mathf.Max(0.3f, shower.nozzle.y - floorY);
        float t = (vy + Mathf.Sqrt(vy * vy + 2f * 9.81f * drop)) / 9.81f;
        Vector3 flat = new Vector3(shower.sprayDir.x, 0f, shower.sprayDir.z) * JetSpeed;
        shower.wetCenter = shower.nozzle + flat * (t * 0.5f);
        Bounds hb = GameArt.LocalBounds(head.transform);
        shower.FitTrigger(Vector3.Max(new Vector3(0.5f, 0.5f, 0.5f), hb.size + Vector3.one * (TriggerPadding * 2f)));
        All.Add(shower);
        return shower;
    }

    void OnDestroy() => All.Remove(this);

    public override void Interact() => GameSession.Instance?.ToggleShower(this);

    public void SetOn(bool on)
    {
        if (on == On) return;
        On = on;
        if (on)
        {
            if (water == null) water = BuildWater();
            water.Play();
            sound = GameAudio.Loop3D("shower", transform, transform.InverseTransformPoint(nozzle), 0.5f, 14f);
            if (sound != null) sound.Play();
        }
        else
        {
            if (water != null) water.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (sound != null) { GameAudio.ReleaseLoop(sound); sound = null; }
        }
    }

    // Un point (le corps d'un personnage) est sous l'eau.
    public bool Wets(Vector3 p)
    {
        if (!On || p.y < floorY - 0.2f || p.y > nozzle.y + 0.2f) return false;
        return new Vector2(p.x - wetCenter.x, p.z - wetCenter.z).magnitude < SprayRadius;
    }

    // Filets d'eau : de fines gouttes étirées dans le sens de leur vitesse (des traits), blanches, lancées de toute la face percée
    // et retombant sous la gravité jusqu'au sol (ou au fond de la baignoire).
    ParticleSystem BuildWater()
    {
        var go = new GameObject("Eau");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(nozzle, Quaternion.LookRotation(sprayDir));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startSpeed = new ParticleSystem.MinMaxCurve(JetSpeed * 0.9f, JetSpeed * 1.1f);
        main.gravityModifier = 1f;
        float vy = sprayDir.y * JetSpeed, drop = Mathf.Max(0.3f, nozzle.y - floorY);
        main.startLifetime = (vy + Mathf.Sqrt(vy * vy + 2f * 9.81f * drop)) / 9.81f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.94f, 0.96f, 0.55f), new Color(1f, 1f, 1f, 0.85f));
        main.maxParticles = 900;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 520f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = roseRadius;
        shape.radiusThickness = 1f;
        // Elles s'effacent juste avant de toucher le fond.
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        // V0.7.21 (Note 15) : l'eau s'arrête sur ce qu'elle touche (le fond et les bords de la baignoire) au lieu de la traverser.
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Medium;
        collision.bounce = 0f;
        collision.dampen = 1f;
        collision.lifetimeLoss = 1f;
        collision.radiusScale = 0.5f;
        collision.collidesWith = Physics.DefaultRaycastLayers;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.09f;
        r.lengthScale = 2f;
        r.sharedMaterial = GameArt.Spark;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }
}

// Bulles de savon autour d'un personnage passé sous une douche : elles naissent tant qu'il est sous l'eau, encore 4 s après, puis
// s'arrêtent progressivement.
public class SoapBubbles : MonoBehaviour
{
    const float Rate = 14f, Linger = 4f, FadeOut = 1.5f;
    ParticleSystem ps;
    float dryAt = -100f;
    bool wet;

    public static SoapBubbles On(Transform body)
    {
        var b = body.GetComponent<SoapBubbles>();
        return b != null ? b : body.gameObject.AddComponent<SoapBubbles>();
    }

    public void SetWet(bool on)
    {
        if (on == wet) return;
        wet = on;
        if (!on) dryAt = Time.time;
        else if (ps == null) ps = Build();
    }

    void Update()
    {
        if (ps == null) return;
        float since = Time.time - dryAt;
        float rate = wet ? Rate : since < Linger ? Rate : Rate * Mathf.Clamp01(1f - (since - Linger) / FadeOut);
        var emission = ps.emission;
        emission.rateOverTime = rate;
        if (rate <= 0f && ps.particleCount == 0) { Destroy(ps.gameObject); ps = null; }
    }

    ParticleSystem Build()
    {
        var go = new GameObject("BullesDeSavon");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var p = go.AddComponent<ParticleSystem>();
        p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = p.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.95f, 1f, 0.8f), Color.white);
        main.gravityModifier = -0.02f;
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = p.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.45f;
        var fade = p.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GameArt.Spark;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        p.Play();
        return p;
    }
}
