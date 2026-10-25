using System.Collections.Generic;
using UnityEngine;

// L'Araignée (entité ID 6, type Abjuration) : n'apparaît que pendant l'Abjuration (5 par zone déverrouillée, à plus de
// 25 m des joueurs). Plus grande qu'un joueur, elle rôde lentement dans les couloirs. Un joueur debout dans son champ de
// vision (12 m) qui bouge est attaqué : elle fonce sur lui, l'immobilise à 0,8 m, le frappe (dégâts, recul), s'arrête 1 seconde
// puis le poursuit 10 secondes à sa vitesse, en l'attaquant de nouveau si elle le rattrape. Un joueur accroupi, ou debout et
// immobile, passe inaperçu tant qu'elle rôde, sauf s'il aboie, saute, ramasse ou pose un objet sous ses yeux.
// Modèle Meshy découpé en corps et pattes rigides (le rig Meshy ne gère que les bipèdes) : les pattes sont animées ici.
public class Spider : Entity
{
    public enum State : byte { Prowl = 0, Rush = 1, Strike = 2, Pause = 3, Pursuit = 4 }

    const float EyeHeight = 0.9f, FieldOfView = 150f;
    const float StrikeDuration = 0.55f, StrikeHitTime = 0.3f;

    public override EntityKind Kind => EntityKind.Spider;
    public override string DisplayName => "L'Araignée";
    public override float ChestHeight => 0.9f;
    public State Current { get => (State)state; private set => state = (byte)value; }

    float stateTimer, pursuitTimer;
    bool hitDone;

    // Modèle Meshy (Resources/Art/Spider, découpé par "FunGame/Installer l'Araignée") : un corps et des pattes rigides,
    // chacune pivotant à sa hanche. Hauteur visée : un peu plus grande qu'un joueur.
    const float ModelScale = 1.35f, FeetDepth = 0.68f;

    class Leg { public Transform pivot; public Vector3 swingAxis, liftAxis; public float phase; public bool front; }
    readonly List<Leg> legs = new List<Leg>();
    Transform body;
    Light eyes;
    float gait, strikeAnim, twitch;

