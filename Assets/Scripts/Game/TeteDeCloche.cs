using System.Collections.Generic;
using UnityEngine;

// La Tête de Cloche (entité ID 11, type Sanction, V0.6) : grande entité bipède (taille de La Dame), une cloche de bronze à
// la place de la tête, armée d'un marteau gravé "WOOF". Elle n'apparaît qu'en cas de Sanction (ordre d'un glyphe non
// respecté), à 30 m du joueur sanctionné, et ne poursuit que lui : à sa vitesse les 7 premières secondes, puis elle
// accélère chaque seconde jusqu'à 2x au bout de 20 secondes. À moins de 0,8 m, elle l'immobilise et le tue. Elle s'éloigne
// ensuite lentement du cadavre, sans attaquer personne, et disparaît d'un coup à 20 m de lui. Si le joueur se
// réfugie dans sa niche, elle renonce et s'éloigne de même.
// Toutes les 3 secondes (0,5 seconde à moins de 5 m de sa proie), elle frappe sa tête de son marteau : le son de cloche
// obnubile tous les joueurs qui la voient (comme La Cloche), jusqu'à ce qu'un mur les sépare.
public class TeteDeCloche : Entity
{
    public enum State : byte { Hunt = 0, Kill = 1, Leave = 2, Vanish = 3 }

    public override EntityKind Kind => EntityKind.TeteDeCloche;
    public override string DisplayName => "La Tête de Cloche";
    public override float ChestHeight => 1.4f * GameRules.BigEntityScale;
    protected override float ModelScale => GameRules.BigEntityScale;
    public override Vector3 EyesLocal => new Vector3(0f, 2.45f * GameRules.BigEntityScale, 0.25f);
    // Son de mise à mort (V0.6.9) : son attaque tombe à l'instant du coup mortel, quand le marteau percute le personnage.
    public override string KillSound => "tete_kill";
    // Ce joueur a déjà entendu le son d'obnubilation de cette entité (V0.6.9 : une seule fois).
    public bool SightVoiced { get; set; }
    public State Current { get => (State)state; private set => state = (byte)value; }

    float huntTime, ringTimer, stateTimer;
    Vector3 corpse;
    readonly List<string> obsessed = new List<string>();
    readonly List<string> scratch = new List<string>();

    public bool Obsesses(string playerId) => obsessed.Contains(playerId);

