using System.Collections.Generic;
using UnityEngine;

// La Tour (entité ID 3, type Cauchemar) : grande entité bipède armée d'une épée, à tête de tour médiévale, de la taille
// de La Dame. Elle arpente le terrain lentement (pas lourds de pierre audibles à 30 m) ; toutes les 7 à 15 secondes (V0.4.3),
// elle s'arrête 2 secondes pour regarder autour d'elle en respirant fort, puis continue (70 %) ou fait demi-tour (30 %).
// Elle voit à 50 m devant elle (les murs bloquent la vue). Un joueur repéré à plus de 20 m (distance d'attaque) voit la
// Tour courir vers lui (V0.4.6) ; à 20 m, il entend une alerte, sa position est mémorisée (localisation tampon) et la Tour
// se prépare 3 secondes. S'il est encore en vue, il est immobilisé et la Tour le charge à une vitesse fulgurante, à travers
// obstacles et dénivelés. S'il s'est caché, elle court vers la localisation tampon.
public class Tour : Entity
{
    public enum State : byte { Walk = 0, Survey = 1, Channel = 2, Charge = 3, Investigate = 4, Approach = 5 }

    const float EyeHeight = 2.3f * GameRules.BigEntityScale, FieldOfView = 120f;
    const float ImpactDistance = 1.1f;

