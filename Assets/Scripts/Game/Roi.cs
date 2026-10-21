using System.Collections.Generic;
using UnityEngine;

// Le Roi (entité ID 8, type Cauchemar, V0.4.6) : grande entité bipède et mince, long cou dressé, dents pointues, drap sur
// les yeux et couronne, une longue épée bâtarde dans la main gauche ; de la taille de La Dame.
// "En recherche", il arpente le terrain sans un bruit (x0,7), s'arrête parfois 2 à 5 s, immobile (toutes les 7 à 15 s),
// puis repart (10 % : demi-tour). S'il voit une Balise, il s'y rend (puis 40 s sans s'en soucier). Hors Abjuration, une
// Abjuration de 10 m l'entoure (arrêtée par les murs, gérée chez chaque joueur). Un joueur repéré à 20 m devant lui est
// "chassé" (de même, sans mur, à moins de 6 m, quel que soit l'angle) : aura blanche à ses pieds (-20 %, pas de course),
// rire ; le Roi avance lentement vers lui en levant le bras droit 2,5 s, puis, si le joueur est toujours en ligne de vue,
// lance une chaîne qui file droit sur lui. Touché, le joueur est immobilisé et traîné vers le Roi (qui ne bouge plus),
// sauf s'il appuie 3 fois sur Espace (la chaîne se rompt). Pas de ligne de vue, ou chaîne rompue : le Roi repart vers lui,
// 1 s sans aura, puis s'il le voit encore, nouvelle visée (1,5 s), sinon il reprend sa recherche. À moins de 0,8 m, il
// immobilise le joueur et le tue.
public class Roi : Entity
{
    public enum State : byte { Walk = 0, Pause = 1, Beacon = 2, Aim = 3, Throw = 4, Pull = 5, Recover = 6, Grab = 7 }

    const float EyeHeight = 2.35f * GameRules.BigEntityScale, FieldOfView = 120f;
    // Mise à mort : il lève son épée au-dessus de sa tête, puis l'abat sur le joueur (le coup porte à la fin du geste).
    const float GrabRaise = 0.6f, GrabStrike = 0.2f;
    const float GrabHitTime = GrabRaise + GrabStrike, GrabDuration = 1.5f;
    const float LinkSpacing = 0.17f;

    public override EntityKind Kind => EntityKind.Roi;
    public override string DisplayName => "Le Roi";
    public override float ChestHeight => 1.6f * GameRules.BigEntityScale;
    protected override float ModelScale => GameRules.BigEntityScale;
    public override bool Nightmare => true;
    public override Vector3 EyesLocal => headBone != null
        ? transform.InverseTransformPoint(headBone.position + transform.forward * 0.12f + Vector3.up * 0.04f)
        : new Vector3(0f, EyeHeight, 0.15f);
    public State Current { get => (State)state; private set => state = (byte)value; }

    // Aura blanche aux pieds du joueur visé (ralenti, sans course).
    public bool Slows => Current == State.Aim || Current == State.Throw;
    // La chaîne est visible (en vol ou accrochée au joueur).
    public bool ChainOut => Current == State.Throw || Current == State.Pull;
    public Vector3 ChainTip { get; private set; }

    float stateTimer, walkTimer, pauseDuration, aimDuration, beaconCooldown;
    bool grabHit;
    Vector3 buffer;

