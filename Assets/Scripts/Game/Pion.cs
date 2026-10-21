using System.Collections.Generic;
using UnityEngine;

// Le Pion (entité ID 4, type Normal) : petite entité à grosse tête, plus petite qu'un joueur, qui marche à la vitesse
// d'un joueur. Les Pions apparaissent par groupes de 1 à 3 et vivent à 30 m au plus de leur point d'apparition.
// Un joueur qui entre dans cette zone est observé à 5 m (état "curieux") et suivi à distance ; au bout de 4 à 8 secondes,
// le Pion crie et court vers lui. Au contact : dégâts, recul, et vol d'un objet ramassable (le Canard), qu'il emporte
// sur sa tête pour le déposer plus loin dans sa zone. Ramasser un objet volé déclenche son attaque sur-le-champ.
// En état curieux, il lui arrive de sauter sur place (V0.4.6 ; animation locale, sans effet sur le jeu).
public class Pion : Entity
{
    public enum State : byte { Idle = 0, Approach = 1, Curious = 2, Rush = 3, Flee = 4, Rest = 5 }

    const float Height = 1.15f;
    const float TouchDistance = 0.75f;
    const float RushTimeout = 6f, RestDuration = 3f;

    public override EntityKind Kind => EntityKind.Pion;
    public override string DisplayName => "Le Pion";
    public override float ChestHeight => 0.6f;
    public State Current { get => (State)state; private set => state = (byte)value; }

    public Vector3 Home { get; set; }
    public ItemType Carried { get; private set; }

    float curiousTimer, stateTimer;
    Vector3 fleeGoal;
    bool stealPending;

    // Affichage : jambes animées en code, objet volé sur la tête.
    Transform body, head, legL, legR, carriedVisual;
    ItemType shownCarried;
    float walkPhase, hopTimer = -1f, hopNext;
    const float HopDuration = 0.45f, HopHeight = 0.35f;

    protected override void Init() => Home = transform.position;

