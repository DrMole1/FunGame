using System.Collections.Generic;
using UnityEngine;

// V0.7.21 (11.3) : pièces du Château.
// - DollEyes : les yeux d'une poupée russe (dernier étage des Tours et du Donjon) suivent le joueur le plus proche. Purement local :
//   chaque client lit les positions des joueurs qu'il connaît déjà, rien ne passe sur le réseau.
// - DarkTowerRoom : dernier étage plongé dans le noir (5 % des Tours et Donjons) : sans musique, -1 de sérénité par seconde, la
//   caméra tremble (GameSession.UpdateDarkTowerDrain).
// - CemeteryArea : cimetières (sol de gravier 1 m sous le niveau du Hub) : bruits de pas sur gravier (Footsteps).
public class DollEyes : MonoBehaviour
{
    const float WatchRange = 25f, CameraRange = 40f, MaxAngle = 70f, TurnSpeed = 240f;

    Transform[] balls;
    Quaternion[] rest;

    public void Init(Transform[] eyeBalls)
    {
        balls = eyeBalls;
        rest = new Quaternion[balls.Length];
        for (int k = 0; k < balls.Length; k++) rest[k] = balls[k].localRotation;
    }

    void Update()
    {
        if (balls == null) return;
        Camera cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > CameraRange * CameraRange) return;
        PlayerPresence target = null;
        float best = WatchRange * WatchRange;
        foreach (var p in PlayerPresence.All)
        {
            if (!p.Alive) continue;
            float d = (p.Position - transform.position).sqrMagnitude;
            if (d < best) { best = d; target = p; }
        }
        for (int k = 0; k < balls.Length; k++)
        {
            Transform ball = balls[k];
            if (ball == null) continue;
            Quaternion wanted = ball.parent.rotation * rest[k];
            if (target != null)
            {
                Vector3 to = target.Position + Vector3.up * 0.6f - ball.position;
                Vector3 front = ball.parent.rotation * rest[k] * Vector3.forward;
                if (to.sqrMagnitude > 0.0001f && Vector3.Angle(front, to) < MaxAngle) wanted = Quaternion.LookRotation(to, Vector3.up);
                else if (to.sqrMagnitude > 0.0001f) wanted = Quaternion.LookRotation(Vector3.RotateTowards(front, to, MaxAngle * Mathf.Deg2Rad, 0f), Vector3.up);
            }
            ball.rotation = Quaternion.RotateTowards(ball.rotation, wanted, TurnSpeed * Time.deltaTime);
        }
    }
}

public class DarkTowerRoom : MonoBehaviour
{
    static readonly List<DarkTowerRoom> all = new List<DarkTowerRoom>();
    Vector3 center;
    float radius, bottom, top;

    public void Init(Vector3 floorCenter, float roomRadius, float height)
    {
        center = floorCenter;
        radius = roomRadius;
        bottom = floorCenter.y - 0.3f;
        top = floorCenter.y + height;
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    public static bool Contains(Vector3 p)
    {
        foreach (var r in all)
            if (p.y >= r.bottom && p.y <= r.top + 1f && new Vector2(p.x - r.center.x, p.z - r.center.z).sqrMagnitude <= r.radius * r.radius) return true;
        return false;
    }
}

public class CemeteryArea : MonoBehaviour
{
    static readonly List<CemeteryArea> all = new List<CemeteryArea>();
    List<Rect> rects;
    List<(Vector2 center, float radius)> holes;
    float ground;

    public void Init(List<Rect> areas, List<(Vector2, float)> towers, float groundY)
    {
        rects = areas;
        holes = towers;
        ground = groundY;
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    // Pieds posés sur le gravier d'un cimetière (pas dans une Tour).
    public static bool Contains(Vector3 feet)
    {
        foreach (var a in all)
        {
            if (a.rects == null || Mathf.Abs(feet.y - a.ground) > 0.6f) continue;
            var flat = new Vector2(feet.x, feet.z);
            if (!a.rects.Exists(r => r.Contains(flat))) continue;
            if (a.holes != null && a.holes.Exists(h => (flat - h.center).sqrMagnitude < h.radius * h.radius)) continue;
            return true;
        }
        return false;
    }
}

// Grand lustre qui balance légèrement autour de son point d'accroche (purement visuel et local, sans son).
public class ChandelierSwing : MonoBehaviour
{
    float amplitude, period, phase;
    Vector3 axis;

    public void Init(System.Random rnd)
    {
        amplitude = 2.5f + (float)rnd.NextDouble() * 2f;
        period = 3.5f + (float)rnd.NextDouble() * 2f;
        phase = (float)rnd.NextDouble() * Mathf.PI * 2f;
        float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
        axis = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    void Update() => transform.localRotation = Quaternion.AngleAxis(amplitude * Mathf.Sin(Time.time * Mathf.PI * 2f / period + phase), axis);
}
