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
    // Dans un Canon (V0.5, note 02 du 7.5) : aucune entité ne peut le prendre pour cible.
    public bool InCannon;
    // En téléportation : aucune entité ne peut le prendre pour cible (les poursuites s'arrêtent) et il n'allume pas les Lasers.
    public bool Teleporting;

    // Autres joueurs : dernière position reçue du réseau, en avance sur celle affichée (lissée avec retard).
    [System.NonSerialized] public bool HasNetPosition;
    [System.NonSerialized] public Vector3 NetPosition;

    public Vector3 Position => HasNetPosition ? NetPosition : transform.position;

    // Immobile : n'a pas bougé de plus de 10 cm depuis un quart de seconde (l'Araignée ne le voit pas).
    const float StillDistance = 0.1f, StillDelay = 0.25f;
    Vector3 stillAnchor;
    float lastMove;
    public bool Still => Time.time - lastMove > StillDelay;

    void Update()
    {
        Vector3 pos = Position;
        if ((pos - stillAnchor).sqrMagnitude > StillDistance * StillDistance)
        {
            stillAnchor = pos;
            lastMove = Time.time;
        }
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
}

// Piège Laser : deux embouts face à face, faisceau visible quand un joueur s'approche à moins de 4 m (2 m s'il marche
// discrètement) et voit au moins un embout : derrière un mur, dans le couloir d'à côté, il ne l'allume pas.
// Chaque client applique les dégâts à son propre personnage.
public class Laser : MonoBehaviour
{
    const float RepeatDelay = 1f;
    const float PlayerRadius = 0.35f, PlayerHalfHeight = 0.9f;
    // Hauteur du regard au-dessus du centre du personnage, et point visé juste devant l'embout, encastré au ras du mur.
    const float ViewHeight = 0.4f, TipInset = 0.2f;

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
            if (!p.Alive || p.Teleporting) continue;
            float range = GameRules.LaserTriggerDistance * (p.Sneaking ? 0.5f : 1f);
            if (DistancePointSegment(p.Position, a, b) <= range && SeesTip(p)) { active = true; break; }
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
        if (Active && local != null && local.Alive && local.isActiveAndEnabled && !local.Teleporting)
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

    bool SeesTip(PlayerPresence p)
    {
        Vector3 eye = p.Position + Vector3.up * ViewHeight;
        Vector3 inset = (b - a).normalized * TipInset;
        return ClearLine(eye, a + inset) || ClearLine(eye, b - inset);
    }

    static bool ClearLine(Vector3 from, Vector3 to) =>
        !Physics.Linecast(from, to, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

    public static float DistancePointSegment(Vector3 p, Vector3 s0, Vector3 s1)
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

// Gros œil encastré qui fixe le joueur le plus proche (V0.6.9 : il ne projette plus de faisceau).
public class Eye : MonoBehaviour
{
    const float FollowRange = 30f;
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
        float cameraDistance = cam != null ? (cam.transform.position - transform.position).sqrMagnitude : 0f;
        if (cameraDistance > FollowRange * FollowRange) return;

        // EE2 (chapitre 15.1, V0.6.14) : le premier œil que voit le joueur, s'il a tiré les 2 %, se change en caméra.
        if (EasterEggs.EyeCameraArmed && cam != null && EasterEggs.Seen(cam, transform.position + transform.forward * (0.3f * transform.lossyScale.x), 25f))
        {
            EasterEggs.EyeCameraArmed = false;
            StartCoroutine(EasterEggs.EyeToCamera(transform));
        }

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
            GameAudio.Play3D("blink", transform.position, 0.26f, 8f, 0.15f);
        }
    }

}

// Néon du labyrinthe : suit le réglage de luminosité ; certains grésillent. V0.4.7 : à moins de 30 m d'un Roi, tous
// clignotent frénétiquement. V0.6.8 : éteints pendant le Couvre Feu.
public class FlickerLight : MonoBehaviour
{
    // Courant du labyrinthe (WorldStreamer.SetPower) : 1 normal, 0 Couvre Feu.
    public static float Power { get; set; } = 1f;
    // Avertissement (GameSession) : pendant les 2 secondes qui précèdent le Couvre Feu, tous les néons clignotent.
    public static bool CurfewWarning { get; set; }
    // Positions des Rois (mises à jour par GameSession chez chaque joueur).
    public static readonly System.Collections.Generic.List<Vector3> Frenzy = new System.Collections.Generic.List<Vector3>();

