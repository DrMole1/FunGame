using System.Collections.Generic;
using UnityEngine;

// Petits effets visuels jetables : gerbes de débris (monticules, Canon), explosion du Réveil, pouf de cirque du Fou et
// disparition en douceur.
public static class Fx
{
    static Mesh cube;
    static Mesh Cube => cube != null ? cube : (cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"));

    static Mesh sphere, cylinder;
    static Mesh Sphere => sphere != null ? sphere : (sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx"));
    static Mesh Cylinder => cylinder != null ? cylinder : (cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"));

    // Gerbe de petits cubes éclairés (terre, éclats) projetés vers le haut et autour, qui retombent.
    public static ParticleSystem Burst(Vector3 position, Material material, int count, float speed, float size, float lifetime = 1.1f, float gravity = 1.6f)
    {
        var ps = Emitter("Debris", position, material, Cube, count, lifetime, speed, size, gravity);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 50f;
        shape.radius = 0.2f;
        ps.Play();
        return ps;
    }

    // Système jetable d'une seule bouffée de 'count' particules (orienté vers le haut ; mesh null : sprites face caméra),
    // à régler puis lancer (Play). Elles rétrécissent sur la fin de leur vie.
    static ParticleSystem Emitter(string name, Vector3 position, Material material, Mesh mesh, int count, float lifetime, float speed, float size, float gravity)
    {
        var go = new GameObject(name);
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
        main.startRotation3D = mesh != null;
        if (mesh != null) main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        else main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = gravity;
        main.maxParticles = count;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        SizeCurve(ps, AnimationCurve.Linear(0.7f, 1f, 1f, 0f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
        if (mesh != null) r.mesh = mesh;
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static AnimationCurve Curve(params float[] timeValue)
    {
        var keys = new Keyframe[timeValue.Length / 2];
        for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(timeValue[2 * i], timeValue[2 * i + 1]);
        var c = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) c.SmoothTangents(i, 0f);
        return c;
    }

    static void SizeCurve(ParticleSystem ps, AnimationCurve curve)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    // Couleur au fil de la vie : couleurs réparties régulièrement, opacité de 1 à 0 sur la fin.
    static void ColorFade(ParticleSystem ps, float fadeFrom, params Color[] colors)
    {
        var keys = new GradientColorKey[colors.Length];
        for (int i = 0; i < colors.Length; i++) keys[i] = new GradientColorKey(colors[i], colors.Length == 1 ? 0f : (float)i / (colors.Length - 1));
        var g = new Gradient();
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeFrom), new GradientAlphaKey(0f, 1f) });
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = g;
    }

    // Freinage (l'air retient les particules lancées vite).
    static void Drag(ParticleSystem ps, float dampen)
    {
        var lv = ps.limitVelocityOverLifetime;
        lv.enabled = true;
        lv.limit = 0f;
        lv.dampen = dampen;
    }

    static void Tumble(ParticleSystem ps, float speed)
    {
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-speed, speed);
    }

    static void Bounce(ParticleSystem ps)
    {
        var col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.dampen = 0.45f;
        col.bounce = 0.35f;
        col.lifetimeLoss = 0.1f;
        col.radiusScale = 0.5f;
        col.quality = ParticleSystemCollisionQuality.Medium;
    }

    // ---------- Explosion réaliste (Le Réveil) ----------

    // Éclair de lumière, boule de feu en volutes (shader FunGame/Explosion) qui gonfle, monte et retombe en fumée
    // noire, braises, éclats qui rebondissent, panache de fumée, poussière soulevée au ras du sol et trace de brûlure.
    public static void Explosion(Vector3 at, Vector3 ground, Material shrapnel)
    {
        Flash(at, new Color(1f, 0.62f, 0.28f), 10f, 7f, 0.45f);

        // Boule de feu : grosses volutes serrées qui s'ouvrent vite puis ralentissent ; leurs bords se rongent et leur
        // cœur passe du blanc-jaune au rouge, puis à la fumée.
        // (Émises vers le haut et un peu au-dessus du sol : les sprites ne s'enfoncent pas dans le plancher.)
        var fire = Volumes("BouleDeFeu", at + Vector3.up * 0.3f, GameArt.ExplosionFire, 16, 1.4f, 4.5f, 2f, -0.35f, 0.4f);
        var shape = fire.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        SizeCurve(fire, Curve(0f, 0.3f, 0.15f, 0.9f, 1f, 1.45f));
        Drag(fire, 0.32f);
        fire.Play();
        // Flash du cœur, très bref.
        var core = Volumes("CoeurDuFeu", at, GameArt.ExplosionFire, 5, 0.35f, 1.2f, 1.8f, 0f, 0.15f);
        SizeCurve(core, Curve(0f, 0.6f, 0.4f, 1.1f, 1f, 1.3f));
        core.Play();

        var embers = Emitter("Braises", at, GameArt.Spark, null, 45, 1.3f, 12f, 0.12f, 1.2f);
        var er = embers.GetComponent<ParticleSystemRenderer>();
        er.renderMode = ParticleSystemRenderMode.Stretch;
        er.velocityScale = 0.035f;
        er.lengthScale = 1.5f;
        ColorFade(embers, 0.5f, new Color(1f, 0.85f, 0.4f), new Color(1f, 0.4f, 0.05f), new Color(0.5f, 0.08f, 0f));
        Drag(embers, 0.08f);
        embers.Play();

        var shards = Emitter("Eclats", at, shrapnel, Cube, 22, 2.2f, 7f, 0.09f, 1.8f);
        Tumble(shards, 15f);
        Bounce(shards);
        shards.Play();

        // Panache de fumée : prend le relais du feu, monte lentement en s'élargissant, puis se dissipe en se rongeant.
        var smoke = Volumes("Fumee", at + Vector3.up * 0.8f, GameArt.ExplosionSmoke, 16, 4.5f, 3f, 2.2f, -0.3f, 0.6f);
        var main = smoke.main;
        main.startDelay = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        shape = smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        SizeCurve(smoke, Curve(0f, 0.4f, 0.4f, 1.3f, 1f, 2.3f));
        Drag(smoke, 0.18f);
        smoke.Play();

        // Poussière soulevée : anneau de volutes brunes qui fuse au ras du sol.
        var dust = Volumes("Poussiere", ground + Vector3.up * 0.35f, GameArt.ExplosionDust, 18, 1.6f, 6.5f, 1f, -0.02f, 0.3f);
        shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        SizeCurve(dust, Curve(0f, 0.3f, 0.4f, 1f, 1f, 1.5f));
        Drag(dust, 0.22f);
        dust.Play();

        Scorch(ground, 1.3f);

        // Joueur local pris tout près : les oreilles sifflent (à moins de 4 m, d'autant plus qu'il est proche).
        PlayerPresence me = GameSession.Instance != null ? GameSession.Instance.LocalPresence : null;
        if (me != null && me.Alive)
        {
            float d = Vector3.Distance(me.Position, at);
            if (d < DeafenRange) GameAudio.Deafen(1f - d / DeafenRange);
        }
    }

    const float DeafenRange = 4f;

    static readonly List<ParticleSystemVertexStream> VolumeStreams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX,
    };