    // L'hôte lui désigne le joueur sanctionné.
    public void Setup(string prey)
    {
        TargetId = prey;
        ringTimer = 1f;
    }

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        stateTimer += dt;
        switch (Current)
        {
            case State.Hunt:
            {
                huntTime += dt;
                var prey = Find(players, TargetId);
                // Mort, parti, ou réfugié dans sa niche (ou un Canon) : elle renonce.
                if (prey == null || !prey.Alive || !prey.isActiveAndEnabled || prey.InCannon || prey.Teleporting)
                {
                    BeginLeave(prey != null ? Feet(prey) : transform.position);
                    break;
                }
                float distance = HorizontalDistance(prey);
                // Dans un Hub, elle ne peut pas l'atteindre : elle attend, en sonnant.
                if (WorldBuilder.IsInHub(prey.Position))
                {
                    Speed = 0f;
                    Face(prey.Position, dt);
                }
                else
                {
                    Speed = GameRules.BaseSpeed * GameRules.TeteSpeed(huntTime);
                    Pursue(prey, dt);
                    if (InReach(prey, GameRules.TeteKillDistance))
                    {
                        Current = State.Kill;
                        stateTimer = 0f;
                        Speed = 0f;
                        Host?.Immobilize(this, TargetId, GameRules.TeteKillHold + 0.6f);
                        break;
                    }
                }
                UpdateObsessed(players);
                ringTimer -= dt;
                if (ringTimer <= 0f)
                {
                    ringTimer = GameRules.TeteRingDelay(distance);
                    Ring(players);
                }
                break;
            }

            case State.Kill:
            {
                Speed = 0f;
                var prey = Find(players, TargetId);
                if (prey != null) Face(prey.Position, dt);
                if (stateTimer < GameRules.TeteKillHold) break;
                if (prey != null && prey.Alive) Host?.Hit(this, TargetId, GameRules.TeteDamage, 0f);
                BeginLeave(prey != null ? Feet(prey) : transform.position);
                break;
            }

            case State.Leave:
                Speed = GameRules.BaseSpeed * GameRules.TeteLeaveMultiplier;
                if (PathDone) PlanPath(AwayFrom(corpse), 0f);
                FollowPath(dt);
                if (Horizontal(transform.position - corpse).magnitude >= GameRules.TeteLeaveDistance || stateTimer > 40f)
                {
                    Current = State.Vanish;
                    stateTimer = 0f;
                }
                break;

            case State.Vanish:
                // Le temps que tous les joueurs reçoivent sa disparition, puis elle quitte la partie.
                Speed = 0f;
                if (stateTimer > 0.6f) Expired = true;
                break;
        }
    }

    void BeginLeave(Vector3 from)
    {
        Current = State.Leave;
        stateTimer = 0f;
        corpse = from;
        obsessed.Clear();
        PlanPath(AwayFrom(from), 0f);
    }

    // Point de la grille à plus de 20 m de 'from', de préférence à l'opposé.
    int AwayFrom(Vector3 from)
    {
        Vector3 away = Horizontal(transform.position - from);
        if (away.sqrMagnitude < 0.01f) away = transform.forward;
        int best = -1;
        for (int k = 0; k < 12; k++)
        {
            int node = nav.RandomNode(0, rnd, from, GameRules.TeteLeaveDistance + 3f, GameRules.TeteLeaveDistance + 20f);
            if (node < 0) continue;
            best = node;
            if (Vector3.Dot(Horizontal(nav.Position(node) - transform.position), away) > 0f) break;
        }
        return best;
    }

    // Obnubilés : ceux qui la voient au moment où elle sonne, tant qu'aucun mur ne les sépare d'elle.
    void UpdateObsessed(IReadOnlyList<PlayerPresence> players)
    {
        obsessed.RemoveAll(id =>
        {
            var p = Find(players, id);
            return p == null || !p.Alive || p.InCannon || p.Teleporting || !Sees(p, EyesLocal.y, GameRules.TeteSightRange, 360f, false);
        });
    }

    void Ring(IReadOnlyList<PlayerPresence> players)
    {
        Pulse++;
        foreach (var p in players)
            if (p != null && p.Alive && p.isActiveAndEnabled && !p.InCannon && !p.Teleporting && !obsessed.Contains(p.PlayerId)
                && Sees(p, EyesLocal.y, GameRules.TeteSightRange, 360f, false))
                obsessed.Add(p.PlayerId);
    }

    public override string Extra => string.Join("+", obsessed);

    protected override void ApplyExtra(string extra)
    {
        obsessed.Clear();
        if (extra.Length > 0) obsessed.AddRange(extra.Split('+'));
    }

    // Coup de marteau sur la cloche, entendu de chacun : ceux qu'elle obnubile perdent de la sérénité (GameSession).
    public event System.Action Rang;

    // ================= Modèle =================

    // Modèle Meshy animé (cloche de bronze pour tête), le marteau Meshy dans la main droite. Sans lui : le corps de La Dame
    // coiffé de la Cloche Décorative, puis une silhouette en primitives.
    Transform headBone, armBone, foreBone, handBone, hammer;
    float strikeAt = -10f;
    const float StrikeDuration = 0.45f;

    protected override void BuildVisual()
    {
        if (LoadCreature("TeteDeClocheArt"))
        {
            if (animator.isHuman) AttachToBones(false);
            return;
        }
        var art = Resources.Load<DameArt>("DameArt");
        if (art != null && art.model != null)
        {
            var model = Instantiate(art.model, transform);
            model.name = "Modele";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * art.scale;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = art.controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            // Haillons gris-vert, plus ternes que La Dame.
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_Color", new Color(0.62f, 0.66f, 0.6f));
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(tint);
            if (animator.isHuman) AttachToBones(true);
        }
        else
        {
            Material rags = GameArt.Lit("TeteHaillons", new Color(0.4f, 0.43f, 0.38f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
            GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.6f, 1.1f, 0.45f), rags);
            var neck = new GameObject("Cou").transform;
            neck.SetParent(transform, false);
            neck.localPosition = new Vector3(0f, 2.3f, 0f);
            BuildBell(neck);
            var arm = new GameObject("Bras").transform;
            arm.SetParent(transform, false);
            arm.localPosition = new Vector3(0.45f, 1.9f, 0f);
            armBone = arm;
            hammer = BuildHammer(arm, Vector3.down);
        }
    }

    void AttachToBones(bool addBell)
    {
        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        armBone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform fore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        foreBone = fore;
        handBone = hand;
        if (addBell && headBone != null) BuildBell(headBone);
        if (hand != null && fore != null) hammer = BuildHammer(hand, hand.position - fore.position);
    }

    // Cloche de bronze (modèle de la Cloche Décorative) enfilée sur la tête : le crâne disparaît dedans.
    static void BuildBell(Transform head)
    {
        var bell = new GameObject("Cloche").transform;
        bell.SetParent(head, false);
        bell.localScale = Vector3.one / Mathf.Max(0.0001f, head.lossyScale.x);
        bell.position = head.position + Vector3.down * 0.5f;
        bell.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(head.forward, Vector3.up), Vector3.up);
        if (GameArt.SpawnModel("ClocheDecorative", bell, 1.4f) == null)
        {
            Material bronze = GameArt.Lit("BellBronze", new Color(0.78f, 0.6f, 0.28f), null, 0.8f);
            GameArt.Prim(PrimitiveType.Cylinder, bell, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.3f, 0.6f), bronze);
            GameArt.Prim(PrimitiveType.Sphere, bell, new Vector3(0f, 0.62f, 0f), new Vector3(0.6f, 0.45f, 0.6f), bronze);
        }
    }

    // Marteau de forge : long manche, tête de fer cerclée, "WOOF" gravé sur la face (à la place du 75 de la référence).
    Transform BuildHammer(Transform hand, Vector3 forearm)
    {
        var root = new GameObject("Marteau").transform;
        root.SetParent(hand, false);
        root.localScale = Vector3.one / Mathf.Max(0.0001f, hand.lossyScale.x);
        Vector3 along = forearm.normalized;
        root.rotation = Quaternion.FromToRotation(Vector3.up, along) * Quaternion.Euler(0f, 90f, 0f);
        root.position = hand.position + along * 0.04f;
        if (SpawnHammer(root)) return root;
        Material wood = GameArt.Lit("MarteauManche", new Color(0.3f, 0.2f, 0.13f), GameArt.Noise("wood", 128, 0.7f, 0.25f, 3), 0.15f);
        Material iron = GameArt.Lit("MarteauFer", new Color(0.28f, 0.27f, 0.27f), GameArt.Noise("rust", 64, 0.55f, 0.35f, 23), 0.55f);
        GameArt.Prim(PrimitiveType.Cylinder, root, new Vector3(0f, 0.35f, 0f), new Vector3(0.06f, 0.6f, 0.06f), wood);
        var head = GameArt.Prim(PrimitiveType.Cylinder, root, new Vector3(0f, 0.95f, 0f), new Vector3(0.26f, 0.22f, 0.26f), iron);
        head.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        foreach (float side in new[] { -1f, 1f })
        {
            var ring = GameArt.Prim(PrimitiveType.Cylinder, root, new Vector3(side * 0.2f, 0.95f, 0f), new Vector3(0.3f, 0.03f, 0.3f), GameArt.Metal);
            ring.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        var text = GameArt.CreateText(root, "WOOF", new Vector3(0.23f, 0.95f, 0f), 0.012f, new Color(0.8f, 0.75f, 0.65f));
        text.text = "WOOF";
        text.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return root;
    }

    // Marteau Meshy (Resources/Art/MarteauTete, "WOOF" gravé sur la tête), livré couché le long de x, tête vers +x :
    // redressé, manche dans la main, tête en haut.
    const float HammerLength = 1.1f;

    static bool SpawnHammer(Transform root)
    {
        var prefab = Resources.Load<GameObject>("Art/MarteauTete");
        if (prefab == null) return false;
        var go = Instantiate(prefab, root);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * prefab.transform.localRotation;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        Bounds b = GameArt.LocalBounds(root);
        go.transform.localScale *= HammerLength / Mathf.Max(0.0001f, b.size.y);
        b = GameArt.LocalBounds(root);
        go.transform.localPosition = new Vector3(-b.center.x, -b.min.y - 0.15f, -b.center.z);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return true;
    }

    // ================= Affichage =================

    readonly GameAudio.FadedLoop steps = new GameAudio.FadedLoop("dame_step", 0.6f, 25f, 0.35f);

    protected override void OnPulse()
    {
        // Un coup encore en préparation sonne tout de suite : aucun n'est perdu.
        if (ringPending) RingBell();
        strikeAt = Time.time;
        // Le bras s'arme d'abord : plus vite s'il est déjà levé. La cloche sonne à l'impact.
        strikeLead = Mathf.Lerp(StrikeLeadRested, StrikeLeadRaised, armWeight);
        ringPending = true;
    }

    void RingBell()
    {
        ringPending = false;
        // GDD 12 (V0.6.1) : le son de cloche, tiré vers les graves.
        GameAudio.Play3D("tete_bell", transform.position + Vector3.up * EyesLocal.y, 1f, 60f, 0.03f, 0.7f);
        Rang?.Invoke();
    }

    protected override void OnStateChanged(byte previous, byte next)
    {
        if ((State)next != State.Vanish) return;
        // Elle disparaît d'un coup, sans son ni particules.
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
    }

    protected override void Animate(float dt)
    {
        steps.Update(transform, Vector3.zero, visualSpeed > 0.3f && Current != State.Vanish, Current == State.Hunt ? 1.2f : 0.8f, dt);
        if (ringPending && Time.time >= strikeAt + strikeLead) RingBell();
        if (animator == null) return;
        string wanted = Current == State.Kill ? "Attack" : visualSpeed < 0.3f ? "Idle" : visualSpeed > 5f ? "Run" : "Walk";
        PlayAnim(wanted, wanted == "Attack" ? 0.05f : 0.2f);
        animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 1.3f, 0.5f, 2f) : wanted == "Run" ? Mathf.Clamp(visualSpeed / 4.5f, 0.8f, 2.2f) : wanted == "Attack" ? GameRules.TeteKillAnimSpeed : 1f;
    }

    // Tête désarticulée (GDD) : penchée sur le côté, elle change de position par à-coups secs, à intervalles irréguliers.
    Quaternion jerkFrom = Quaternion.identity, jerkTo = Quaternion.identity;
    float jerkAt, nextJerk;
    const float JerkSnap = 0.07f;

    void JerkHead()
    {
        if (headBone == null || Current == State.Vanish) return;
        if (Time.time >= nextJerk)
        {
            jerkFrom = CurrentJerk();
            jerkTo = Quaternion.Euler(Random.Range(-10f, 12f), Random.Range(-16f, 16f), Random.Range(10f, 26f) * (Random.value < 0.8f ? 1f : -1f));
            jerkAt = Time.time;
            nextJerk = Time.time + Random.Range(0.25f, 1.4f);
        }
        headBone.rotation = transform.rotation * CurrentJerk() * Quaternion.Inverse(transform.rotation) * headBone.rotation;
    }

    Quaternion CurrentJerk() => Quaternion.Slerp(jerkFrom, jerkTo, (Time.time - jerkAt) / JerkSnap);

    void LateUpdate()
    {
        JerkHead();
        if (armBone == null) return;
        if (foreBone == null || handBone == null || headBone == null || hammer == null)
        {
            // Silhouette de secours, sans coude : le bras lève le marteau puis l'abat.
            float t = Time.time - strikeAt;
            if (t >= StrikeDuration) return;
            float k = t / StrikeDuration;
            float lift = k < 0.55f ? Mathf.SmoothStep(0f, 1f, k / 0.55f) : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.55f) / 0.45f);
            armBone.rotation = Quaternion.AngleAxis(-150f * lift, transform.right) * armBone.rotation;
            return;
        }
        UpdateArmWeight(Time.deltaTime);
        if (armWeight > 0.001f) PoseArm(CurrentPose(), armWeight);
    }

    // ================= Bras qui frappe la cloche =================

    // Pose du bras, autour de la tête : position de la main et direction du manche (tête du marteau au bout), dans le repère
    // de l'entité (x à sa droite, y en haut, z devant). Après l'Animator, le bras rejoint ces poses par épaule, coude et poignet.
    struct ArmPose
    {
        public Vector3 hand, handle;
        public ArmPose(Vector3 hand, Vector3 handle) { this.hand = hand; this.handle = handle; }
    }

    // Levé près de la tête, entre deux coups.
    static readonly ArmPose Guard = new ArmPose(new Vector3(0.7f, -0.3f, 0.3f), new Vector3(0.3f, 0.9f, -0.15f));
    // Armé : le marteau s'écarte de la cloche.
    static readonly ArmPose Windup = new ArmPose(new Vector3(0.8f, -0.25f, 0.22f), new Vector3(0.85f, 0.5f, -0.1f));
    // Impact : la tête du marteau contre le flanc de la cloche.
    static readonly ArmPose Impact = new ArmPose(new Vector3(0.72f, -0.35f, 0.35f), new Vector3(-0.17f, 0.95f, -0.35f));

    const float StrikeLeadRested = 0.42f, StrikeLeadRaised = 0.22f, WindupShare = 0.7f, RecoilTime = 0.22f;
    const float LowerDelay = 0.2f, LowerTime = 0.35f, DropTime = 0.12f, RaiseTime = 0.35f;
    // Sous ce délai entre deux coups, le bras reste levé (le rythme dépend de la distance à la proie, connue de chaque client).
    const float RaisedRingDelay = 1.6f;

    float armWeight, strikeLead = StrikeLeadRested;
    bool ringPending;

    bool RingsSoon()
    {
        if (Current != State.Hunt) return false;
        var prey = Find(PlayerPresence.All, TargetId);
        return prey != null && GameRules.TeteRingDelay(HorizontalDistance(prey)) <= RaisedRingDelay;
    }

    void UpdateArmWeight(float dt)
    {
        float since = Time.time - strikeAt;
        float strikeEnd = strikeLead + RecoilTime;
        float rate;
        float target;
        if (Current == State.Kill || Current == State.Vanish) { target = 0f; rate = 1f / DropTime; }
        else if (since < strikeEnd) { target = 1f; rate = 1f / (strikeLead * WindupShare); }
        else if (RingsSoon()) { target = 1f; rate = 1f / RaiseTime; }
        else if (since < strikeEnd + LowerDelay) return;
        else { target = 0f; rate = 1f / LowerTime; }
        armWeight = Mathf.MoveTowards(armWeight, target, rate * dt);
    }

    ArmPose CurrentPose()
    {
        float since = Time.time - strikeAt;
        float windup = strikeLead * WindupShare;
        if (since < windup) return Blend(Guard, Windup, Mathf.SmoothStep(0f, 1f, since / windup));
        if (since < strikeLead)
        {
            // Le coup s'abat en accélérant.
            float k = (since - windup) / (strikeLead - windup);
            return Blend(Windup, Impact, k * k);
        }
        if (since < strikeLead + RecoilTime) return Blend(Impact, Guard, Mathf.SmoothStep(0f, 1f, (since - strikeLead) / RecoilTime));
        return Guard;
    }

    static ArmPose Blend(ArmPose a, ArmPose b, float k) => new ArmPose(Vector3.Lerp(a.hand, b.hand, k), Vector3.Lerp(a.handle, b.handle, k));

    void PoseArm(ArmPose pose, float weight)
    {
        Vector3 ToWorld(Vector3 v) => transform.right * v.x + transform.up * v.y + transform.forward * v.z;

        Vector3 target = headBone.position + ToWorld(pose.hand);
        Vector3 shoulder = armBone.position;
        float upper = Vector3.Distance(shoulder, foreBone.position);
        float lower = Vector3.Distance(foreBone.position, handBone.position);
        Vector3 reach = target - shoulder;
        float distance = Mathf.Clamp(reach.magnitude, Mathf.Abs(upper - lower) + 0.01f, (upper + lower) * 0.98f);
        Vector3 direction = reach.normalized;

        // Coude : triangle épaule-coude-main, plié vers l'extérieur et vers le bas.
        Vector3 pole = Vector3.ProjectOnPlane(ToWorld(new Vector3(0.7f, -0.6f, -0.2f)), direction).normalized;
        float along = (upper * upper + distance * distance - lower * lower) / (2f * upper * distance);
        Vector3 elbow = shoulder + direction * (upper * along) + pole * (upper * Mathf.Sqrt(Mathf.Max(0f, 1f - along * along)));

        RotateBone(armBone, foreBone.position - shoulder, elbow - shoulder, weight);
        RotateBone(foreBone, handBone.position - foreBone.position, shoulder + direction * distance - foreBone.position, weight);
        RotateBone(handBone, hammer.up, ToWorld(pose.handle).normalized, weight);
    }

    static void RotateBone(Transform bone, Vector3 from, Vector3 to, float weight)
    {
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(from, to), weight) * bone.rotation;
    }
}