    Light lightSource;
    Renderer bulb;
    float baseIntensity, timer, level = 1f;
    bool off, flickers;

    public void Init(Light l, Renderer bulbRenderer, bool flicker)
    {
        lightSource = l;
        bulb = bulbRenderer;
        baseIntensity = l.intensity;
        flickers = flicker;
        WorldStreamer.Register(this);
    }

    // Lampe trop loin de la caméra : éteinte, et plus aucune mise à jour (WorldStreamer).
    public void SetLit(bool lit)
    {
        if (enabled == lit) return;
        enabled = lit;
        lightSource.enabled = lit;
    }

    bool Frantic
    {
        get
        {
            foreach (Vector3 p in Frenzy)
                if ((p - transform.position).sqrMagnitude < GameRules.RoiLightRange * GameRules.RoiLightRange) return true;
            return false;
        }
    }

    bool frantic, blackout;
    int burstLeft;

    // Près du Roi : des rafales imprévisibles de 1 à 5 coupures (extinctions franches ou simples baisses, de durées
    // variables), séparées par des moments allumés, plus ou moins longs, où la lumière tremble légèrement.
    void FranticFlicker()
    {
        timer -= Time.deltaTime;
        if (!off && burstLeft == 0) level = Mathf.Clamp(level + Random.Range(-0.04f, 0.04f), 0.75f, 1.15f);
        if (timer > 0f) return;
        if (off)
        {
            // Rallumage, puis la coupure suivante de la rafale, ou une accalmie.
            off = false;
            level = Random.Range(0.7f, 1.2f);
            timer = burstLeft > 0 ? Random.Range(0.04f, 0.25f) : CurfewWarning ? Random.Range(0.05f, 0.2f) : Random.Range(0.25f, 1.6f);
        }
        else
        {
            if (burstLeft == 0) burstLeft = Random.Range(1, 6);
            burstLeft--;
            off = true;
            level = Random.value < 0.35f ? Random.Range(0.15f, 0.45f) : Random.Range(0.01f, 0.06f);
            timer = Random.Range(0.03f, 0.15f);
            if (Random.value < 0.1f) GameAudio.Play3D("neon", transform.position, 0.25f, 14f, 0.2f);
        }
        if (bulb != null) bulb.enabled = level > 0.2f;
    }

