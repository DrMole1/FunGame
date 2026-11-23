using System.Collections.Generic;
using UnityEngine;

// V0.7.11 (7.12) : Métronome posé au sol (ou sorti d'un Super Monticule). Son balancier bat 120 BPM, avec un son de métronome
// spatialisé, entendu à moins de 25 m. Un joueur qui le regarde (dans son champ de vision, sans mur entre eux) à moins de 20 m
// penche la tête au rythme des battements, à droite puis à gauche (roulis de la caméra, OrbitCamera.MetronomeTilt).
// Le tempo suit l'horloge de chaque joueur.
public class MetronomeDevice : MonoBehaviour
{
    public const float Bpm = 120f, SoundRange = 25f, LookRange = 20f, SwingAngle = 28f, HeadTilt = 6f;
    public static readonly List<MetronomeDevice> All = new List<MetronomeDevice>();

    Transform arm;
    int lastBeat = -1;
    float height;

    public static MetronomeDevice Build(GameObject device, Transform visual, float height)
    {
        if (GameArt.SpawnModel("Metronome", visual, height) == null)
        {
            // Pyramide de bois (repli) : un socle et un corps qui s'affine.
            Material wood = GameArt.Lit("MetronomeBois", new Color(0.3f, 0.17f, 0.08f), null, 0.5f);
            GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, height * 0.06f, 0f), new Vector3(height * 0.55f, height * 0.12f, height * 0.4f), wood);
            GameArt.Prim(PrimitiveType.Cube, visual, new Vector3(0f, height * 0.5f, -0.02f), new Vector3(height * 0.35f, height * 0.8f, height * 0.25f), wood);
        }
        var m = device.AddComponent<MetronomeDevice>();
        m.height = height;
        // Balancier de laiton devant le corps, pivotant à sa base.
        m.arm = new GameObject("Balancier").transform;
        m.arm.SetParent(visual, false);
        m.arm.localPosition = new Vector3(0f, height * 0.16f, height * 0.16f);
        Material brass = GameArt.Lit("MetronomeLaiton", new Color(0.85f, 0.65f, 0.25f), null, 0.8f);
        GameArt.Prim(PrimitiveType.Cylinder, m.arm, new Vector3(0f, height * 0.36f, 0f), new Vector3(0.012f, height * 0.36f, 0.012f), brass);
        GameArt.Prim(PrimitiveType.Cube, m.arm, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.05f, 0.06f, 0.03f), brass);
        foreach (var c in m.arm.GetComponentsInChildren<Collider>()) Destroy(c);
        return m;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // Battements depuis l'origine de l'horloge commune : un toutes les 0,5 s, le balancier change de côté à chaque battement.
    public static float Beats => (float)(Time.timeAsDouble * Bpm / 60.0);

    void Update()
    {
        float beats = Beats;
        int beat = Mathf.FloorToInt(beats);
        // Aller-retour : un battement par extrémité.
        float phase = Mathf.Sin(beats * Mathf.PI);
        if (arm != null) arm.localRotation = Quaternion.Euler(0f, 0f, SwingAngle * phase * (beat % 2 == 0 ? 1f : 1f));
        if (beat != lastBeat)
        {
            if (lastBeat >= 0) GameAudio.Play3D("metronome", transform.position + Vector3.up * height * 0.6f, 0.8f, SoundRange, 0f, beat % 4 == 0 ? 1.12f : 1f);
            lastBeat = beat;
        }
    }

    // Le joueur local regarde-t-il un Métronome à moins de 20 m ? (caméra principale, sans mur entre eux)
    public static bool Watched(Camera cam)
    {
        if (cam == null) return false;
        foreach (var m in All)
        {
            if (m == null) continue;
            Vector3 p = m.transform.position + Vector3.up * m.height * 0.5f;
            Vector3 d = p - cam.transform.position;
            if (d.sqrMagnitude > LookRange * LookRange) continue;
            Vector3 v = cam.WorldToViewportPoint(p);
            if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
            if (Physics.Linecast(cam.transform.position, p, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(m.transform) && hit.collider.GetComponentInParent<PlayerController>() == null) continue;
            return true;
        }
        return false;
    }
}

// V0.7.11 (7.13) : Super Balle, vieille balle de cuir un peu plus petite qu'un joueur, posée ou ramassée comme les autres objets.
// Poussée par un joueur, elle part dans sa direction (plus vite s'il la percute en courant) ; elle rebondit sur les murs et sur
// les autres Super Balles. Lancée vite, elle repousse violemment le joueur qu'elle percute (10 de dégâts) et les entités de type
// "normale". Comme les ballons des Hubs : seul le joueur qui la pousse envoie un message, chacun calcule ensuite le même trajet.
public class SuperBall : MonoBehaviour
{
    public const float FastSpeed = 7f, HitDamageCooldown = 1f;
    const float PlayerRadius = 0.35f, ReachHeight = 2f, MinPush = 0.4f, MinKick = 3f, KickBoost = 1.6f, RunBoost = 2.4f;
    const float Friction = 0.6f, StopSpeed = 0.08f, Bounce = 0.75f, SendInterval = 0.15f, Gravity = 18f;

