using System.Collections.Generic;
using UnityEngine;

// Ballon de cirque d'un Hub (Note 06 du chapitre 4, V0.6.9) : poussé par les joueurs, à la manière d'un ballon de football. Sans
// collision avec les personnages, il roule sur le sol plat du Hub et rebondit sur le décor. Seul le joueur qui le touche envoie un
// message (position et vitesse, au plus quelques fois par seconde) : chacun calcule ensuite le même trajet.
public class HubBall : MonoBehaviour
{
    const float PlayerRadius = 0.35f, ReachHeight = 2f, MinPush = 0.4f, MinKick = 3f, KickBoost = 1.6f;
    const float Friction = 0.9f, StopSpeed = 0.08f, Bounce = 0.7f, WallMargin = 0.05f, SendInterval = 0.15f;

    public static readonly Dictionary<int, HubBall> All = new Dictionary<int, HubBall>();

    int id;
    Transform hub;
    float radius;
    Vector3 velocity;
    float lastSend = -10f;
    Rigidbody playerBody;
    PlayerPresence bodyOwner;
    readonly RaycastHit[] hits = new RaycastHit[8];

    public bool Moved { get; private set; }
    // Champs d'un message : identifiant, position et vitesse horizontales.
    public string State => id + "|" + NetworkGame.F(transform.position.x) + "|" + NetworkGame.F(transform.position.z) + "|" + NetworkGame.F(velocity.x) + "|" + NetworkGame.F(velocity.z);

    public static void Attach(GameObject sphere, int id, Transform hub)
    {
        var ball = sphere.AddComponent<HubBall>();
        ball.id = id;
        ball.hub = hub;
        ball.radius = sphere.transform.localScale.x * 0.5f;
        All[id] = ball;
    }

    void OnDestroy()
    {
        if (All.TryGetValue(id, out var ball) && ball == this) All.Remove(id);
    }

    public void Apply(float x, float z, float vx, float vz)
    {
        transform.position = new Vector3(x, transform.position.y, z);
        velocity = new Vector3(vx, 0f, vz);
        Moved = true;
    }

    void FixedUpdate()
    {
        TryKick();
        if (velocity == Vector3.zero) return;
        float dt = Time.fixedDeltaTime;
        Roll(velocity * dt);
        velocity *= Mathf.Exp(-Friction * dt);
        if (velocity.sqrMagnitude < StopSpeed * StopSpeed) velocity = Vector3.zero;
    }

    // Le joueur local avance contre le ballon : celui-ci part devant lui, plus vite que lui.
    void TryKick()
    {
        PlayerPresence player = GameSession.Instance != null ? GameSession.Instance.LocalPresence : null;
        if (player == null || !player.Alive || !player.isActiveAndEnabled) return;
        Vector3 away = transform.position - player.Position;
        if (Mathf.Abs(away.y) > ReachHeight) return;
        away.y = 0f;
        float reach = radius + PlayerRadius;
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
        float kick = Mathf.Max(MinKick, push * KickBoost);
        if (Vector3.Dot(velocity, direction) >= kick * 0.8f) return;
        velocity = direction * kick;
        Moved = true;
        if (Time.time - lastSend < SendInterval) return;
        lastSend = Time.time;
        GameSession.Instance.SendBallKick(this);
    }

    void Roll(Vector3 step)
    {
        float distance = step.magnitude;
        Vector3 direction = step / distance;
        // Sphère réduite et un peu relevée : elle ne touche pas le sol.
        float probe = radius * 0.85f;
        Vector3 from = transform.position + Vector3.up * (radius * 0.2f);
        int count = Physics.SphereCastNonAlloc(from, probe, direction, hits, distance + radius - probe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (c.GetComponentInParent<PlayerPresence>() != null || c.GetComponentInParent<Entity>() != null || hits[i].distance <= 0f) continue;
            Vector3 normal = new Vector3(hits[i].normal.x, 0f, hits[i].normal.z).normalized;
            if (normal == Vector3.zero) continue;
            velocity = Vector3.Reflect(velocity, normal) * Bounce;
            return;
        }
        Vector3 next = transform.position + step;
        // Il reste dans le chapiteau, même devant la porte ouverte.
        Vector3 fromCenter = next - hub.position;
        fromCenter.y = 0f;
        float limit = WorldBuilder.HubRadius - radius - WallMargin;
        if (fromCenter.sqrMagnitude > limit * limit)
        {
            Vector3 inward = -fromCenter.normalized;
            velocity = Vector3.Reflect(velocity, inward) * Bounce;
            next = new Vector3(hub.position.x, next.y, hub.position.z) - inward * limit;
        }
        transform.position = next;
    }
}