    public override EntityKind Kind => EntityKind.Tour;
    public override string DisplayName => "La Tour";
    public override float ChestHeight => 1.5f * GameRules.BigEntityScale;
    protected override float ModelScale => GameRules.BigEntityScale;
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
            if (animator.isHuman)
            {
                // L'accroupissement modifie la pose après l'Animator : il doit la recalculer à chaque image, même hors champ.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                legs = new[]
                {
                    (animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), animator.GetBoneTransform(HumanBodyBones.LeftFoot)),
                    (animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), animator.GetBoneTransform(HumanBodyBones.RightFoot)),
                };
            }
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
                    Speed = GameRules.BaseSpeed * GameRules.TourRunMultiplier;
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

            case State.Approach:
            {
                // Elle court vers le joueur repéré jusqu'à sa distance d'attaque.
                var target = Find(players, TargetId);
                bool seen = Valid(target) && Sees(target, EyeHeight, GameRules.TourSightRange, 360f);
                if (seen) buffer = Feet(target);
                if (!seen)
                {
                    // Perdu de vue en chemin : elle court là où elle l'a vu pour la dernière fois.
                    TargetId = "";
                    Current = State.Investigate;
                    stateTimer = 0f;
                    PlanPath(nav.NodeAt(buffer), 0f);
                    break;
                }
                if (Horizontal(target.Position - transform.position).magnitude <= GameRules.TourAttackDistance) { BeginChannel(target); break; }
                Speed = GameRules.BaseSpeed * GameRules.TourRunMultiplier;
                Pursue(target, dt);
                break;
            }

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

    // Joueur repéré : trop loin, elle court vers lui ; à sa distance d'attaque, elle canalise.
    void Spot(PlayerPresence p)
    {
        if (Horizontal(p.Position - transform.position).magnitude <= GameRules.TourAttackDistance) { BeginChannel(p); return; }
        Current = State.Approach;
        stateTimer = 0f;
        TargetId = p.PlayerId;
        buffer = Feet(p);
        ClearPath();
        repathTimer = 0f;
    }

    void BeginChannel(PlayerPresence p)
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
        Vector3 mouth = transform.position + Vector3.up * 2.2f;
        switch ((State)next)
        {
            case State.Survey: GameAudio.Play3D("tour_breath", mouth, 0.9f, 25f, 0.08f); break;
            case State.Channel:
                channelShownAt = Time.time;
                // La fin du son coïncide avec la fin de la canalisation (GDD 12).
                GameAudio.Play3DEndingIn("tour_channel", mouth, 1f, 35f, GameRules.TourChannel);
                break;
            case State.Charge:
            {
                // La fin du son coïncide avec le moment où la Tour atteint le joueur (GDD 12).
                PlayerPresence target = FindPresence(TargetId);
                float distance = target != null ? Vector3.Distance(transform.position, target.Position + Vector3.down * 0.9f) : 0f;
                float duration = Mathf.Min(Mathf.Max(0f, distance - ImpactDistance) / GameRules.TourChargeSpeed, 3f);
                GameAudio.Play3DEndingIn("tour_charge", mouth, 1f, 35f, duration);
                break;
            }
        }
    }

    public override string KillSound => "tour_kill";

    // ================= Canalisation : accroupissement lent et yeux rouges =================

    // Pendant les 3 s de canalisation, elle s'accroupit lentement (bassin abaissé, buste penché, genoux pliés, pieds
    // au sol) ; ses yeux s'allument d'un rouge luisant, visible même dans le noir, jusqu'à la fin de la charge.
    const float CrouchDrop = 0.7f, CrouchLean = 22f, CrouchRelease = 0.3f;
    Transform hips, spine, head, eyes;
    (Transform upper, Transform lower, Transform foot)[] legs;
    Light eyeGlow;
    float channelShownAt = -10f, crouch;

    void LateUpdate()
    {
        float goal = Current == State.Channel ? Mathf.SmoothStep(0f, 1f, (Time.time - channelShownAt) / GameRules.TourChannel) : 0f;
        crouch = goal >= crouch ? goal : Mathf.MoveTowards(crouch, goal, Time.deltaTime / CrouchRelease);
        if (crouch > 0.001f) Crouch(crouch);
        bool red = Current == State.Channel || Current == State.Charge;
        if (red && eyes == null) BuildEyes();
        if (eyes != null && eyes.gameObject.activeSelf != red) eyes.gameObject.SetActive(red);
    }

    void Crouch(float w)
    {
        if (hips == null || legs == null) return;
        var feet = new Vector3[legs.Length];
        for (int i = 0; i < legs.Length; i++) feet[i] = legs[i].foot != null ? legs[i].foot.position : Vector3.zero;
        hips.position += Vector3.down * CrouchDrop * GameRules.BigEntityScale * w;
        if (spine != null) spine.rotation = Quaternion.AngleAxis(CrouchLean * w, transform.right) * spine.rotation;
        for (int i = 0; i < legs.Length; i++)
            if (legs[i].upper != null && legs[i].lower != null && legs[i].foot != null) SolveLeg(legs[i].upper, legs[i].lower, legs[i].foot, feet[i]);
    }

    // IK à deux os : le pied revient à sa place, le genou plié vers l'avant.
    void SolveLeg(Transform upper, Transform lower, Transform foot, Vector3 target)
    {
        Vector3 a = upper.position, b = lower.position, c = foot.position;
        float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c);
        float dist = Mathf.Clamp(Vector3.Distance(a, target), Mathf.Abs(ab - bc) + 0.001f, ab + bc - 0.001f);
        float current = Vector3.Angle(a - b, c - b);
        float wanted = Mathf.Acos(Mathf.Clamp((ab * ab + bc * bc - dist * dist) / (2f * ab * bc), -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 axis = transform.right;
        // Le sens de flexion qui envoie le genou vers l'avant.
        Quaternion bendA = Quaternion.AngleAxis(current - wanted, axis), bendB = Quaternion.AngleAxis(wanted - current, axis);
        Vector3 cA = b + bendA * (c - b), cB = b + bendB * (c - b);
        Vector3 kneeA = a + Quaternion.FromToRotation(cA - a, target - a) * (b - a);
        Vector3 kneeB = a + Quaternion.FromToRotation(cB - a, target - a) * (b - a);
        bool useA = Vector3.Dot(kneeA - (a + target) * 0.5f, transform.forward) >= Vector3.Dot(kneeB - (a + target) * 0.5f, transform.forward);
        lower.rotation = (useA ? bendA : bendB) * lower.rotation;
        upper.rotation = Quaternion.FromToRotation(foot.position - a, target - a) * upper.rotation;
    }

    void BuildEyes()
    {
        Transform parent = head != null ? head : transform;
        eyes = new GameObject("YeuxRouges").transform;
        eyes.SetParent(parent, false);
        eyes.localScale = Vector3.one / Mathf.Max(0.0001f, parent.lossyScale.x);
        Vector3 at = head != null ? head.position + transform.up * EyeUp + transform.forward * EyeForward : transform.position + Vector3.up * EyeHeight + transform.forward * 0.4f;
        eyes.SetPositionAndRotation(at, transform.rotation);
        Material glow = GameArt.Glow("TourEyes", new Color(1f, 0.08f, 0.03f), 3.5f);
        foreach (float x in new[] { -EyeGap, EyeGap })
            GameArt.Prim(PrimitiveType.Sphere, eyes, new Vector3(x, 0f, 0f), new Vector3(0.09f, 0.05f, 0.04f), glow)
                .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        eyeGlow = new GameObject("Lueur").AddComponent<Light>();
        eyeGlow.transform.SetParent(eyes, false);
        eyeGlow.transform.localPosition = new Vector3(0f, 0f, 0.2f);
        eyeGlow.type = LightType.Point;
        eyeGlow.color = new Color(1f, 0.1f, 0.05f);
        eyeGlow.range = 2.5f;
        eyeGlow.intensity = 1.2f;
        eyeGlow.shadows = LightShadows.None;
        eyes.gameObject.SetActive(false);
    }

    // Position des yeux par rapport à l'os de la tête (modèle Meshy agrandi de 25 %).
    const float EyeUp = 0.2f, EyeForward = 0.6f, EyeGap = 0.12f;

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
            : Current == State.Charge || Current == State.Investigate || Current == State.Approach ? "Run" : visualSpeed < 0.3f ? "Idle" : "Walk";
        PlayAnim(wanted);
        animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 1.6f, 0.5f, 1.6f) : wanted == "Run" && Current == State.Charge ? 2.5f : 1f;
    }
}
