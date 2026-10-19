using System.Collections.Generic;
using UnityEngine;

// L'Araignée (entité ID 6, type Abjuration) : n'apparaît que pendant l'Abjuration (5 par zone déverrouillée, à plus de
// 25 m des joueurs). Plus grande qu'un joueur, elle rôde lentement dans les couloirs. Un joueur debout dans son champ de
// vision (12 m) est attaqué : elle fonce sur lui, l'immobilise à 0,8 m, le frappe (dégâts, recul), s'arrête 1 seconde puis
// le poursuit 10 secondes à sa vitesse, en l'attaquant de nouveau si elle le rattrape. Un joueur accroupi passe inaperçu,
// sauf s'il aboie, saute, ramasse ou pose un objet sous ses yeux.
// Modèle low-poly construit en code : le rig Meshy ne gère que les bipèdes ; les huit pattes sont animées ici.
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

    // Pattes : hanche (pivot), segment haut, genou, segment bas.
    class Leg { public Transform hip, knee; public float phase, side, baseYaw; }
    readonly List<Leg> legs = new List<Leg>();
    Transform body;
    float gait, strikeAnim;

    protected override void BuildVisual()
    {
        Material skin = GameArt.Lit("SpiderSkin", new Color(0.05f, 0.05f, 0.055f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 31), 0.35f);
        Material eye = GameArt.Emissive("SpiderEye", new Color(0.4f, 0f, 0f), new Color(2.2f, 0.12f, 0.05f));
        Material drool = GameArt.Lit("SpiderDrool", new Color(0.3f, 0.02f, 0.02f), null, 0.9f);

        body = new GameObject("Corps").transform;
        body.SetParent(transform, false);
        body.localPosition = new Vector3(0f, 0.95f, 0f);
        GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(0f, 0.12f, -0.55f), new Vector3(0.95f, 0.8f, 1.1f), skin); // abdomen
        GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(0f, 0f, 0.15f), new Vector3(0.62f, 0.45f, 0.62f), skin);    // céphalothorax
        GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(0f, -0.02f, 0.45f), new Vector3(0.38f, 0.32f, 0.3f), skin);  // tête
        GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(-0.09f, 0.05f, 0.58f), Vector3.one * 0.1f, eye);
        GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(0.09f, 0.05f, 0.58f), Vector3.one * 0.1f, eye);
        GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(-0.04f, 0.13f, 0.56f), Vector3.one * 0.05f, eye);
        GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(0.04f, 0.13f, 0.56f), Vector3.one * 0.05f, eye);
        // Crochets et filets de bave.
        GameArt.Prim(PrimitiveType.Capsule, body, new Vector3(-0.06f, -0.2f, 0.55f), new Vector3(0.04f, 0.1f, 0.04f), skin);
        GameArt.Prim(PrimitiveType.Capsule, body, new Vector3(0.06f, -0.2f, 0.55f), new Vector3(0.04f, 0.1f, 0.04f), skin);
        GameArt.Prim(PrimitiveType.Cylinder, body, new Vector3(0.02f, -0.36f, 0.56f), new Vector3(0.012f, 0.14f, 0.012f), drool);
        GameArt.Prim(PrimitiveType.Cylinder, body, new Vector3(-0.03f, -0.32f, 0.54f), new Vector3(0.01f, 0.1f, 0.01f), drool);

        for (int i = 0; i < 8; i++)
        {
            float side = i < 4 ? -1f : 1f;
            int k = i % 4;
            var leg = new Leg
            {
                side = side,
                // Pattes alternées (tétrapode) : 1-3 d'un côté avec 2-4 de l'autre.
                phase = (k % 2 == 0 ? 0f : Mathf.PI) + (side > 0 ? Mathf.PI : 0f),
                baseYaw = side * (35f + k * 35f),
            };
            leg.hip = new GameObject("Hanche" + i).transform;
            leg.hip.SetParent(body, false);
            leg.hip.localPosition = new Vector3(side * 0.22f, 0f, 0.28f - k * 0.13f);
            GameArt.Prim(PrimitiveType.Capsule, leg.hip, new Vector3(0f, 0f, 0.42f), new Vector3(0.09f, 0.45f, 0.09f), skin)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            leg.knee = new GameObject("Genou").transform;
            leg.knee.SetParent(leg.hip, false);
            leg.knee.localPosition = new Vector3(0f, 0f, 0.85f);
            GameArt.Prim(PrimitiveType.Capsule, leg.knee, new Vector3(0f, 0f, 0.7f), new Vector3(0.07f, 0.72f, 0.07f), skin)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            legs.Add(leg);
        }
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

    // Dans son champ de vision (12 m) : debout, ou accroupi mais trahi par une action bruyante.
    bool Notices(PlayerPresence p)
    {
        if (!Sees(p, EyeHeight, GameRules.SpiderSightRange, FieldOfView, false)) return false;
        if (!p.Sneaking) return true;
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

    protected override void OnStateChanged(byte previous, byte next)
    {
        if ((State)next == State.Strike) GameAudio.Play3D("spider_attack", transform.position + Vector3.up, 1f, 25f, 0.06f);
    }

    protected override void Animate(float dt)
    {
        float speed = Mathf.Clamp(visualSpeed, 0f, 12f);
        gait += dt * (2f + speed * 3.2f);
        float amount = Mathf.Clamp01(speed / 1.2f);
        strikeAnim = Mathf.MoveTowards(strikeAnim, Current == State.Strike ? 1f : 0f, dt / 0.15f);

        // Le corps tangue légèrement et se cabre pour frapper.
        body.localPosition = new Vector3(0f, 0.95f + Mathf.Sin(gait * 2f) * 0.03f * amount + strikeAnim * 0.2f, 0f);
        body.localRotation = Quaternion.Euler(-strikeAnim * 22f, 0f, Mathf.Sin(gait) * 2f * amount);

        for (int i = 0; i < legs.Count; i++)
        {
            Leg leg = legs[i];
            float s = Mathf.Sin(gait + leg.phase);
            float lift = Mathf.Max(0f, Mathf.Cos(gait + leg.phase)) * amount;
            float yaw = leg.baseYaw + s * 18f * amount;
            float raise = -28f + lift * -18f;
            // Les deux pattes avant se lèvent pour l'attaque.
            bool front = i % 4 == 0;
            if (front) raise -= strikeAnim * 50f;
            leg.hip.localRotation = Quaternion.Euler(raise, yaw, 0f);
            leg.knee.localRotation = Quaternion.Euler(100f + lift * 14f - (front ? strikeAnim * 40f : 0f), 0f, 0f);
        }
    }
}