    public static readonly List<SuperBall> All = new List<SuperBall>();

    Beacon device;
    Transform visual;
    float radius;
    Vector3 velocity;
    float lastSend = -10f, hitAt = -10f;
    string lastKicker = "";
    float lastKickAt = -10f;
    Rigidbody playerBody;
    PlayerPresence bodyOwner;
    readonly RaycastHit[] hits = new RaycastHit[8];

    public int Id => device != null ? device.Id : 0;
    public float Speed => new Vector2(velocity.x, velocity.z).magnitude;
    public Vector3 Velocity => velocity;
    public string State => Id + "|" + NetworkGame.F(transform.position.x) + "|" + NetworkGame.F(transform.position.y) + "|" + NetworkGame.F(transform.position.z)
        + "|" + NetworkGame.F(velocity.x) + "|" + NetworkGame.F(velocity.z);

    public static SuperBall Build(GameObject go, Transform visual, float size)
    {
        var model = GameArt.SpawnModel("SuperBalle", visual, size);
        if (model != null)
        {
            // V0.7.13 (Note 05) : bien sphérique (le modèle est ramené à la même largeur que sa hauteur, dans les deux sens).
            Bounds b = GameArt.LocalBounds(model.transform);
            if (b.size.x > 0.001f && b.size.z > 0.001f) model.transform.localScale = new Vector3(size / b.size.x, 1f, size / b.size.z);
        }
        else
            GameArt.Prim(PrimitiveType.Sphere, visual, new Vector3(0f, size * 0.5f, 0f), Vector3.one * size, GameArt.Lit("SuperBalleCuir", new Color(0.45f, 0.27f, 0.13f), null, 0.35f));
        var ball = go.AddComponent<SuperBall>();
        ball.device = go.GetComponent<Beacon>();
        ball.visual = visual;
        ball.radius = size * 0.5f;
        // Solide : les joueurs ne la traversent pas (ils la poussent).
        var solid = go.AddComponent<SphereCollider>();
        solid.center = Vector3.up * ball.radius;
        solid.radius = ball.radius * 0.95f;
        return ball;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public void Apply(Vector3 pos, Vector3 vel, string kicker)
    {
        transform.position = pos;
        velocity = new Vector3(vel.x, 0f, vel.z);
        lastKicker = kicker;
        lastKickAt = Time.time;
    }

    // Onde de choc du Mégaphone, collision avec une autre balle : nouvelle vitesse (chez chacun, même calcul).
    public void Push(Vector3 vel)
    {
        velocity = new Vector3(vel.x, 0f, vel.z);
        lastKicker = "";
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        TryKick();
        if (velocity == Vector3.zero) { Settle(dt); return; }
        Roll(velocity * dt);
        Settle(dt);
        BumpBalls();
        HitPlayer();
        velocity *= Mathf.Exp(-Friction * dt);
        if (velocity.sqrMagnitude < StopSpeed * StopSpeed) velocity = Vector3.zero;
    }

    // Elle suit le sol (pentes, marches) et tombe dans le vide ('fall' : vitesse de chute ; négative, elle monte).
    float fall, bouncedAt = -10f;

    void Settle(float dt)
    {
        Vector3 top = transform.position + Vector3.up * (radius + 0.3f);
        if (Physics.SphereCast(top, radius * 0.5f, Vector3.down, out RaycastHit hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform))
        {
            float ground = hit.point.y;
            float y = transform.position.y;
            // V0.7.13 (Note 05) : sur un Trampoline, elle rebondit comme un joueur (aussi haut).
            var session = GameSession.Instance;
            if (fall >= 0f && y - ground < 0.4f && Time.time - bouncedAt > 0.35f && session != null && session.TrampolineAtFeet(transform.position) is Beacon pad)
            {
                bouncedAt = Time.time;
                fall = -Mathf.Sqrt(2f * Gravity * GameSession.TrampolineReach);
                GameSession.TrampolineBounce(pad);
            }
            if (y - ground > 0.05f || fall < 0f)
            {
                fall += Gravity * dt;
                float next = y - fall * dt;
                // Un plafond l'arrête dans sa montée.
                if (fall < 0f && Physics.SphereCast(transform.position + Vector3.up * radius, radius * 0.9f, Vector3.up, out RaycastHit roof, -fall * dt + 0.05f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && !roof.collider.transform.IsChildOf(transform))
                {
                    fall = 0f;
                    next = y;
                }
                y = Mathf.Max(ground, next);
            }
            else
            {
                fall = 0f;
                y = Mathf.MoveTowards(y, ground, 4f * dt);
            }
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
        }
        // Le cuir roule sur lui-même.
        if (visual != null && velocity.sqrMagnitude > 0.0001f)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, velocity.normalized);
            visual.RotateAround(transform.position + Vector3.up * radius, axis, velocity.magnitude * dt / radius * Mathf.Rad2Deg);
        }
    }

