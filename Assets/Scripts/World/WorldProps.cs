using System.Collections.Generic;
using UnityEngine;

// Présence d'un personnage (local ou distant) dans le monde : utilisée par les pièges, les yeux et les chenils.
public class PlayerPresence : MonoBehaviour
{
    public static readonly List<PlayerPresence> All = new List<PlayerPresence>();

    public string PlayerId;
    public bool IsLocal;
    public bool Alive = true;
    public bool Sneaking;

    public Vector3 Position => transform.position;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
}

// Piège Laser : deux embouts face à face, faisceau visible quand un joueur s'approche à moins de 2 m
// (1 m s'il marche discrètement). Chaque client applique les dégâts à son propre personnage.
public class Laser : MonoBehaviour
{
    const float RepeatDelay = 1f;
    const float PlayerRadius = 0.35f, PlayerHalfHeight = 0.9f;

    Vector3 a, b;
    GameObject beam;
    bool touching;
    float repeatTimer;

    public bool Active { get; private set; }

    public void Init(Vector3 from, Vector3 to, GameObject beamObject)
    {
        a = from;
        b = to;
        beam = beamObject;
        beam.SetActive(false);
    }

    void Update()
    {
        bool active = false;
        foreach (var p in PlayerPresence.All)
        {
            if (!p.Alive) continue;
            float range = GameRules.LaserTriggerDistance * (p.Sneaking ? 0.5f : 1f);
            if (DistancePointSegment(p.Position, a, b) <= range) { active = true; break; }
        }
        if (active != Active)
        {
            Active = active;
            beam.SetActive(active);
        }

        // Dégâts : à l'entrée dans le faisceau, puis toutes les secondes si le joueur y reste.
        var session = GameSession.Instance;
        PlayerPresence local = session != null ? session.LocalPresence : null;
        bool hit = false;
        if (Active && local != null && local.Alive && local.isActiveAndEnabled)
        {
            Vector3 c = local.Position;
            Vector3 lo = c + Vector3.down * (PlayerHalfHeight - PlayerRadius);
            Vector3 hi = c + Vector3.up * (PlayerHalfHeight - PlayerRadius);
            hit = DistanceSegments(lo, hi, a, b) < PlayerRadius;
        }
        repeatTimer -= Time.deltaTime;
        if (hit && (!touching || repeatTimer <= 0f))
        {
            repeatTimer = RepeatDelay;
            session.DamageLocal(GameRules.LaserDamage, "Laser");
        }
        touching = hit;
    }

    static float DistancePointSegment(Vector3 p, Vector3 s0, Vector3 s1)
    {
        Vector3 d = s1 - s0;
        float t = Mathf.Clamp01(Vector3.Dot(p - s0, d) / d.sqrMagnitude);
        return Vector3.Distance(p, s0 + d * t);
    }

    static float DistanceSegments(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float c = Vector3.Dot(d1, r), b = Vector3.Dot(d1, d2);
        float denom = a * e - b * b;
        float s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
        float t = (b * s + f) / e;
        if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
        else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
        return Vector3.Distance(p1 + d1 * s, p2 + d2 * t);
    }
}

// Gros œil encastré qui fixe le joueur le plus proche.
public class Eye : MonoBehaviour
{
    const float WatchRange = 16f;
    const float MaxAngle = 65f;

    Transform ball;
    Vector3 restDir;
    float blinkTimer;
    Transform lid;

    public void Init(Transform eyeBall, Transform eyeLid)
    {
        ball = eyeBall;
        lid = eyeLid;
        restDir = transform.forward;
        blinkTimer = Random.Range(2f, 9f);
    }

    void Update()
    {
        Camera cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > 30f * 30f) return;

        PlayerPresence target = null;
        float best = WatchRange * WatchRange;
        foreach (var p in PlayerPresence.All)
        {
            if (!p.Alive) continue;
            float d = (p.Position - transform.position).sqrMagnitude;
            if (d < best) { best = d; target = p; }
        }

        Vector3 dir = restDir;
        if (target != null)
        {
            dir = (target.Position + Vector3.up * 0.5f - transform.position).normalized;
            float angle = Vector3.Angle(restDir, dir);
            if (angle > MaxAngle) dir = Vector3.Slerp(restDir, dir, MaxAngle / angle);
        }
        float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
        ball.rotation = Quaternion.Slerp(ball.rotation, Quaternion.LookRotation(dir, Vector3.up), k);

        // Clignement occasionnel.
        blinkTimer -= Time.deltaTime;
        if (lid != null)
        {
            float open = blinkTimer < 0.15f ? Mathf.Abs(blinkTimer / 0.15f * 2f - 1f) : 1f;
            lid.localScale = new Vector3(1.08f, Mathf.Lerp(1.08f, 0.02f, open), 1.08f);
            lid.gameObject.SetActive(open < 0.98f);
        }
        if (blinkTimer <= 0f) blinkTimer = Random.Range(3f, 10f);
    }
}

// Lampe qui grésille.
public class FlickerLight : MonoBehaviour
{
    Light lightSource;
    Renderer bulb;
    float baseIntensity, timer;
    bool off;

    public void Init(Light l, Renderer bulbRenderer)
    {
        lightSource = l;
        bulb = bulbRenderer;
        baseIntensity = l.intensity;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        off = !off && Random.value < 0.35f;
        timer = off ? Random.Range(0.03f, 0.18f) : Random.Range(0.05f, 2.5f);
        lightSource.intensity = off ? baseIntensity * 0.08f : baseIntensity * Random.Range(0.75f, 1.05f);
        if (bulb != null) bulb.enabled = !off;
    }
}

// Unique porte du Hub : coulisse vers le haut une fois le Bouton Rouge enclenché.
public class HubDoor : MonoBehaviour
{
    Vector3 closedPos, openPos;
    bool open;

    public void Init(float lift)
    {
        closedPos = transform.localPosition;
        openPos = closedPos + Vector3.up * lift;
    }

    public void SetOpen(bool value, bool instant = false)
    {
        open = value;
        if (instant) transform.localPosition = open ? openPos : closedPos;
    }

    void Update()
    {
        Vector3 target = open ? openPos : closedPos;
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, target, 2f * Time.deltaTime);
    }
}

// Légère animation des objets ramassables.
public class Bobbing : MonoBehaviour
{
    Vector3 basePos;
    float phase;

    void Start()
    {
        basePos = transform.localPosition;
        phase = Random.value * 10f;
    }

    void Update()
    {
        float t = Time.time + phase;
        transform.localPosition = basePos + Vector3.up * (0.06f + Mathf.Sin(t * 2f) * 0.05f);
        transform.localRotation = Quaternion.Euler(0f, t * 40f, 0f);
    }
}
