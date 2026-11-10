using System.Collections.Generic;
using UnityEngine;

// Pont suspendu (GDD 10.3, V0.6.6) entre deux plateformes d'une très haute salle : parcelles, bâtons et cordes (modèles Meshy) au
// damier noir et blanc. Les parcelles ploient sous les pas et, dès qu'un joueur est dessus, tout le tablier se balance de gauche à
// droite en entraînant bâtons, cordes et personnages (V0.6.9 ; calcul local, sans réseau). Trois joueurs ou plus dessus : il casse
// (décidé par l'hôte) ; parcelles et cordes tombent et disparaissent 2 secondes après l'impact. Hors du bâti : les entités
// ne le connaissent pas.
public class SuspendedBridge : MonoBehaviour
{
    const float Width = 1.6f, PlankSpacing = 0.58f, PostSpacing = 3f, PostInset = 0.74f, RopeHeight = 1.15f, ColliderSegment = 1.5f;
    // Balancement : amplitude au milieu du pont (m), inclinaison des parcelles et des bâtons (degrés), vitesse, temps de mise en route.
    const float SwayAmplitude = 0.14f, SwayRoll = 4f, SwayLean = 5f, SwaySpeed = 2.6f, SwayFade = 0.8f;
    const float Bend = 0.13f, BendReach = 1.3f, SettleSpeed = 6f, MaxSagRatio = 0.05f, MaxSag = 1.1f, FallSafety = 10f;

    public int Id { get; private set; }
    public bool Broken { get; private set; }

    Vector3 start, end, side, flatDir;
    float flatLength, sag;
    Transform[] planks;
    Vector3[] rest;
    Quaternion[] restRotation;
    readonly List<Post> posts = new List<Post>();
    readonly List<(Transform rope, Post a, Post b)> ropes = new List<(Transform, Post, Post)>();
    float swayAmount;
    float[] offset, targets;
    readonly List<Transform> falling = new List<Transform>();
    readonly List<GameObject> solids = new List<GameObject>();
    bool moving;

    class Post { public Transform transform; public Vector3 foot; public float bow; public Vector3 Top; }

    static Material plankA, plankB;

    static void LoadMaterials()
    {
        if (plankA != null) return;
        plankA = GameArt.CheckerWall;
        plankB = new Material(plankA) { name = "ParcelleInverse", mainTextureOffset = new Vector2(0f, 0.5f) };
    }

    Vector3 Curve(float t) => Vector3.Lerp(start, end, t) + Vector3.down * (sag * 4f * t * (1f - t));

    public static SuspendedBridge Create(Transform parent, int id, Vector3 a, Vector3 b)
    {
        LoadMaterials();
        var go = new GameObject("PontSuspendu_" + id);
        go.transform.SetParent(parent, false);
        var bridge = go.AddComponent<SuspendedBridge>();
        bridge.Id = id;
        bridge.Build(a, b);
        return bridge;
    }