    protected override void BuildVisual()
    {
        Material cloak = GameArt.Lit("PionCloak", new Color(0.55f, 0.08f, 0.06f), null, 0.1f);
        Material black = GameArt.Lit("PionLeg", new Color(0.04f, 0.04f, 0.04f), null, 0.1f);
        // Tout le corps sous un nœud qui peut sauter sans toucher à la position de l'entité.
        body = new GameObject("Corps").transform;
        body.SetParent(transform, false);
        // Cape rouge et deux jambes fines, noires.
        GameArt.Prim(PrimitiveType.Cylinder, body, new Vector3(0f, 0.38f, 0f), new Vector3(0.36f, 0.14f, 0.3f), cloak);
        GameArt.Prim(PrimitiveType.Sphere, body, new Vector3(0f, 0.3f, -0.03f), new Vector3(0.42f, 0.2f, 0.36f), cloak);
        legL = Leg(-0.07f, black);
        legR = Leg(0.07f, black);
        head = new GameObject("Tete").transform;
        head.SetParent(body, false);
        head.localPosition = new Vector3(0f, 0.46f, 0f);
        // Tête Meshy (visage de vieillard surmonté de deux grandes cornes), face vers +Z.
        if (GameArt.SpawnModel("PionHead", head, Height - 0.46f) == null)
        {
            Material skin = GameArt.Lit("PionSkin", new Color(0.82f, 0.74f, 0.62f), null, 0.2f);
            GameArt.Prim(PrimitiveType.Sphere, head, new Vector3(0f, 0.1f, 0f), new Vector3(0.24f, 0.22f, 0.22f), skin);
            GameArt.Prim(PrimitiveType.Capsule, head, new Vector3(-0.07f, 0.32f, 0f), new Vector3(0.06f, 0.16f, 0.06f), skin);
            GameArt.Prim(PrimitiveType.Capsule, head, new Vector3(0.07f, 0.32f, 0f), new Vector3(0.06f, 0.16f, 0.06f), skin);
        }
        foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    Transform Leg(float x, Material mat)
    {
        var pivot = new GameObject("Jambe").transform;
        pivot.SetParent(body, false);
        pivot.localPosition = new Vector3(x, 0.3f, 0f);
        GameArt.Prim(PrimitiveType.Cylinder, pivot, new Vector3(0f, -0.15f, 0f), new Vector3(0.035f, 0.15f, 0.035f), mat);
        return pivot;
    }

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        stateTimer += dt;
        var target = Find(players, TargetId);
        switch (Current)
        {
            case State.Idle:
            {
                Speed = GameRules.BaseSpeed * 0.5f;
                // Le joueur le plus proche dans sa zone d'habitat.
                PlayerPresence intruder = null;
                float best = float.MaxValue;
                foreach (var p in players)
                {
                    if (!Valid(p) || !InHabitat(p.Position)) continue;
                    float d = HorizontalDistance(p);
                    if (d < best) { best = d; intruder = p; }
                }
                if (intruder != null)
                {
                    TargetId = intruder.PlayerId;
                    Current = State.Approach;
                    stateTimer = 0f;
                    repathTimer = 0f;
                    break;
                }
                // Petits tours près de son point d'apparition.
                if (PathDone && stateTimer > 2f)
                {
                    stateTimer = 0f;
                    PlanPath(NodeNear(Home, 8f), 0f);
                }
                FollowPath(dt);
                break;
            }

            case State.Approach:
            case State.Curious:
            {
                if (!Valid(target) || !InHabitat(target.Position)) { Calm(); break; }
                Speed = GameRules.BaseSpeed;
                float d = HorizontalDistance(target);
                if (Current == State.Approach && d <= GameRules.PionWatchDistance + 0.3f)
                {
                    Current = State.Curious;
                    curiousTimer = Range(GameRules.PionCuriousMin, GameRules.PionCuriousMax);
                }
                // Il garde ses distances : il suit le joueur jusqu'à 5 m, sans s'approcher plus.
                if (d > GameRules.PionWatchDistance) Pursue(target, dt);
                else Face(target.Position, dt);
                if (Current == State.Curious)
                {
                    curiousTimer -= dt;
                    if (curiousTimer <= 0f) BeginRush(target.PlayerId);
                }
                break;
            }

            case State.Rush:
            {
                if (!Valid(target) || stateTimer > RushTimeout) { Calm(); break; }
                Speed = GameRules.BaseSpeed * GameRules.PionRushMultiplier;
                Pursue(target, dt);
                if (InReach(target, TouchDistance))
                {
                    Host?.Hit(this, TargetId, GameRules.PionDamage, GameRules.PionKnockback);
                    // Repos en attendant la réponse du joueur (immédiate quand c'est l'hôte : OnStolen peut déjà fuir).
                    Current = State.Rest;
                    stateTimer = 0f;
                    ClearPath();
                    stealPending = true;
                    Host?.Steal(this, TargetId, OnStolen);
                }
                break;
            }

            case State.Flee:
                Speed = GameRules.BaseSpeed * GameRules.PionRushMultiplier;
                FollowPath(dt);
                if (PathDone || stateTimer > 15f)
                {
                    Host?.DropStolen(this, Carried, transform.position);
                    Carried = ItemType.None;
                    Calm();
                }
                break;

            case State.Rest:
                Speed = 0f;
                // Sans réponse du joueur (déconnecté), il n'attend pas indéfiniment.
                if (stateTimer >= RestDuration && (!stealPending || stateTimer > RestDuration * 3f)) { stealPending = false; Calm(); }
                break;
        }
    }

    void OnStolen(ItemType type)
    {
        stealPending = false;
        if (type == ItemType.None || this == null) return;
        Carried = type;
        // Il part loin du joueur, dans les limites de sa zone d'habitat.
        Current = State.Flee;
        stateTimer = 0f;
        TargetId = "";
        Vector3 away = Horizontal(transform.position - Home);
        PlanPath(NodeNear(Home + (away.sqrMagnitude > 1f ? -away.normalized : Random.insideUnitSphere) * 15f, 12f), 0f);
    }