    protected override void Init() => walkTimer = Range(GameRules.RoiWalkMin, GameRules.RoiWalkMax);

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        stateTimer += dt;
        beaconCooldown -= dt;
        var target = Find(players, TargetId);
        // En chasse, au contact du joueur : il l'immobilise et le tue.
        if (Current >= State.Aim && Current != State.Grab && Valid(target) && InReach(target, GameRules.RoiKillDistance))
        {
            BeginGrab();
            return;
        }
        switch (Current)
        {
            case State.Walk:
            case State.Pause:
            case State.Beacon:
            {
                foreach (var p in players)
                    if (Valid(p) && (Sees(p, EyeHeight, GameRules.RoiSightRange, FieldOfView) || Senses(p))) { BeginAim(p, true); return; }
                if (Current == State.Pause)
                {
                    Speed = 0f;
                    if (stateTimer < pauseDuration) break;
                    Current = State.Walk;
                    stateTimer = 0f;
                    walkTimer = Range(GameRules.RoiWalkMin, GameRules.RoiWalkMax);
                    // 10 % : il repart dans l'autre sens.
                    if (rnd.NextDouble() < GameRules.RoiTurnBackChance) PlanPath(NodeAround(-transform.forward), 0f);
                    break;
                }
                Speed = GameRules.BaseSpeed * GameRules.RoiWalkMultiplier;
                if (Current == State.Beacon)
                {
                    FollowPath(dt);
                    if (PathDone)
                    {
                        // Balise atteinte : il reprend sa route, et l'ignore pendant 40 secondes.
                        beaconCooldown = GameRules.RoiBeaconCooldown;
                        Current = State.Walk;
                        stateTimer = 0f;
                        ClearPath();
                    }
                    break;
                }
                if (beaconCooldown <= 0f && Host != null)
                    foreach (Vector3 b in Host.BeaconPositions())
                        if (SeesPoint(b + Vector3.up * 0.5f))
                        {
                            Current = State.Beacon;
                            stateTimer = 0f;
                            PlanPath(nav.NodeAt(b), 0f);
                            return;
                        }
                walkTimer -= dt;
                if (walkTimer <= 0f)
                {
                    Current = State.Pause;
                    stateTimer = 0f;
                    pauseDuration = Range(GameRules.RoiPauseMin, GameRules.RoiPauseMax);
                    break;
                }
                Wander(dt, 10f, 40f);
                break;
            }

            case State.Aim:
                if (!Valid(target)) { Search(); break; }
                // En chasse, il avance lentement vers le joueur en levant le bras ; il ne s'arrête que pour lancer la chaîne.
                Speed = GameRules.BaseSpeed * GameRules.RoiChaseMultiplier;
                Pursue(target, dt);
                if (stateTimer < aimDuration) break;
                ClearPath();
                // V0.4.6 : la chaîne ne part que si le joueur est encore en ligne de vue (aucun mur entre eux).
                if (!ClearLine(transform.position + Vector3.up * EyeHeight, target.Position + Vector3.up * 0.4f)) { BeginRecover(); break; }
                buffer = target.Position;
                ChainTip = HandPosition();
                Current = State.Throw;
                stateTimer = 0f;
                Pulse++; // lancer de la chaîne
                break;

            case State.Throw:
            {
                Speed = 0f;
                // Elle file droit sur le joueur, où qu'il aille.
                if (Valid(target)) buffer = target.Position;
                Vector3 to = buffer - ChainTip;
                float step = GameRules.RoiChainSpeed * dt;
                Vector3 next = to.magnitude <= step ? buffer : ChainTip + to.normalized * step;
                // Un mur arrête la chaîne.
                bool blocked = Physics.Linecast(ChainTip, next, out RaycastHit wall, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (blocked) next = wall.point;
                // Touché si le joueur visé se trouve sur le trajet de la pointe.
                if (Valid(target) && SegmentDistance(target.Position, ChainTip, next) < GameRules.RoiChainRadius)
                {
                    ChainTip = target.Position;
                    Current = State.Pull;
                    stateTimer = 0f;
                    Host?.Chain(this, TargetId, true);
                    break;
                }
                ChainTip = next;
                if (blocked || next == buffer || stateTimer > 3f) BeginRecover();
                break;
            }

            case State.Pull:
                Speed = 0f;
                if (!Valid(target)) { Host?.Chain(this, TargetId, false); Search(); break; }
                Face(target.Position, dt);
                ChainTip = target.Position;
                break;

            case State.Recover:
                if (!Valid(target)) { Search(); break; }
                // Chaîne ratée ou rompue : il se remet aussitôt en marche vers le joueur.
                Speed = GameRules.BaseSpeed * GameRules.RoiChaseMultiplier;
                Pursue(target, dt);
                if (stateTimer < GameRules.RoiRecover) break;
                // Toujours en vue : nouvelle visée, plus courte ; sinon il abaisse le bras et repart.
                if (Sees(target, EyeHeight, GameRules.RoiSightRange, FieldOfView)) BeginAim(target, false);
                else Search();
                break;

            case State.Grab:
                Speed = 0f;
                if (target != null) Face(target.Position, dt);
                if (!grabHit && stateTimer >= GrabHitTime)
                {
                    grabHit = true;
                    if (target != null && target.Alive) Host?.Hit(this, TargetId, GameRules.MaxHp, 0f);
                }
                if (stateTimer >= GrabDuration) Search();
                break;
        }
    }

