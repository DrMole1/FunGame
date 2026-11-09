using System.Collections.Generic;
using UnityEngine;

// Pont suspendu (GDD 10.3, V0.6.6) entre deux plateformes d'une très haute salle : parcelles, bâtons et cordes (modèles Meshy) au
// damier noir et blanc. Les parcelles ploient sous les pas (calcul local, sans réseau). Trois joueurs ou plus dessus : il casse
// (décidé par l'hôte) ; parcelles et cordes tombent et disparaissent 2 secondes après l'impact. Hors du bâti : les entités
// ne le connaissent pas.
public class SuspendedBridge : MonoBehaviour
{
    const float Width = 1.6f, PlankSpacing = 0.58f, PostSpacing = 3f, PostInset = 0.74f, RopeHeight = 1.15f, ColliderSegment = 1.5f;
    const float Bend = 0.13f, BendReach = 1.3f, SettleSpeed = 6f, MaxSagRatio = 0.05f, MaxSag = 1.1f, FallSafety = 10f;

    public int Id { get; private set; }
    public bool Broken { get; private set; }

    Vector3 start, end, side, flatDir;
    float flatLength, sag;
    Transform[] planks;
    Vector3[] rest;
    float[] offset, targets;
    readonly List<Transform> falling = new List<Transform>();
    readonly List<GameObject> solids = new List<GameObject>();
    bool moving;

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
            falling.Add(plank.transform);
        }

        // Une rangée de bâtons de chaque côté, reliés par une corde. Ceux des deux extrémités, sur les plateformes, restent.
        int posts = Mathf.Max(2, Mathf.RoundToInt(flatLength / PostSpacing) + 1);
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 previous = Vector3.zero;
            for (int k = 0; k < posts; k++)
            {
                float t = k / (posts - 1f);
                Vector3 foot = Curve(t) + side * (s * PostInset);
                var post = GameArt.MeshObject("Baton", transform, postMesh, plankA);
                post.transform.position = foot;
                Shadowless(post);
                if (k > 0 && k < posts - 1) falling.Add(post.transform);
                Vector3 top = foot + Vector3.up * RopeHeight;
                if (k > 0)
                {
                    var rope = GameArt.MeshObject("Corde", transform, ropeMesh, plankA);
                    Vector3 span = top - previous;
                    rope.transform.SetPositionAndRotation((top + previous) * 0.5f, Quaternion.LookRotation(span));
                    rope.transform.localScale = new Vector3(1f, 1f, span.magnitude);
                    Shadowless(rope);
                    falling.Add(rope.transform);
                }
                previous = top;
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
        System.Array.Clear(targets, 0, targets.Length);
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || !p.isActiveAndEnabled || !Over(p.Position, out float t)) continue;
            anyone = true;
            for (int k = 0; k < planks.Length; k++)
            {
                float distance = ((k + 0.5f) / planks.Length - t) * flatLength;
                targets[k] = Mathf.Max(targets[k], Bend * Mathf.Exp(-(distance * distance) / (BendReach * BendReach)));
            }
        }
        if (!anyone && !moving) return;
        moving = false;
        float step = SettleSpeed * Time.deltaTime * Bend;
        for (int k = 0; k < planks.Length; k++)
        {
            float next = Mathf.MoveTowards(offset[k], targets[k], step);
            if (!Mathf.Approximately(next, offset[k]))
            {
                offset[k] = next;
                planks[k].position = rest[k] + Vector3.down * next;
                moving = true;
            }
        }
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