    // Un joueur ramasse l'objet que ce Pion avait volé : il l'attaque sur-le-champ.
    public void Provoke(string playerId) => BeginRush(playerId);

    void BeginRush(string playerId)
    {
        Current = State.Rush;
        TargetId = playerId;
        stateTimer = 0f;
        repathTimer = 0f;
        Pulse++; // cri
    }

    void Calm()
    {
        Current = State.Idle;
        TargetId = "";
        stateTimer = 0f;
        ClearPath();
    }

    bool InHabitat(Vector3 p) => Horizontal(p - Home).magnitude <= GameRules.PionHabitat;

    int NodeNear(Vector3 center, float radius)
    {
        int here = nav.NodeAt(transform.position);
        int zone = here >= 0 ? nav.ZoneOfNode(here) : 0;
        int best = -1;
        float bestDist = float.MaxValue;
        for (int k = 0; k < 16; k++)
        {
            int node = nav.RandomNode(zone, rnd, transform.position, 2f, GameRules.PionHabitat * 2f);
            if (node < 0) continue;
            float d = Horizontal(nav.Position(node) - center).magnitude;
            if (d < bestDist) { bestDist = d; best = node; }
            if (d < radius && Horizontal(nav.Position(node) - Home).magnitude <= GameRules.PionHabitat) return node;
        }
        return best;
    }

    public override string Extra => ((int)Carried).ToString();
    protected override void ApplyExtra(string extra) => Carried = (ItemType)NetworkGame.I(extra);

    // ================= Affichage =================

    protected override void OnPulse() =>
        GameAudio.Play3D("pion_cry", transform.position + Vector3.up * 0.7f, 1f, 30f, 0.1f);

    protected override void Animate(float dt)
    {
        walkPhase += dt * visualSpeed * 9f;
        float swing = Mathf.Sin(walkPhase) * Mathf.Clamp01(visualSpeed / 1.5f) * 35f;

        // Curieux et presque immobile : de temps en temps, un petit saut sur place, jambes repliées.
        if (hopTimer >= 0f)
        {
            hopTimer += dt;
            float k = hopTimer / HopDuration;
            body.localPosition = Vector3.up * (4f * k * (1f - k) * HopHeight);
            swing = Mathf.Sin(k * Mathf.PI) * 40f;
            if (k >= 1f) { hopTimer = -1f; body.localPosition = Vector3.zero; }
        }
        else if (Current == State.Curious && visualSpeed < 1f)
        {
            hopNext -= dt;
            if (hopNext <= 0f)
            {
                hopNext = Random.Range(1.5f, 4f);
                if (Random.value < 0.5f) hopTimer = 0f;
            }
        }
        legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        // La grosse tête dodeline en marchant.
        head.localPosition = new Vector3(0f, 0.46f + Mathf.Abs(Mathf.Sin(walkPhase)) * 0.03f * Mathf.Clamp01(visualSpeed), 0f);
        head.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(walkPhase * 0.5f) * 4f * Mathf.Clamp01(visualSpeed));

        if (shownCarried != Carried)
        {
            shownCarried = Carried;
            if (carriedVisual != null) Destroy(carriedVisual.gameObject);
            carriedVisual = null;
            if (Carried != ItemType.None)
            {
                carriedVisual = new GameObject("ObjetVole").transform;
                carriedVisual.SetParent(head, false);
                carriedVisual.localPosition = new Vector3(0f, Height - 0.4f, 0f);
                if (GameArt.SpawnModel(Items.ModelName(Carried), carriedVisual, 0.3f) == null)
                    GameArt.Prim(PrimitiveType.Sphere, carriedVisual, new Vector3(0f, 0.12f, 0f), Vector3.one * 0.22f,
                        GameArt.Lit("Item" + Carried, Items.Get(Carried).color, null, 0.8f));
            }
        }
    }
}