    // Le joueur chaîné a rompu la chaîne (3 appuis sur Espace).
    public void BreakFree(string playerId)
    {
        if (Current != State.Pull || playerId != TargetId) return;
        Pulse++; // chaîne rompue
        BeginRecover();
    }

    void BeginAim(PlayerPresence p, bool first)
    {
        Current = State.Aim;
        stateTimer = 0f;
        TargetId = p.PlayerId;
        aimDuration = first ? GameRules.RoiAimFirst : GameRules.RoiAimAgain;
        ClearPath();
        repathTimer = 0f;
    }

    void BeginRecover()
    {
        Current = State.Recover;
        stateTimer = 0f;
        repathTimer = 0f;
    }

    void BeginGrab()
    {
        if (Current == State.Pull) Host?.Chain(this, TargetId, false);
        Current = State.Grab;
        stateTimer = 0f;
        grabHit = false;
        ClearPath();
        Host?.Immobilize(this, TargetId, GrabDuration);
    }

    void Search()
    {
        Current = State.Walk;
        stateTimer = 0f;
        TargetId = "";
        walkTimer = Range(GameRules.RoiWalkMin, GameRules.RoiWalkMax);
        ClearPath();
    }

    // À moins de 6 m (3 m en marche discrète), devant comme dans son dos, sans mur entre eux.
    bool Senses(PlayerPresence p)
    {
        Vector3 d = p.Position - transform.position;
        float radius = GameRules.RoiSenseRadius * (p.Sneaking ? 0.5f : 1f);
        if (Horizontal(d).magnitude > radius || Mathf.Abs(d.y - 0.9f) > 2.5f) return false;
        return ClearLine(transform.position + Vector3.up * 1.5f, p.Position + Vector3.up * 0.4f);
    }

