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
    ParticleSystem[] sparks;
    AudioSource hum;
    bool touching;
    float repeatTimer;

    public bool Active { get; private set; }

    public void Init(Vector3 from, Vector3 to, GameObject beamObject, ParticleSystem[] sparkSystems)
    {
        a = from;
        b = to;
        beam = beamObject;
        sparks = sparkSystems;
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
            foreach (var s in sparks)
            {
                if (active) s.Play();
                else s.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            Vector3 mid = (a + b) * 0.5f;
            if (active)
            {
                GameAudio.Play3D("laser_on", mid, 0.6f, 16f);
                if (hum == null) hum = GameAudio.Loop3D("laser_hum", transform, mid, 0.6f, 12f);
                if (hum != null) hum.Play();
            }
            else if (hum != null) hum.Stop();
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
            GameAudio.Play3D("laser_hit", local.Position, 0.8f, 10f);
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
        if (blinkTimer <= 0f)
        {
            blinkTimer = Random.Range(3f, 10f);
            GameAudio.Play3D("blink", transform.position, 0.35f, 8f, 0.15f);
        }
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
        if (off) GameAudio.Play3D("neon", transform.position, 0.3f, 14f, 0.15f);
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
        if (!instant && value != open) GameAudio.Play3D("door", transform.position, 1f, 35f, 0f);
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

// Porte massive entre la Zone 1 et la Zone 2, fermée par un battant ectoplasmique.
// Elle s'ouvre (le battant se dissipe, la porte s'enfonce dans le sol) quand les trois orbes sont brisés.
public class ZoneDoor : MonoBehaviour
{
    const float OpenDuration = 4f;

    GameObject frame;
    Material ecto;
    BoxCollider blocker;
    Light glow;
    float height, openAmount;
    bool open;

    public bool IsOpen => open;

    public static ZoneDoor Create(Transform parent, Vector3 pos, Quaternion rot, float width, float height)
    {
        var go = new GameObject("Porte_Zone2");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var door = go.AddComponent<ZoneDoor>();
        door.height = height;

        // L'avant du modèle Meshy (+Z) regarde la Zone 1.
        door.frame = GameArt.SpawnModel("ZoneDoor", go.transform, height + 0.6f);
        float front = 0.2f;
        if (door.frame != null)
        {
            Bounds b = GameArt.LocalBounds(door.frame.transform);
            float scale = Mathf.Min(1f, (width + 1.2f) / Mathf.Max(0.01f, b.size.x));
            door.frame.transform.localScale *= scale;
            b = GameArt.LocalBounds(door.frame.transform);
            door.frame.transform.localPosition += new Vector3(0f, 0f, 0.15f - b.min.z);
            front = 0.15f + b.size.z;
        }
        else
        {
            door.frame = new GameObject("Cadre");
            door.frame.transform.SetParent(go.transform, false);
            Material stone = GameArt.Lit("DoorStone", new Color(0.18f, 0.18f, 0.2f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
            foreach (float s in new[] { -1f, 1f })
                GameArt.Prim(PrimitiveType.Cube, door.frame.transform, new Vector3(s * (width * 0.5f + 0.25f), height * 0.5f + 0.1f, 0.3f), new Vector3(0.5f, height + 0.2f, 0.4f), stone);
            GameArt.Prim(PrimitiveType.Cube, door.frame.transform, new Vector3(0f, height + 0.35f, 0.3f), new Vector3(width + 1f, 0.5f, 0.4f), stone);
            GameArt.Prim(PrimitiveType.Cube, door.frame.transform, new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, 0.15f), GameArt.Lit("DoorSlab", new Color(0.05f, 0.07f, 0.1f), null, 0.4f));
            front = 0.5f;
        }

        // Battant ectoplasmique, devant la porte.
        var slab = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(slab.GetComponent<Collider>());
        slab.name = "Ectoplasme";
        slab.transform.SetParent(go.transform, false);
        slab.transform.localPosition = new Vector3(0f, height * 0.5f, front + 0.02f);
        // Le quad regarde vers -Z : on le retourne vers la Zone 1.
        slab.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        slab.transform.localScale = new Vector3(width * 0.92f, height * 0.95f, 1f);
        door.ecto = new Material(GameArt.Ectoplasm);
        door.ecto.SetFloat("_Aspect", width / height);
        var r = slab.GetComponent<Renderer>();
        r.sharedMaterial = door.ecto;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        door.glow = new GameObject("Lueur").AddComponent<Light>();
        door.glow.transform.SetParent(go.transform, false);
        door.glow.transform.localPosition = new Vector3(0f, height * 0.6f, front + 1.2f);
        door.glow.type = LightType.Point;
        door.glow.color = new Color(0.3f, 0.6f, 1f);
        door.glow.range = 9f;
        door.glow.intensity = 1.4f;
        door.glow.shadows = LightShadows.None;

        door.blocker = go.AddComponent<BoxCollider>();
        door.blocker.center = new Vector3(0f, height * 0.5f, 0f);
        door.blocker.size = new Vector3(width + 0.2f, height, 0.6f);
        return door;
    }

    public void SetOpen(bool value, bool instant)
    {
        if (value && !open && !instant) GameAudio.Play3D("zone_door", transform.position + Vector3.up * 2f, 1f, 60f, 0f);
        open = value;
        blocker.enabled = !open;
        if (instant) Apply(open ? 1f : 0f);
    }

    void Update()
    {
        float goal = open ? 1f : 0f;
        if (Mathf.Approximately(openAmount, goal)) return;
        Apply(Mathf.MoveTowards(openAmount, goal, Time.deltaTime / OpenDuration));
    }

    void Apply(float amount)
    {
        openAmount = amount;
        ecto.SetFloat("_Open", Mathf.Clamp01(amount * 1.6f));
        float sink = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((amount - 0.3f) / 0.7f));
        frame.transform.localPosition = new Vector3(frame.transform.localPosition.x, -sink * (height + 1.2f), frame.transform.localPosition.z);
        frame.SetActive(amount < 0.999f);
        glow.intensity = Mathf.Lerp(1.4f, 0f, amount);
    }
}