    protected override void BuildVisual()
    {
        body = new GameObject("Corps").transform;
        body.SetParent(transform, false);
        body.localPosition = new Vector3(0f, FeetDepth * ModelScale, 0f);
        var prefab = Resources.Load<GameObject>("Art/Spider");
        if (prefab != null)
        {
            var model = Instantiate(prefab, body);
            model.name = "Modele";
            model.transform.localScale = Vector3.one * ModelScale;
            var parts = new List<Transform>();
            foreach (Transform t in model.transform) if (t.name.StartsWith("Patte")) parts.Add(t);
            // Démarche alternée : de l'avant vers l'arrière, une patte sur deux, décalée d'un côté à l'autre.
            foreach (float side in new[] { -1f, 1f })
            {
                var row = parts.FindAll(t => Mathf.Sign(t.localPosition.x) == side);
                row.Sort((a, b) => b.localPosition.z.CompareTo(a.localPosition.z));
                for (int k = 0; k < row.Count; k++)
                {
                    Vector3 outward = row[k].GetComponent<MeshFilter>().sharedMesh.bounds.center;
                    outward.y = 0f;
                    if (outward.sqrMagnitude < 0.0001f) outward = new Vector3(side, 0f, 0f);
                    legs.Add(new Leg
                    {
                        pivot = row[k],
                        swingAxis = Vector3.up,
                        liftAxis = Vector3.Cross(outward.normalized, Vector3.up),
                        phase = (k % 2 == 0 ? 0f : Mathf.PI) + (side > 0f ? Mathf.PI : 0f),
                        front = k == 0,
                    });
                }
            }
        }
        else
        {
            Material skin = GameArt.Lit("SpiderSkin", new Color(0.05f, 0.05f, 0.055f), null, 0.35f);
            GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(0f, 0.12f, -0.4f), new Vector3(0.95f, 0.8f, 1.1f), skin);
            GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(0f, 0f, 0.3f), new Vector3(0.6f, 0.45f, 0.6f), skin);
        }
        // Lueur rouge des yeux : on les devine dans le noir avant de voir l'Araignée.
        eyes = new GameObject("Yeux").AddComponent<Light>();
        eyes.transform.SetParent(body, false);
        eyes.transform.localPosition = new Vector3(0f, 0.25f, 0.85f) * ModelScale;
        eyes.type = LightType.Point;
        eyes.color = new Color(1f, 0.08f, 0.04f);
        eyes.range = 2.6f;
        eyes.intensity = 1.5f;
        eyes.shadows = LightShadows.None;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        stateTimer += dt;
        var target = Find(players, TargetId);
        switch (Current)
        {
            case State.Prowl:
                Speed = GameRules.BaseSpeed * GameRules.SpiderProwlMultiplier;
                foreach (var p in players)
                    if (Valid(p) && Notices(p)) { BeginRush(p.PlayerId); return; }
                Wander(dt, 6f, 30f);
                break;

            case State.Rush:
            case State.Pursuit:
                if (!Valid(target)) { Prowl(); break; }
                if (Current == State.Pursuit)
                {
                    pursuitTimer -= dt;
                    if (pursuitTimer <= 0f) { Prowl(); break; }
                }
                Speed = GameRules.BaseSpeed * (Current == State.Rush ? GameRules.SpiderRushMultiplier : 1f);
                Pursue(target, dt);
                if (InReach(target, GameRules.SpiderAttackDistance)) BeginStrike();
                else if (Current == State.Rush && stateTimer > 8f) Prowl();
                break;

            case State.Strike:
                Speed = 0f;
                if (target != null) Face(target.Position, dt);
                if (!hitDone && stateTimer >= StrikeHitTime)
                {
                    hitDone = true;
                    if (target != null && InReach(target, GameRules.SpiderAttackDistance + 1f))
                        Host?.Hit(this, TargetId, GameRules.SpiderDamage, GameRules.SpiderKnockback);
                }
                if (stateTimer >= StrikeDuration) { Current = State.Pause; stateTimer = 0f; }
                break;

            case State.Pause:
                Speed = 0f;
                if (stateTimer >= GameRules.SpiderPause)
                {
                    // Puis elle le poursuit 10 secondes (une seule fois par attaque initiale).
                    if (pursuitTimer <= 0f) pursuitTimer = GameRules.SpiderPursuit;
                    Current = State.Pursuit;
                    stateTimer = 0f;
                    repathTimer = 0f;
                }
                break;
        }
    }

    // Dans son champ de vision (12 m) : debout et en mouvement, ou trahi par une action bruyante
    // (accroupi ou debout immobile).
    bool Notices(PlayerPresence p)
    {
        if (!Sees(p, EyeHeight, GameRules.SpiderSightRange, FieldOfView, false)) return false;
        if (!p.Sneaking && !p.Still) return true;
        return Host != null && Host.NoiseAge(p.PlayerId) < GameRules.NoiseMemory;
    }

    void BeginRush(string playerId)
    {
        Current = State.Rush;
        TargetId = playerId;
        stateTimer = 0f;
        pursuitTimer = 0f;
        repathTimer = 0f;
        Pulse++; // sifflement
    }

    void BeginStrike()
    {
        Current = State.Strike;
        stateTimer = 0f;
        hitDone = false;
        ClearPath();
        Host?.Immobilize(this, TargetId, StrikeDuration);
    }

    void Prowl()
    {
        Current = State.Prowl;
        TargetId = "";
        stateTimer = 0f;
        pursuitTimer = 0f;
        ClearPath();
    }

    // ================= Affichage =================

    protected override void OnPulse() => GameAudio.Play3D("spider_hiss", transform.position + Vector3.up, 1f, 25f, 0.08f);

    // V0.4.9 : Araignee_Attaque quand elle blesse un joueur, Araignee_Tuer quand elle le tue (joués chez tous).
    public override string HurtSound => "spider_attack";
    public override string KillSound => "spider_kill";
    // GDD 12 (V0.5.2) : Araignee_Attaque et Araignee_Tuer, entendus par tous les joueurs, spatialisés.
    public override bool SoundsShared => true;

    protected override void Animate(float dt)
    {
        float speed = Mathf.Clamp(visualSpeed, 0f, 12f);
        float amount = Mathf.Clamp01(speed / 1.2f);
        // Démarche saccadée : la cadence hésite, accélère, se fige un instant.
        float jerk = Mathf.PerlinNoise(Time.time * 2.7f, Id * 3.1f);
        gait += dt * (2f + speed * 3.4f) * Mathf.Lerp(0.15f, 1.9f, jerk);
        strikeAnim = Mathf.MoveTowards(strikeAnim, Current == State.Strike ? 1f : 0f, dt / 0.12f);
        // Petits soubresauts nerveux du corps, plus forts quand elle chasse.
        bool hunting = Current == State.Rush || Current == State.Pursuit || Current == State.Strike;
        twitch = Mathf.Lerp(twitch, (Mathf.PerlinNoise(Time.time * 9f, Id) - 0.5f) * (hunting ? 9f : 3f), 1f - Mathf.Exp(-20f * dt));

        body.localPosition = new Vector3(0f, FeetDepth * ModelScale + Mathf.Sin(gait * 2f) * 0.035f * amount + strikeAnim * 0.25f, 0f);
        body.localRotation = Quaternion.Euler(-strikeAnim * 24f + twitch * 0.4f, twitch, Mathf.Sin(gait) * 2.5f * amount);

        foreach (var leg in legs)
        {
            float s = Mathf.Sin(gait + leg.phase);
            float lift = Mathf.Max(0f, Mathf.Cos(gait + leg.phase)) * amount * 14f;
            // Les pattes avant se dressent pour frapper.
            if (leg.front) lift += strikeAnim * 38f;
            leg.pivot.localRotation = Quaternion.AngleAxis(s * 13f * amount, leg.swingAxis) * Quaternion.AngleAxis(lift, leg.liftAxis);
        }
        eyes.intensity = (hunting ? 2.2f : 1.3f) * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 6f, Id + 7f));
    }
}
