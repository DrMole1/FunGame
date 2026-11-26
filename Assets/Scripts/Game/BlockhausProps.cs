using System.Collections.Generic;
using UnityEngine;

// V0.7.19 (11.4) : objets vivants du Blockhaus.

// Pommeau de douche au-dessus d'une baignoire : un clic ouvre ou coupe l'eau (aucun texte d'aide, aucun contour). L'état est tenu
// par l'hôte (message SW, puis SH à tous) ; chaque joueur montre l'eau et les bulles de savon de son côté.
public class ShowerHead : Interactable
{
    public static readonly List<ShowerHead> All = new List<ShowerHead>();
    public int Id { get; private set; }
    public bool On { get; private set; }
    Vector3 nozzle;     // sortie de l'eau (monde)
    float floorY;
    ParticleSystem water;
    AudioSource sound;

    // Colonne d'eau : sous le pommeau, un peu devant le mur, jusqu'au sol.
    const float SprayRadius = 0.6f, SprayReach = 0.25f;

    public override string Prompt => "";
    protected override bool OutlineVisible => false;

    public static ShowerHead Attach(GameObject head, int id, Vector3 outward, float floorY)
    {
        var shower = head.AddComponent<ShowerHead>();
        shower.Id = id;
        shower.floorY = floorY;
        Bounds b = GameArt.LocalBounds(head.transform);
        Vector3 center = head.transform.TransformPoint(b.center);
        shower.nozzle = new Vector3(center.x, head.transform.TransformPoint(new Vector3(0f, b.min.y, 0f)).y, center.z) + outward * SprayReach;
        shower.FitTrigger(new Vector3(0.5f, 0.5f, 0.5f));
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
        return new Vector2(p.x - nozzle.x, p.z - nozzle.z).magnitude < SprayRadius;
    }

    ParticleSystem BuildWater()
    {
        var go = new GameObject("Eau");
        go.transform.SetParent(transform, false);
        go.transform.position = nozzle;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        float drop = Mathf.Max(0.5f, nozzle.y - floorY);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3f);
        main.gravityModifier = 0.6f;
        main.startLifetime = drop / 3.2f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.8f, 0.85f, 0.6f), new Color(1f, 1f, 1f, 0.9f));
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 160f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 11f;
        shape.radius = 0.08f;
        var r = go.GetComponent<ParticleSystemRenderer>();
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

// Fuite au plafond : une goutte tombe toutes les 2 s dans une petite flaque luisante, avec un "ploc". Purement visuelle (chez
// chaque joueur, rien sur le réseau).
public class WaterLeak : MonoBehaviour
{
    const float Period = 2f;
    Vector3 top, bottom;
    Transform drop;
    float phase, fall;

    public static WaterLeak Create(Transform parent, Vector3 ceiling, Vector3 floor, float phase)
    {
        var go = new GameObject("Fuite");
        go.transform.SetParent(parent, false);
        go.transform.position = floor;
        var leak = go.AddComponent<WaterLeak>();
        leak.top = ceiling + Vector3.down * 0.03f;
        leak.bottom = floor;
        leak.phase = phase;
        // Chute libre : t = sqrt(2h / g).
        leak.fall = Mathf.Sqrt(2f * Mathf.Max(0.5f, ceiling.y - floor.y) / 9.81f);
        // Flaque : un disque plat, sombre et très brillant.
        var puddle = GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.004f, 0f), new Vector3(0.7f, 0.002f, 0.55f), Puddle);
        puddle.transform.localRotation = Quaternion.Euler(0f, phase * 97f, 0f);
        foreach (var c in puddle.GetComponentsInChildren<Collider>()) Destroy(c);
        // Tache humide au plafond.
        var stain = GameArt.Prim(PrimitiveType.Cylinder, go.transform, go.transform.InverseTransformPoint(ceiling) + Vector3.down * 0.006f, new Vector3(0.45f, 0.002f, 0.4f), Puddle);
        foreach (var c in stain.GetComponentsInChildren<Collider>()) Destroy(c);
        leak.drop = GameArt.Prim(PrimitiveType.Sphere, go.transform, Vector3.zero, new Vector3(0.035f, 0.05f, 0.035f), Puddle).transform;
        foreach (var c in leak.drop.GetComponentsInChildren<Collider>()) Destroy(c);
        return leak;
    }

    static Material puddle;
    static Material Puddle
    {
        get
        {
            if (puddle != null) return puddle;
            puddle = GameArt.Lit("FlaqueLuisante", new Color(0.12f, 0.13f, 0.15f, 1f), null, 0.97f);
            return puddle;
        }
    }

    int lastCycle = -1;

    void Update()
    {
        float t = Time.time + phase;
        int cycle = Mathf.FloorToInt(t / Period);
        float u = t - cycle * Period;
        if (u < fall)
        {
            drop.gameObject.SetActive(true);
            float k = u / fall;
            drop.position = Vector3.Lerp(top, bottom, k * k);
        }
        else
        {
            drop.gameObject.SetActive(false);
            if (cycle != lastCycle)
            {
                lastCycle = cycle;
                if (GameAudio.InRange(bottom, 12f)) GameAudio.Play3D("ploc", bottom, 0.55f, 12f, 0.12f);
            }
        }
    }
}