    void Build(Vector3 a, Vector3 b)
    {
        start = a;
        end = b;
        Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
        flatLength = flat.magnitude;
        flatDir = flat / flatLength;
        side = Vector3.Cross(Vector3.up, flatDir);
        sag = Mathf.Min(MaxSag, flatLength * MaxSagRatio);

        Mesh plankMesh = Resources.Load<Mesh>("Art/Decor/PontParcelle"), postMesh = Resources.Load<Mesh>("Art/Decor/PontBaton"), ropeMesh = Resources.Load<Mesh>("Art/Decor/PontCorde");

        int count = Mathf.Max(2, Mathf.RoundToInt(flatLength / PlankSpacing));
        planks = new Transform[count];
        rest = new Vector3[count];
        restRotation = new Quaternion[count];
        offset = new float[count];
        targets = new float[count];
        for (int k = 0; k < count; k++)
        {
            float t = (k + 0.5f) / count;
            Vector3 p = Curve(t);
            Vector3 tangent = (Curve(t + 0.01f) - Curve(t - 0.01f)).normalized;
            var plank = GameArt.MeshObject("Parcelle", transform, plankMesh, k % 2 == 0 ? plankA : plankB);
            plank.transform.SetPositionAndRotation(p + Vector3.down * 0.03f, Quaternion.LookRotation(tangent));
            Shadowless(plank);
            planks[k] = plank.transform;
            rest[k] = plank.transform.position;
            restRotation[k] = plank.transform.rotation;
            falling.Add(plank.transform);
        }

        // Une rangée de bâtons de chaque côté, reliés par une corde. Ceux des deux extrémités, sur les plateformes, restent.
        int postCount = Mathf.Max(2, Mathf.RoundToInt(flatLength / PostSpacing) + 1);
        foreach (float s in new[] { -1f, 1f })
        {
            Post previous = null;
            for (int k = 0; k < postCount; k++)
            {
                float t = k / (postCount - 1f);
                Vector3 foot = Curve(t) + side * (s * PostInset);
                var go = GameArt.MeshObject("Baton", transform, postMesh, plankA);
                go.transform.position = foot;
                Shadowless(go);
                if (k > 0 && k < postCount - 1) falling.Add(go.transform);
                var post = new Post { transform = go.transform, foot = foot, bow = Mathf.Sin(t * Mathf.PI), Top = foot + Vector3.up * RopeHeight };
                posts.Add(post);
                if (previous != null)
                {
                    var rope = GameArt.MeshObject("Corde", transform, ropeMesh, plankA);
                    Shadowless(rope);
                    falling.Add(rope.transform);
                    ropes.Add((rope.transform, previous, post));
                    PlaceRope(rope.transform, previous.Top, post.Top);
                }
                previous = post;
            }
        }

        // Le sol reste fixe : des dalles invisibles sous les parcelles, le long de la courbe au repos.
        int segments = Mathf.Max(1, Mathf.RoundToInt(flatLength / ColliderSegment));
        for (int k = 0; k < segments; k++)
        {
            Vector3 p0 = Curve(k / (float)segments), p1 = Curve((k + 1f) / segments);
            Vector3 along = p1 - p0;
            var solid = new GameObject("Plateforme");
            solid.transform.SetParent(transform, false);
            solid.transform.SetPositionAndRotation((p0 + p1) * 0.5f + Vector3.down * 0.06f, Quaternion.LookRotation(along));
            solid.AddComponent<BoxCollider>().size = new Vector3(Width, 0.12f, along.magnitude + 0.1f);
            solids.Add(solid);
        }
    }

    static void PlaceRope(Transform rope, Vector3 from, Vector3 to)
    {
        Vector3 span = to - from;
        rope.SetPositionAndRotation((to + from) * 0.5f, Quaternion.LookRotation(span));
        rope.localScale = new Vector3(1f, 1f, span.magnitude);
    }