    bool SeesPoint(Vector3 point)
    {
        Vector3 eye = transform.position + Vector3.up * EyeHeight;
        Vector3 d = point - eye;
        if (d.magnitude > GameRules.RoiSightRange) return false;
        if (Vector3.Angle(transform.forward, Horizontal(d)) > FieldOfView * 0.5f) return false;
        return ClearLine(eye, point);
    }

    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector3.Distance(p, a + ab * t);
    }

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

    // ================= Réseau =================

    public override string Extra => ChainOut
        ? NetworkGame.F(ChainTip.x) + ":" + NetworkGame.F(ChainTip.y) + ":" + NetworkGame.F(ChainTip.z) : "";

    protected override void ApplyExtra(string extra)
    {
        string[] p = extra.Split(':');
        if (p.Length == 3) netTip = new Vector3(NetworkGame.P(p[0]), NetworkGame.P(p[1]), NetworkGame.P(p[2]));
    }

    // ================= Modèle =================

    Transform headBone, handBone, upperArm, lowerArm;
    Transform swordHand, swordUpper, swordLower;
    float grabShownAt = -1f;
    Transform fallbackArm;
    Transform chainRoot, whiteAura;
    readonly List<Transform> links = new List<Transform>();
    Vector3 netTip, shownTip;
    float armRaise;
    bool tipShown;

    protected override void BuildVisual()
    {
        if (LoadCreature("RoiArt") && animator.isHuman)
        {
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            // Le modèle Meshy a perdu son épée : une longue épée bâtarde en primitives, dans la main gauche.
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform leftFore = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            if (left != null && leftFore != null) BuildSword(left, left.position - leftFore.position);
            swordHand = left;
            swordLower = leftFore;
            swordUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        }
        else if (animator == null)
        {
            // Repli : silhouette en primitives (robe, long cou, couronne, épée à gauche).
            Material robe = GameArt.Lit("RoiRobe", new Color(0.12f, 0.12f, 0.13f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
            Material skin = GameArt.Lit("RoiSkin", new Color(0.55f, 0.54f, 0.5f), null, 0.2f);
            Material cloth = GameArt.Lit("RoiCloth", new Color(0.6f, 0.58f, 0.55f), null, 0.1f);
            Material iron = GameArt.Lit("RoiIron", new Color(0.2f, 0.2f, 0.21f), null, 0.7f);
            GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.6f, 0.8f, 0.5f), robe);
            GameArt.Prim(PrimitiveType.Cube, transform, new Vector3(0f, 1.75f, 0f), new Vector3(0.6f, 0.5f, 0.3f), robe);
            GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 2.2f, 0f), new Vector3(0.12f, 0.25f, 0.12f), skin);
            GameArt.Prim(PrimitiveType.Sphere, transform, new Vector3(0f, 2.5f, 0f), new Vector3(0.24f, 0.28f, 0.24f), skin);
            GameArt.Prim(PrimitiveType.Cube, transform, new Vector3(0f, 2.54f, 0.04f), new Vector3(0.26f, 0.07f, 0.22f), cloth);
            GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 2.66f, 0f), new Vector3(0.24f, 0.05f, 0.24f), iron);
            GameArt.Prim(PrimitiveType.Cube, transform, new Vector3(-0.42f, 1.0f, 0.1f), new Vector3(0.06f, 1.3f, 0.02f), iron);
            fallbackArm = new GameObject("BrasD").transform;
            fallbackArm.SetParent(transform, false);
            fallbackArm.localPosition = new Vector3(0.36f, 1.95f, 0f);
            GameArt.Prim(PrimitiveType.Cube, fallbackArm, new Vector3(0f, -0.45f, 0f), new Vector3(0.1f, 0.9f, 0.1f), robe);
        }
        chainRoot = new GameObject("Chaine").transform;
        chainRoot.SetParent(transform, false);
        // Aura blanche aux pieds du joueur visé.
        whiteAura = GameArt.Prim(PrimitiveType.Sphere, transform, Vector3.zero, new Vector3(1.3f, 0.22f, 1.3f),
            GameArt.GhostAuraTinted("White", new Color(0.95f, 0.95f, 1f))).transform;
        whiteAura.gameObject.SetActive(false);
    }

    void UpdateAura(PlayerPresence target)
    {
        bool shown = target != null && target.Alive;
        whiteAura.gameObject.SetActive(shown);
        if (shown) whiteAura.position = target.Position + Vector3.down * 0.85f;
    }

    // La lame prolonge l'avant-bras (bras tombant : pointe vers le sol), un peu inclinée vers l'avant.
    void BuildSword(Transform hand, Vector3 forearm)
    {
        var sword = new GameObject("Epee").transform;
        sword.SetParent(hand, false);
        // Échelle monde fixe malgré l'échelle des os.
        sword.localScale = Vector3.one / Mathf.Max(0.0001f, hand.lossyScale.x);
        Vector3 along = forearm.normalized;
        Vector3 tilt = Vector3.Cross(along, transform.forward).normalized;
        sword.rotation = Quaternion.AngleAxis(25f, tilt) * Quaternion.FromToRotation(Vector3.up, along);
        sword.position = hand.position + along * 0.06f;
        Material steel = GameArt.Lit("RoiSteel", new Color(0.33f, 0.31f, 0.29f), GameArt.Noise("rust", 64, 0.55f, 0.35f, 23), 0.55f);
        Material grip = GameArt.Lit("RoiGrip", new Color(0.1f, 0.08f, 0.07f), null, 0.2f);
        GameArt.Prim(PrimitiveType.Cube, sword, new Vector3(0f, 0.68f, 0f), new Vector3(0.06f, 1.05f, 0.018f), steel);
        GameArt.Prim(PrimitiveType.Cube, sword, new Vector3(0f, 0.14f, 0f), new Vector3(0.045f, 0.035f, 0.3f), steel);
        GameArt.Prim(PrimitiveType.Cylinder, sword, new Vector3(0f, 0f, 0f), new Vector3(0.035f, 0.12f, 0.035f), grip);
        GameArt.Prim(PrimitiveType.Sphere, sword, new Vector3(0f, -0.14f, 0f), new Vector3(0.06f, 0.06f, 0.06f), steel);
    }

    Vector3 HandPosition()
    {
        if (handBone != null) return handBone.position;
        if (fallbackArm != null) return fallbackArm.TransformPoint(new Vector3(0f, -0.9f, 0f));
        return transform.position + Vector3.up * 1.9f + transform.right * 0.4f;
    }

    // ================= Affichage =================

    protected override void OnStateChanged(byte previous, byte next)
    {
        Vector3 head = transform.position + Vector3.up * EyeHeight;
        // Rire quand il commence à lever le bras.
        if ((State)next == State.Aim && (State)previous < State.Aim) GameAudio.Play3D("roi_laugh", head, 1f, 35f, 0f);
        if ((State)next == State.Aim)
        {
            aimShownAt = Time.time;
            aimShownLen = (State)previous == State.Recover ? GameRules.RoiAimAgain : GameRules.RoiAimFirst;
            // L'aura blanche apparaît chez le joueur visé.
            PlayerPresence target = FindPresence(TargetId);
            if (target != null) GameAudio.Play3D("roi_flash", target.Position, 1f, 30f, 0f);
        }
        if ((State)next == State.Throw) throwShownAt = Time.time;
        // La chaîne a touché son objectif ou fini sa course : le son du lancer se coupe.
        if ((State)previous == State.Throw && chainSound != null)
        {
            // (La source a pu être reprise par un autre son entre-temps.)
            if (chainSound.clip == SoundBank.Get("roi_chain")) chainSound.Stop();
            chainSound = null;
        }
        if ((State)next == State.Grab) grabShownAt = Time.time;
    }

    public override string KillSound => "roi_kill";

    AudioSource chainSound;

    // Lancer de la chaîne, puis chaîne rompue.
    protected override void OnPulse()
    {
        if (Current == State.Throw)
        {
            chainSound = GameAudio.Play3D("roi_chain", HandPosition(), 1f, 30f, 0f);
            return;
        }
        GameAudio.Play3D("roi_break", ChainTipShown, 1f, 25f, 0.08f);
        // La chaîne vole en éclats : chaque maillon encore affiché se détache et tombe au sol, puis disparaît.
        Vector3 snap = ChainTipShown;
        foreach (var link in links)
            if (link.gameObject.activeSelf) ChainLink.Shatter(link, snap);
        if (hook != null && hook.gameObject.activeSelf) ChainLink.Shatter(hook, snap, hookBoxCenter, hookBoxSize, 1f);
    }

    Vector3 ChainTipShown => tipShown ? shownTip : transform.position + Vector3.up;

    protected override void Animate(float dt)
    {
        if (animator != null)
        {
            PlayAnim(visualSpeed < 0.2f ? "Idle" : "Walk");
            animator.speed = CurrentAnim == "Walk" ? Mathf.Clamp(visualSpeed / 1.4f, 0.5f, 1.6f) : 1f;
        }
        // Le bras droit se lève vers le joueur pendant la chasse, et retombe en recherche.
        bool raised = Current >= State.Aim && Current <= State.Recover;
        armRaise = Mathf.MoveTowards(armRaise, raised ? 1f : 0f, dt * 2.5f);
    }

    void LateUpdate()
    {
        // Après l'Animator : le bras pointe vers le joueur visé.
        Vector3 aim = AimPoint();
        if (armRaise > 0.001f)
        {
            if (upperArm != null && handBone != null) PointArm(upperArm, lowerArm, handBone, aim, armRaise);
            else if (fallbackArm != null)
            {
                Vector3 local = fallbackArm.parent.InverseTransformDirection(aim - fallbackArm.position);
                fallbackArm.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.down, local), armRaise);
            }
        }
        else if (fallbackArm != null) fallbackArm.localRotation = Quaternion.identity;
        AnimateSword();
        UpdateChain();
        UpdateAura(Slows ? FindPresence(TargetId) : null);
    }

    // Mise à mort : le bras gauche monte l'épée au-dessus de la tête, puis l'abat d'un coup sec sur le joueur.
    void AnimateSword()
    {
        if (Current != State.Grab || grabShownAt < 0f || swordUpper == null || swordHand == null) return;
        float t = Time.time - grabShownAt;
        float raise = Mathf.SmoothStep(0f, 1f, t / GrabRaise);
        float strike = Mathf.SmoothStep(0f, 1f, (t - GrabRaise) / GrabStrike);
        if (strike > 0f && !slashPlayed)
        {
            slashPlayed = true;
            GameAudio.Play3D("roi_slash", swordHand.position, 1f, 25f, 0.08f);
        }
        if (strike <= 0f) slashPlayed = false;
        Vector3 shoulder = swordUpper.position;
        Vector3 overhead = shoulder + Vector3.up * 1.2f - transform.forward * 0.35f;
        PlayerPresence target = FindPresence(TargetId);
        Vector3 victim = target != null ? target.Position + Vector3.down * 0.3f : transform.position + transform.forward * 1f + Vector3.up * 0.6f;
        Vector3 aim = Vector3.Lerp(overhead, victim, strike);
        PointArm(swordUpper, swordLower, swordHand, aim, raise);
    }

    bool slashPlayed;

    // Oriente le bras (épaule puis coude) pour que la main pointe vers 'aim', avec un poids de 0 à 1.
    static void PointArm(Transform upper, Transform lower, Transform hand, Vector3 aim, float weight)
    {
        Quaternion turn = Quaternion.FromToRotation(hand.position - upper.position, aim - upper.position);
        upper.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * upper.rotation;
        if (lower == null) return;
        Quaternion straighten = Quaternion.FromToRotation(hand.position - lower.position, aim - lower.position);
        lower.rotation = Quaternion.Slerp(Quaternion.identity, straighten, weight) * lower.rotation;
    }

    // Geste du lancer : bras tendu vers le joueur pendant la visée ; juste avant de lancer, il lève légèrement le bras,
    // puis le retend d'un coup sec vers le joueur quand la chaîne part.
    const float LiftTime = 0.35f, SnapTime = 0.1f, LiftAngle = 35f;
    float aimShownAt = -10f, aimShownLen, throwShownAt = -10f;

    Vector3 AimPoint()
    {
        PlayerPresence p = FindPresence(TargetId);
        Vector3 target = ChainOut ? ChainTipShown : p != null ? p.Position : transform.position + transform.forward * 5f + Vector3.up * 1.5f;
        Vector3 shoulder = upperArm != null ? upperArm.position : fallbackArm != null ? fallbackArm.position : transform.position + Vector3.up * 2.4f;
        Vector3 toTarget = target - shoulder;
        Vector3 side = Vector3.Cross(Vector3.up, toTarget);
        // Bras relevé : la direction du joueur, inclinée de 35° vers le haut.
        Vector3 lifted = side.sqrMagnitude > 0.0001f
            ? shoulder + Quaternion.AngleAxis(-LiftAngle, side.normalized) * toTarget
            : target + Vector3.up;
        if (Current == State.Aim)
        {
            float left = aimShownAt + aimShownLen - Time.time;
            if (left < LiftTime) return Vector3.Lerp(target, lifted, Mathf.SmoothStep(0f, 1f, 1f - left / LiftTime));
        }
        if (Current == State.Throw && Time.time - throwShownAt < SnapTime)
        {
            // Coup sec : le bras retombe vite au début, puis se pose sur le joueur.
            float k = (Time.time - throwShownAt) / SnapTime;
            return Vector3.Lerp(lifted, target, 1f - (1f - k) * (1f - k));
        }
        return target;
    }

    // Maillons alignés de la main jusqu'à la pointe, tournés d'un quart de tour un sur deux.
    void UpdateChain()
    {
        bool isHost = GameSession.Instance != null && GameSession.Instance.IsHost;
        if (!ChainOut)
        {
            tipShown = false;
            SetLinks(0);
            if (hook != null) hook.gameObject.SetActive(false);
            return;
        }
        Vector3 goal = isHost ? ChainTip : netTip;
        if (!tipShown)
        {
            shownTip = HandPosition();
            tipShown = true;
        }
        // Chez les autres joueurs, la pointe rattrape en douceur la position reçue (10 envois par seconde).
        shownTip = isHost ? goal : Vector3.MoveTowards(shownTip, goal, Mathf.Max(GameRules.RoiChainSpeed * 1.5f, Vector3.Distance(shownTip, goal) * 12f) * Time.deltaTime);
        Vector3 from = HandPosition();
        Vector3 d = shownTip - from;
        int count = Mathf.Clamp(Mathf.CeilToInt(d.magnitude / LinkSpacing), 1, 220);
        SetLinks(count);
        // Le crochet au bout : son anneau sur la pointe, le crochet tourné vers l'avant.
        if (hook == null) BuildHook();
        if (hook != null)
        {
            hook.gameObject.SetActive(true);
            hook.position = shownTip;
            if (d.sqrMagnitude > 0.0001f) hook.rotation = Quaternion.FromToRotation(Vector3.up, -d.normalized);
        }
        Quaternion along = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : transform.rotation;
        for (int k = 0; k < count; k++)
        {
            links[k].position = from + d * ((k + 0.5f) / count);
            links[k].rotation = along * Quaternion.Euler(0f, 0f, k % 2 == 0 ? 0f : 90f);
        }
    }

    // Crochet de fer forgé (modèle Meshy Resources/Art/Crochet) : environ 60 cm, l'anneau à l'origine, la pointe vers -Y.
    const float HookLength = 0.6f;
    Transform hook;
    Vector3 hookBoxCenter, hookBoxSize;
    bool hookTried;

    void BuildHook()
    {
        if (hookTried) return;
        hookTried = true;
        var model = Resources.Load<GameObject>("Art/Crochet");
        if (model == null) return;
        hook = new GameObject("Crochet").transform;
        hook.SetParent(chainRoot, false);
        var shape = Instantiate(model, hook);
        shape.name = "Forme";
        foreach (var c in shape.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var r in shape.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Mise à l'échelle et recentrage d'après le modèle : le haut (l'anneau) sur l'origine.
        var filters = shape.GetComponentsInChildren<MeshFilter>();
        if (filters.Length == 0) return;
        Bounds b = filters[0].sharedMesh.bounds;
        Matrix4x4 toHook = hook.worldToLocalMatrix * filters[0].transform.localToWorldMatrix;
        Bounds local = new Bounds(toHook.MultiplyPoint3x4(b.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            local.Encapsulate(toHook.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2f - Vector3.one)));
        float scale = HookLength / Mathf.Max(0.0001f, local.size.y);
        shape.transform.localScale *= scale;
        shape.transform.localPosition = -new Vector3(local.center.x, local.max.y, local.center.z) * scale;
        hookBoxCenter = new Vector3(0f, -HookLength * 0.5f, 0f);
        hookBoxSize = new Vector3(Mathf.Max(0.05f, local.size.x * scale), HookLength, Mathf.Max(0.05f, local.size.z * scale));
    }

    void SetLinks(int count)
    {
        while (links.Count < count) links.Add(ChainLink.Create(chainRoot));
        for (int k = 0; k < links.Count; k++)
            if (links[k].gameObject.activeSelf != k < count) links[k].gameObject.SetActive(k < count);
    }
}