    void Update()
    {
        if (Power <= 0f)
        {
            lightSource.enabled = false;
            if (bulb != null) bulb.enabled = false;
            blackout = true;
            return;
        }
        if (blackout)
        {
            blackout = false;
            lightSource.enabled = true;
            off = false;
            level = 1f;
            if (bulb != null) bulb.enabled = true;
        }
        lightSource.intensity = baseIntensity * level * Brightness.Factor * Power;
        bool wasFrantic = frantic;
        frantic = CurfewWarning || Frenzy.Count > 0 && Frantic;
        // (Un néon qui grésille peut attendre 2,5 s sa prochaine coupure : l'avertissement commence tout de suite.)
        if (CurfewWarning && !wasFrantic) timer = 0f;
        if (frantic)
        {
            FranticFlicker();
            return;
        }
        if (wasFrantic)
        {
            off = false;
            burstLeft = 0;
            level = 1f;
            timer = 0f;
            if (bulb != null) bulb.enabled = true;
        }
        if (!flickers) return;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        off = !off && Random.value < 0.35f;
        timer = off ? Random.Range(0.03f, 0.18f) : Random.Range(0.05f, 2.5f);
        level = off ? 0.08f : Random.Range(0.75f, 1.05f);
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
        if (value != open) BeaconNetwork.SightChanged();
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

// Porte entre une zone impaire et la zone paire suivante. V0.6.1 : porte d'acier blindée de bunker (modèle Meshy
// "PorteBlindee"), sans voile bleu. Elle s'ouvre (elle s'enfonce dans le sol, un gyrophare orange tourne) quand les trois
// glyphes neutres de sa zone sont actionnés (V0.6.5).
public class ZoneDoor : MonoBehaviour
{
    const float OpenDuration = 4f;
    // Profondeur de la porte (le modèle Meshy, très épais, est aplati pour tenir dans le mur).
    const float Depth = 0.7f;

    GameObject frame;
    BoxCollider blocker;
    Light warning;
    float height, openAmount;
    bool open;

    public bool IsOpen => open;
    public Collider Blocker => blocker;

    public static ZoneDoor Create(Transform parent, Vector3 pos, Quaternion rot, float width, float height)
    {
        var go = new GameObject("Porte_Zone");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var door = go.AddComponent<ZoneDoor>();
        door.height = height;

        // L'avant du modèle Meshy (+Z) regarde la zone impaire. V0.4.6 : la porte est prise dans le mur qui délimite la
        // zone, à moitié dans chaque zone.
        door.frame = GameArt.SpawnModel("PorteBlindee", go.transform, height + 0.6f);
        if (door.frame != null)
        {
            Bounds b = GameArt.LocalBounds(door.frame.transform);
            float scale = Mathf.Min(1f, (width + 1.2f) / Mathf.Max(0.01f, b.size.x));
            Vector3 s = door.frame.transform.localScale * scale;
            door.frame.transform.localScale = new Vector3(s.x, s.y, s.z * Depth / Mathf.Max(0.01f, b.size.z * scale));
            b = GameArt.LocalBounds(door.frame.transform);
            door.frame.transform.localPosition += new Vector3(0f, 0f, -b.center.z);
        }
        else
        {
            door.frame = new GameObject("Cadre");
            door.frame.transform.SetParent(go.transform, false);
            Material steel = GameArt.Lit("PorteAcier", new Color(0.32f, 0.33f, 0.35f), GameArt.Noise("rust", 64, 0.55f, 0.35f, 23), 0.55f);
            foreach (float s in new[] { -1f, 1f })
                GameArt.Prim(PrimitiveType.Cube, door.frame.transform, new Vector3(s * (width * 0.5f + 0.25f), height * 0.5f + 0.1f, 0f), new Vector3(0.5f, height + 0.2f, Depth), steel);
            GameArt.Prim(PrimitiveType.Cube, door.frame.transform, new Vector3(0f, height + 0.35f, 0f), new Vector3(width + 1f, 0.5f, Depth), steel);
            GameArt.Prim(PrimitiveType.Cube, door.frame.transform, new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, Depth * 0.6f), steel);
        }

        // Gyrophare orange au-dessus de la porte, allumé pendant l'ouverture.
        door.warning = new GameObject("Gyrophare").AddComponent<Light>();
        door.warning.transform.SetParent(go.transform, false);
        door.warning.transform.localPosition = new Vector3(0f, height + 0.9f, Depth * 0.5f + 0.4f);
        door.warning.type = LightType.Spot;
        door.warning.color = new Color(1f, 0.55f, 0.1f);
        door.warning.range = 12f;
        door.warning.spotAngle = 70f;
        door.warning.intensity = 3f;
        door.warning.shadows = LightShadows.None;
        door.warning.enabled = false;

        door.blocker = go.AddComponent<BoxCollider>();
        door.blocker.center = new Vector3(0f, height * 0.5f, 0f);
        door.blocker.size = new Vector3(width + 0.2f, height, 0.6f);
        return door;
    }

    public void SetOpen(bool value, bool instant)
    {
        if (value && !open && !instant) GameAudio.Play3D("zone_door", transform.position + Vector3.up * 2f, 1f, 60f, 0f);
        if (value != open) BeaconNetwork.SightChanged();
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
        // Les verrous sautent, puis la porte s'enfonce lourdement dans le sol.
        float sink = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((amount - 0.15f) / 0.85f));
        frame.transform.localPosition = new Vector3(frame.transform.localPosition.x, -sink * (height + 1.2f), frame.transform.localPosition.z);
        frame.SetActive(amount < 0.999f);
        warning.enabled = amount > 0.001f && amount < 0.999f;
        if (warning.enabled) warning.transform.localRotation = Quaternion.Euler(35f, Time.time * 360f, 0f);
    }
}