    static void Shadowless(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Position 'p' (centre du personnage) au-dessus du tablier ; 't' : avancement sur le pont, de 0 à 1.
    bool Over(Vector3 p, out float t)
    {
        Vector3 d = p - start;
        t = Vector3.Dot(new Vector3(d.x, 0f, d.z), flatDir) / flatLength;
        if (t < 0f || t > 1f || Mathf.Abs(Vector3.Dot(d, side)) > Width * 0.5f + 0.2f) return false;
        float h = p.y - Curve(t).y;
        return h > -0.2f && h < 2.6f;
    }

    // Hôte : nombre de joueurs en vie sur le pont.
    public int PlayersOn()
    {
        int n = 0;
        foreach (var p in PlayerPresence.All)
            if (p != null && p.isActiveAndEnabled && p.Alive && !p.InCannon && !p.Teleporting && Over(p.Position, out _)) n++;
        return n;
    }

    public void SetBroken(bool value, bool animate)
    {
        if (!value || Broken) return;
        Broken = true;
        BeaconNetwork.SightChanged();
        foreach (var solid in solids) if (solid != null) solid.SetActive(false);
        if (animate) GameAudio.Play3D("cannon_impact", (start + end) * 0.5f, 1f, 60f, 0.05f, 0.7f);
        foreach (var piece in falling)
        {
            if (piece == null) continue;
            if (!animate) { Destroy(piece.gameObject); continue; }
            piece.SetParent(null, true);
            piece.gameObject.layer = 2;
            piece.gameObject.AddComponent<BoxCollider>();
            piece.gameObject.AddComponent<Rigidbody>().angularVelocity = Random.insideUnitSphere * 3f;
            piece.gameObject.AddComponent<BridgePiece>();
        }
        falling.Clear();
        enabled = false;
    }

    void Update()
    {
        // Les parcelles ploient autour de chaque joueur, puis reviennent en place.
        bool anyone = false;
        float localT = -1f;
        PlayerPresence local = GameSession.Instance != null ? GameSession.Instance.LocalPresence : null;
        System.Array.Clear(targets, 0, targets.Length);
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || !p.isActiveAndEnabled || !Over(p.Position, out float t)) continue;
            anyone = true;
            if (p == local) localT = t;
            for (int k = 0; k < planks.Length; k++)
            {
                float distance = ((k + 0.5f) / planks.Length - t) * flatLength;
                targets[k] = Mathf.Max(targets[k], Bend * Mathf.Exp(-(distance * distance) / (BendReach * BendReach)));
            }
        }
        float previousSway = swayAmount;
        swayAmount = Mathf.MoveTowards(swayAmount, anyone ? 1f : 0f, Time.deltaTime / SwayFade);
        if (!anyone && !moving && previousSway <= 0f) return;

        moving = false;
        float step = SettleSpeed * Time.deltaTime * Bend;
        float amount = Mathf.SmoothStep(0f, 1f, swayAmount);
        float wave = amount * Mathf.Sin(Time.time * SwaySpeed);
        // Le personnage local suit le tablier à l'endroit où il se trouve (les autres joueurs reçoivent sa position, comme d'habitude).
        if (localT >= 0f)
        {
            var rider = local.GetComponent<PlayerController>();
            if (rider != null) rider.Carry(side * (SwayAmplitude * Mathf.Sin(localT * Mathf.PI) * amount * Mathf.Cos(Time.time * SwaySpeed) * SwaySpeed));
        }
        for (int k = 0; k < planks.Length; k++)
        {
            float next = Mathf.MoveTowards(offset[k], targets[k], step);
            if (!Mathf.Approximately(next, offset[k])) moving = true;
            offset[k] = next;
            float bow = Mathf.Sin((k + 0.5f) / planks.Length * Mathf.PI);
            planks[k].SetPositionAndRotation(rest[k] + Vector3.down * next + side * (SwayAmplitude * bow * wave), restRotation[k] * Quaternion.Euler(0f, 0f, SwayRoll * bow * wave));
        }

        // Les bâtons suivent le tablier en s'inclinant, les cordes restent tendues entre leurs sommets.
        foreach (Post post in posts)
        {
            Quaternion lean = Quaternion.AngleAxis(SwayLean * post.bow * wave, flatDir);
            post.transform.SetPositionAndRotation(post.foot + side * (SwayAmplitude * post.bow * wave), lean);
            post.Top = post.transform.position + lean * Vector3.up * RopeHeight;
        }
        foreach (var (rope, a, b) in ropes) PlaceRope(rope, a.Top, b.Top);
    }
}

// Morceau d'un pont cassé : disparaît 2 secondes après avoir touché le sol.
public class BridgePiece : MonoBehaviour
{
    bool landed;

    void Start() => Destroy(gameObject, 10f);

    void OnCollisionEnter(Collision c)
    {
        if (landed) return;
        landed = true;
        Destroy(gameObject, GameRules.BridgeFallCleanup);
    }
}