    // Volutes du shader d'explosion : sprites face caméra tournés au hasard, qui reçoivent leur âge et un aléa stable
    // (TEXCOORD0.zw) ; rendus du plus loin au plus proche pour que la fumée se superpose correctement.
    static ParticleSystem Volumes(string name, Vector3 at, Material material, int count, float lifetime, float speed, float size, float gravity, float radius)
    {
        var ps = Emitter(name, at, material, null, count, lifetime, speed, size, gravity);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.75f, lifetime);
        var shape = ps.shape;
        shape.radius = radius;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.SetActiveVertexStreams(VolumeStreams);
        r.sortMode = ParticleSystemSortMode.Distance;
        return ps;
    }

    // Lumière ponctuelle qui s'allume d'un coup puis s'éteint en vacillant.
    public static void Flash(Vector3 at, Color color, float range, float intensity, float duration)
    {
        var go = new GameObject("Eclair");
        go.transform.position = at;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        var f = go.AddComponent<Flicker>();
        f.lamp = light;
        f.intensity = intensity;
        f.duration = duration;
    }

    class Flicker : MonoBehaviour
    {
        public Light lamp;
        public float intensity, duration;
        float t;

        void Update()
        {
            t += Time.deltaTime;
            float k = t / duration;
            lamp.intensity = intensity * Mathf.Max(0f, 1f - k) * (1f - k) * Random.Range(0.75f, 1f);
            if (k >= 1f) Destroy(gameObject);
        }
    }

    // Trace de brûlure noire au sol : reste quelques secondes, puis se résorbe.
    static void Scorch(Vector3 ground, float diameter)
    {
        var go = new GameObject("Brulure");
        go.transform.position = ground + Vector3.up * 0.012f;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.transform.localScale = new Vector3(diameter, 0.005f, diameter * Random.Range(0.8f, 1f));
        go.AddComponent<MeshFilter>().sharedMesh = Cylinder;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = GameArt.Lit("Brulure", new Color(0.03f, 0.025f, 0.02f), null, 0f);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var d = go.AddComponent<Delayed>();
        d.at = Time.time + 8f;
        d.run = () => Vanish(go, 2f);
        d.keep = true;
    }

    // ---------- Pouf de cirque (Le Fou) ----------

    // Nuage blanc tout rond qui gonfle d'un coup (rebond) puis se résorbe, confettis aux couleurs du chapiteau et
    // étoiles dorées qui scintillent ; « pouf » et sifflet à coulisse. 'surprise' : une colombe s'en échappe, ou un
    // chapeau haut-de-forme de magicien en jaillit.
    public enum Surprise { None, Dove, Hat }

    public static void CircusPoof(Vector3 ground, Surprise surprise = Surprise.None)
    {
        Vector3 at = ground + Vector3.up * 0.8f;
        GameAudio.Play3D("fou_poof", at, 0.8f, 25f, 0.08f);
        if (surprise == Surprise.Dove) Dove.Spawn(at);
        else if (surprise == Surprise.Hat) MagicHat.Spawn(at, ground.y);

        var cloud = Emitter("NuageCartoon", at, GameArt.Lit("NuageCartoon", new Color(0.97f, 0.96f, 0.94f), null, 0f), Sphere, 16, 0.8f, 2.6f, 0.6f, -0.05f);
        var main = cloud.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.8f);
        var shape = cloud.shape;
        shape.radius = 0.55f;
        SizeCurve(cloud, Curve(0f, 0f, 0.12f, 1.3f, 0.24f, 0.95f, 0.34f, 1.05f, 0.7f, 1f, 1f, 0f));
        Drag(cloud, 0.35f);
        cloud.Play();

        Confetti(at);

        var stars = Emitter("Etoiles", at, GameArt.Spark, null, 14, 1f, 4.5f, 0.6f, 0.4f);
        shape = stars.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        SizeCurve(stars, Curve(0f, 0.2f, 0.15f, 1f, 0.3f, 0.5f, 0.45f, 1f, 0.6f, 0.45f, 0.75f, 0.9f, 1f, 0f));
        ColorFade(stars, 0.7f, new Color(1f, 0.9f, 0.3f), new Color(1f, 0.75f, 0.15f));
        Drag(stars, 0.15f);
        stars.Play();
    }

    // Joueur transporté par Le Fou : seulement les confettis (ni nuage, ni étoiles), avec le « pouf ».
    public static void CircusConfetti(Vector3 ground)
    {
        Vector3 at = ground + Vector3.up * 0.8f;
        GameAudio.Play3D("fou_poof", at, 0.8f, 25f, 0.08f);
        Confetti(at);
    }

    static void Confetti(Vector3 at)
    {
        Color[] circus = { new Color(0.9f, 0.1f, 0.12f), new Color(1f, 0.85f, 0.1f), new Color(0.15f, 0.35f, 0.95f),
                           new Color(0.15f, 0.75f, 0.3f), new Color(0.95f, 0.3f, 0.75f), Color.white };
        foreach (Color c in circus)
        {
            var confetti = Emitter("Confettis", at, GameArt.Emissive("Confetti" + c, c, c * 0.5f), Cube, 9, 2f, 5.5f, 1f, 0.35f);
            var main = confetti.main;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.12f, 0.17f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.015f, 0.025f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.08f, 0.12f);
            var shape = confetti.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.3f;
            Drag(confetti, 0.12f);
            Tumble(confetti, 12f);
            confetti.Play();
        }
    }

    // Pièce d'un petit modèle fait de formes simples (sans collision).
    static Transform Part(Transform parent, Mesh mesh, Vector3 localPos, Vector3 scale, Material mat, Vector3 euler = default)
    {
        var go = new GameObject(mesh.name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // Colombe blanche qui jaillit du nuage en battant des ailes et s'envole en biais, puis disparaît.
    class Dove : MonoBehaviour
    {
        Transform wingL, wingR;
        Vector3 dir;
        float t, phase;

        public static void Spawn(Vector3 at)
        {
            var go = new GameObject("Colombe");
            go.transform.position = at;
            var d = go.AddComponent<Dove>();
            Vector2 h = Random.insideUnitCircle.normalized;
            d.dir = new Vector3(h.x, 0f, h.y);
            go.transform.rotation = Quaternion.LookRotation(d.dir);
            Material white = GameArt.Lit("Colombe", new Color(0.97f, 0.97f, 0.97f), null, 0.3f);
            Part(go.transform, Sphere, Vector3.zero, new Vector3(0.16f, 0.15f, 0.3f), white);
            Part(go.transform, Sphere, new Vector3(0f, 0.08f, 0.15f), Vector3.one * 0.12f, white);
            Part(go.transform, Cube, new Vector3(0f, 0.07f, 0.23f), new Vector3(0.03f, 0.03f, 0.06f), GameArt.Lit("BecColombe", new Color(1f, 0.6f, 0.15f)), new Vector3(10f, 0f, 0f));
            Part(go.transform, Cube, new Vector3(0f, 0.02f, -0.19f), new Vector3(0.12f, 0.02f, 0.12f), white, new Vector3(-15f, 0f, 0f));
            d.wingL = new GameObject("AileG").transform;
            d.wingL.SetParent(go.transform, false);
            d.wingL.localPosition = new Vector3(-0.06f, 0.05f, 0f);
            Part(d.wingL, Cube, new Vector3(-0.15f, 0f, 0f), new Vector3(0.3f, 0.02f, 0.16f), white);
            d.wingR = new GameObject("AileD").transform;
            d.wingR.SetParent(go.transform, false);
            d.wingR.localPosition = new Vector3(0.06f, 0.05f, 0f);
            Part(d.wingR, Cube, new Vector3(0.15f, 0f, 0f), new Vector3(0.3f, 0.02f, 0.16f), white);
        }

        void Update()
        {
            t += Time.deltaTime;
            phase += Time.deltaTime * 26f;
            float flap = Mathf.Sin(phase) * 55f;
            wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
            wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
            // Monte vite au début, puis file en légère ondulation.
            float climb = Mathf.Lerp(3f, 1.2f, Mathf.Clamp01(t / 1f));
            transform.position += (dir * 2.6f + Vector3.up * climb + Vector3.up * Mathf.Sin(phase * 0.5f) * 0.3f) * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(dir + Vector3.up * climb * 0.15f);
            if (t > 3f)
            {
                Vanish(gameObject, 0.4f);
                enabled = false;
            }
        }
    }

    // Chapeau haut-de-forme noir à ruban rouge qui jaillit en tournoyant, retombe, rebondit, puis se résorbe.
    class MagicHat : MonoBehaviour
    {
        Vector3 velocity, spin;
        float groundY, t;

        public static void Spawn(Vector3 at, float groundY)
        {
            var go = new GameObject("ChapeauMagicien");
            go.transform.position = at;
            var hat = go.AddComponent<MagicHat>();
            hat.groundY = groundY;
            Vector2 h = Random.insideUnitCircle * 1.2f;
            hat.velocity = new Vector3(h.x, 5f, h.y);
            hat.spin = new Vector3(Random.Range(-500f, 500f), Random.Range(-300f, 300f), Random.Range(-500f, 500f));
            Material black = GameArt.Lit("ChapeauNoir", new Color(0.05f, 0.05f, 0.06f), null, 0.5f);
            Part(go.transform, Cylinder, new Vector3(0f, 0.13f, 0f), new Vector3(0.26f, 0.13f, 0.26f), black);
            Part(go.transform, Cylinder, Vector3.zero, new Vector3(0.42f, 0.008f, 0.42f), black);
            Part(go.transform, Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(0.265f, 0.03f, 0.265f), GameArt.Lit("RubanRouge", new Color(0.85f, 0.08f, 0.1f), null, 0.4f));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            velocity += Physics.gravity * dt;
            Vector3 p = transform.position + velocity * dt;
            transform.Rotate(spin * dt, Space.World);
            if (p.y < groundY + 0.01f)
            {
                p.y = groundY + 0.01f;
                if (velocity.y < -1f)
                {
                    // Rebond : il perd de sa vitesse et se redresse peu à peu.
                    velocity = new Vector3(velocity.x * 0.5f, -velocity.y * 0.35f, velocity.z * 0.5f);
                    spin *= 0.35f;
                }
                else
                {
                    velocity = Vector3.zero;
                    spin = Vector3.zero;
                }
            }
            if (spin == Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), 1f - Mathf.Exp(-10f * dt));
            transform.position = p;
            if (t > 4f)
            {
                Vanish(gameObject, 0.5f);
                enabled = false;
            }
        }
    }

    // Confettis sous un personnage, après un court délai (le temps que sa nouvelle position arrive).
    public static void CircusConfettiUnder(PlayerPresence who, float delay)
    {
        if (who == null) return;
        var go = new GameObject("ConfettisDifferes");
        var d = go.AddComponent<Delayed>();
        d.at = Time.time + delay;
        d.run = () => { if (who != null) CircusConfetti(who.Position + Vector3.down * 0.9f); };
    }

    public static Material Dirt => GameArt.Lit("DebrisTerre", new Color(0.3f, 0.2f, 0.12f), null, 0.05f);
    class Delayed : MonoBehaviour
    {
        public float at;
        public System.Action run;
        public bool keep;   // l'action prend en charge l'objet (pas détruit ici)

        void Update()
        {
            if (Time.time < at) return;
            run?.Invoke();
            if (keep) Destroy(this);
            else Destroy(gameObject);
        }
    }

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
