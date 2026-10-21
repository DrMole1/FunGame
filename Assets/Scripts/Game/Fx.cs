using UnityEngine;

// Petits effets visuels jetables : gerbes de débris (monticules, Canon) et disparition en douceur.
public static class Fx
{
    static Mesh cube;
    static Mesh Cube => cube != null ? cube : (cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"));

    // Gerbe de petits cubes éclairés (terre, éclats) projetés vers le haut et autour, qui retombent.
    public static ParticleSystem Burst(Vector3 position, Material material, int count, float speed, float size, float lifetime = 1.1f)
    {
        var go = new GameObject("Debris");
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startRotation3D = true;
        main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 1.6f;
        main.maxParticles = count;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 50f;
        shape.radius = 0.2f;
        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0.7f, 1f, 1f, 0f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = Cube;
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    public static Material Dirt => GameArt.Lit("DebrisTerre", new Color(0.3f, 0.2f, 0.12f), null, 0.05f);

    // L'objet rétrécit puis disparaît (sans son).
    public static void Vanish(GameObject go, float duration = 0.45f)
    {
        if (go == null) return;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        go.AddComponent<Vanisher>().duration = duration;
    }

    class Vanisher : MonoBehaviour
    {
        public float duration;
        float t;
        Vector3 scale;

        void Start() => scale = transform.localScale;

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            // Un léger gonflement, puis il se résorbe.
            transform.localScale = scale * (k < 0.25f ? 1f + k * 0.6f : 1.15f * (1f - Mathf.SmoothStep(0f, 1f, (k - 0.25f) / 0.75f)));
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
