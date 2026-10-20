using System.Collections.Generic;
using UnityEngine;

// La Tour (entité ID 3, type Cauchemar) : grande entité bipède armée d'une épée, à tête de tour médiévale, de la taille
// de La Dame. Elle arpente le terrain lentement (pas lourds de pierre audibles à 30 m) ; toutes les 7 à 15 secondes (V0.4.3),
// elle s'arrête 2 secondes pour regarder autour d'elle en respirant fort, puis continue (70 %) ou fait demi-tour (30 %).
// Elle voit à 50 m devant elle (les murs bloquent la vue). Un joueur repéré entend une alerte ; sa position est mémorisée
// (localisation tampon) et la Tour se prépare 3 secondes. S'il est encore en vue, il est immobilisé et la Tour le charge
// à une vitesse fulgurante, à travers obstacles et dénivelés. S'il s'est caché, elle va voir la localisation tampon.
public class Tour : Entity
{
    public enum State : byte { Walk = 0, Survey = 1, Channel = 2, Charge = 3, Investigate = 4 }

    const float EyeHeight = 2.3f, FieldOfView = 120f;
    const float ImpactDistance = 1.1f;

    public override EntityKind Kind => EntityKind.Tour;
    public override string DisplayName => "La Tour";
    public override float ChestHeight => 1.5f;
    public override bool Nightmare => true;
    public override Vector3 EyesLocal => new Vector3(0f, EyeHeight, 0.3f);
    public State Current { get => (State)state; private set => state = (byte)value; }

    float surveyTimer, stateTimer, stepTimer;
    Vector3 buffer, chargeFrom, chargeTo;
    bool turnBack;

    protected override void Init() => surveyTimer = Range(GameRules.TourSurveyMin, GameRules.TourSurveyMax);