    // Le joueur local avance contre la balle : elle part devant lui, d'autant plus vite qu'il court.
    void TryKick()
    {
        var session = GameSession.Instance;
        PlayerPresence player = session != null ? session.LocalPresence : null;
        if (player == null || !player.Alive || !player.isActiveAndEnabled) return;
        Vector3 center = transform.position + Vector3.up * radius;
        Vector3 away = center - (player.Position + Vector3.up * 0.0f);
        if (Mathf.Abs(away.y) > ReachHeight) return;
        away.y = 0f;
        float reach = radius + PlayerRadius + 0.05f;
        if (away.sqrMagnitude > reach * reach || away.sqrMagnitude < 0.0001f) return;
        if (bodyOwner != player)
        {
            bodyOwner = player;
            playerBody = player.GetComponent<Rigidbody>();
        }
        if (playerBody == null) return;
        Vector3 direction = away.normalized;
        float push = Vector3.Dot(new Vector3(playerBody.velocity.x, 0f, playerBody.velocity.z), direction);
        if (push < MinPush) return;
        bool running = push > GameRules.BaseSpeed * 1.15f;
        float kick = Mathf.Max(MinKick, push * (running ? RunBoost : KickBoost));
        if (Vector3.Dot(velocity, direction) >= kick * 0.8f) return;
        velocity = direction * kick;
        lastKicker = player.PlayerId;
        lastKickAt = Time.time;
        if (Time.time - lastSend < SendInterval) return;
        lastSend = Time.time;
        session.SendSuperBallKick(this);
    }

    void Roll(Vector3 step)
    {
        float distance = step.magnitude;
        if (distance < 0.0001f) return;
        Vector3 direction = step / distance;
        float probe = radius * 0.8f;
        Vector3 from = transform.position + Vector3.up * (radius + 0.15f);
        int count = Physics.SphereCastNonAlloc(from, probe, direction, hits, distance + radius - probe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerPresence>() != null || c.GetComponentInParent<Entity>() != null
                || c.GetComponentInParent<SuperBall>() != null || hits[i].distance <= 0f) continue;
            Vector3 normal = new Vector3(hits[i].normal.x, 0f, hits[i].normal.z).normalized;
            if (normal == Vector3.zero) continue;
            // Elle rebondit sur les murs.
            velocity = Vector3.Reflect(velocity, normal) * Bounce;
            return;
        }
        transform.position += step;
    }

    // Les Super Balles se repoussent entre elles (choc élastique, masses égales).
    void BumpBalls()
    {
        foreach (var other in All)
        {
            if (other == null || other == this || other.GetInstanceID() < GetInstanceID()) continue;
            Vector3 d = other.transform.position - transform.position;
            d.y = 0f;
            float min = radius + other.radius;
            if (d.sqrMagnitude >= min * min || d.sqrMagnitude < 0.0001f) continue;
            Vector3 n = d.normalized;
            float approach = Vector3.Dot(velocity - other.velocity, n);
            if (approach <= 0f) continue;
            velocity -= n * approach;
            other.velocity += n * approach;
            float overlap = min - d.magnitude;
            transform.position -= n * overlap * 0.5f;
            other.transform.position += n * overlap * 0.5f;
        }
    }

    // Lancée vite, elle percute le joueur local (sauf celui qui vient de la pousser) : 10 de dégâts et un violent recul.
    void HitPlayer()
    {
        if (Speed < FastSpeed || Time.time - hitAt < HitDamageCooldown) return;
        var session = GameSession.Instance;
        PlayerPresence player = session != null ? session.LocalPresence : null;
        if (player == null || !player.Alive) return;
        if (player.PlayerId == lastKicker && Time.time - lastKickAt < 0.6f) return;
        Vector3 d = player.Position - (transform.position + Vector3.up * radius);
        if (Mathf.Abs(d.y) > radius + 1f) return;
        d.y = 0f;
        if (d.magnitude > radius + PlayerRadius + 0.1f) return;
        hitAt = Time.time;
        session.HitBySuperBall(this, velocity);
        velocity *= 0.4f;
    }
}