    protected override void BuildVisual()
    {
        if (LoadCreature("TourArt"))
        {
            // Le modèle Meshy a perdu son épée : une longue lame en primitives, tenue dans la main droite.
            Transform hand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand != null) BuildSword(hand);
            return;
        }
        Material stone = GameArt.Lit("TourStone", new Color(0.32f, 0.32f, 0.33f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
        GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.5f), stone);
        GameArt.Prim(PrimitiveType.Cube, transform, new Vector3(0f, 2.15f, 0f), new Vector3(0.9f, 0.8f, 0.9f), stone);
    }

    void BuildSword(Transform hand)
    {
        var sword = new GameObject("Epee").transform;
        sword.SetParent(hand, false);
        // Échelle monde fixe malgré l'échelle du modèle.
        float s = 1f / Mathf.Max(0.0001f, hand.lossyScale.x);
        sword.localScale = Vector3.one * s;
        sword.localRotation = Quaternion.Euler(0f, 0f, -90f);
        Material steel = GameArt.Lit("TourSteel", new Color(0.42f, 0.43f, 0.45f), null, 0.75f);
        Material grip = GameArt.Lit("TourGrip", new Color(0.12f, 0.1f, 0.09f), null, 0.2f);
        GameArt.Prim(PrimitiveType.Cube, sword, new Vector3(0f, 0.75f, 0f), new Vector3(0.07f, 1.3f, 0.02f), steel);
        GameArt.Prim(PrimitiveType.Cube, sword, new Vector3(0f, 0.08f, 0f), new Vector3(0.32f, 0.04f, 0.05f), steel);
        GameArt.Prim(PrimitiveType.Cylinder, sword, new Vector3(0f, -0.04f, 0f), new Vector3(0.04f, 0.1f, 0.04f), grip);
    }

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        stateTimer += dt;
        switch (Current)
        {
            case State.Walk:
            case State.Investigate:
            {
                // Toujours à l'affût, même en allant vers la localisation tampon.
                foreach (var p in players)
                    if (Valid(p) && Sees(p, EyeHeight, GameRules.TourSightRange, FieldOfView)) { Spot(p); return; }

                if (Current == State.Investigate)
                {
                    Speed = GameRules.BaseSpeed * GameRules.TourInvestigateMultiplier;
                    FollowPath(dt);
                    if (PathDone) BeginSurvey();
                    break;
                }
                Speed = GameRules.BaseSpeed * GameRules.TourWalkMultiplier;
                surveyTimer -= dt;
                if (surveyTimer <= 0f) { BeginSurvey(); break; }
                Wander(dt);
                break;
            }

            case State.Survey:
                Speed = 0f;
                foreach (var p in players)
                    if (Valid(p) && Sees(p, EyeHeight, GameRules.TourSightRange, 360f)) { Spot(p); return; }
                if (stateTimer >= GameRules.TourSurveyDuration)
                {
                    Current = State.Walk;
                    surveyTimer = Range(GameRules.TourSurveyMin, GameRules.TourSurveyMax);
                    // 30 % : demi-tour, vers un point derrière elle ; sinon elle reprend sa route.
                    turnBack = rnd.NextDouble() < GameRules.TourTurnBackChance;
                    if (turnBack || PathDone) PlanPath(NodeAround(turnBack ? -transform.forward : transform.forward), 0f);
                }
                break;

            case State.Channel:
            {
                Speed = 0f;
                var target = Find(players, TargetId);
                if (target != null) Face(target.Position, dt);
                if (stateTimer < GameRules.TourChannel) break;
                if (Valid(target) && Sees(target, EyeHeight, GameRules.TourSightRange, 360f))
                {
                    // La charge : le joueur est immobilisé, la Tour fonce droit sur lui.
                    Current = State.Charge;
                    stateTimer = 0f;
                    chargeFrom = transform.position;
                    chargeTo = Feet(target);
                    float duration = Vector3.Distance(chargeFrom, chargeTo) / GameRules.TourChargeSpeed;
                    Host?.Immobilize(this, TargetId, duration + 0.3f);
                }
                else
                {
                    // Le joueur s'est caché : la Tour va voir la localisation tampon.
                    TargetId = "";
                    Current = State.Investigate;
                    stateTimer = 0f;
                    PlanPath(nav.NodeAt(buffer), 0f);
                }
                break;
            }

            case State.Charge:
            {
                var target = Find(players, TargetId);
                if (Valid(target)) chargeTo = Feet(target);
                Vector3 d = chargeTo - transform.position;
                float step = GameRules.TourChargeSpeed * dt;
                if (Horizontal(d).sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(Horizontal(d));
                // À travers murs et dénivelés : en ligne droite.
                transform.position = d.magnitude <= step ? chargeTo : transform.position + d.normalized * step;
                if (d.magnitude > ImpactDistance && stateTimer < 3f) break;
                if (target != null && InReach(target, ImpactDistance + 0.6f, 2.5f))
                    Host?.Hit(this, TargetId, GameRules.TourDamage, GameRules.TourKnockback);
                Pulse++; // impact
                // Elle se repose sur la grille de navigation la plus proche.
                int node = nav.NodeAt(transform.position);
                if (node >= 0) transform.position = nav.Position(node);
                TargetId = "";
                BeginSurvey();
                break;
            }
        }
    }

    void Spot(PlayerPresence p)
    {
        Current = State.Channel;
        stateTimer = 0f;
        TargetId = p.PlayerId;
        buffer = Feet(p);
        ClearPath();
        Host?.Alert(this, p.PlayerId);
    }

    void BeginSurvey()
    {
        Current = State.Survey;
        stateTimer = 0f;
        Speed = 0f;
    }

    // Un point de la grille de sa zone, à 10-40 m, dans la direction voulue (demi-tour ou tout droit).
    int NodeAround(Vector3 direction)
    {
        int here = nav.NodeAt(transform.position);
        int zone = here >= 0 ? nav.ZoneOfNode(here) : 0;
        int best = -1;
        for (int k = 0; k < 12; k++)
        {
            int node = nav.RandomNode(zone, rnd, transform.position, 10f, 40f);
            if (node < 0) continue;
            best = node;
            if (Vector3.Dot(Horizontal(nav.Position(node) - transform.position), direction) > 0f) break;
        }
        return best;
    }

    // ================= Affichage =================

    protected override void OnStateChanged(byte previous, byte next)
    {
        Vector3 head = transform.position + Vector3.up * 2.2f;
        switch ((State)next)
        {
            case State.Survey: GameAudio.Play3D("tour_breath", head, 0.9f, 25f, 0.08f); break;
            case State.Channel: GameAudio.Play3D("tour_charge", head, 0.8f, 35f, 0.05f); break;
        }
    }

    // Impact de la charge.
    protected override void OnPulse() => GameAudio.Play3D("tour_impact", transform.position + Vector3.up, 1f, 35f, 0.05f);

    protected override void Animate(float dt)
    {
        // Pas lourds, comme de la pierre (son spatialisé à 30 m).
        if (visualSpeed > 0.3f && Current != State.Charge)
        {
            stepTimer -= dt * Mathf.Clamp(visualSpeed / 2.2f, 0.6f, 2.2f);
            if (stepTimer <= 0f)
            {
                stepTimer = 0.62f;
                GameAudio.Play3D("tour_step", transform.position, 0.9f, GameRules.TourStepRange, 0.12f);
            }
        }
        if (animator == null) return;
        string wanted = Current == State.Survey ? "Look" : Current == State.Channel ? "Stance"
            : Current == State.Charge || Current == State.Investigate ? "Run" : visualSpeed < 0.3f ? "Idle" : "Walk";
        PlayAnim(wanted);
        animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 1.6f, 0.5f, 1.6f) : wanted == "Run" && Current == State.Charge ? 2.5f : 1f;
    }
}
