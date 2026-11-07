using System.Collections.Generic;
using UnityEngine;

// La Tête de Cloche (entité ID 11, type Sanction, V0.6) : grande entité bipède (taille de La Dame), une cloche de bronze à
// la place de la tête, armée d'un marteau gravé "WOOF". Elle n'apparaît qu'en cas de Sanction (ordre d'un glyphe non
// respecté), à 30 m du joueur sanctionné, et ne poursuit que lui : à sa vitesse les 7 premières secondes, puis elle
// accélère chaque seconde jusqu'à 2x au bout de 20 secondes. À moins de 0,8 m, elle l'immobilise et le tue. Elle s'éloigne
// ensuite lentement du cadavre, sans attaquer personne, et disparaît dans un nuage de fumée à 20 m de lui. Si le joueur se
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
    public override string KillSound => "tete_bell";
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
                // Le temps que tous les joueurs voient la fumée, puis elle quitte la partie.
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

    // Corps de La Dame (modèle Meshy animé), sa tête remplacée par une cloche de bronze, le marteau dans la main droite.
    Transform headBone, armBone, hammer;
    float strikeAt = -10f;
    const float StrikeDuration = 0.45f;

    protected override void BuildVisual()
    {
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
            if (animator.isHuman)
            {
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                armBone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform fore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                if (headBone != null) BuildBell(headBone);
                if (hand != null && fore != null) hammer = BuildHammer(hand, hand.position - fore.position);
            }
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

    // ================= Affichage =================

    readonly GameAudio.FadedLoop steps = new GameAudio.FadedLoop("dame_step", 0.6f, 25f, 0.35f);

    protected override void OnPulse()
    {
        strikeAt = Time.time;
        GameAudio.Play3D("tete_bell", transform.position + Vector3.up * EyesLocal.y, 1f, 60f, 0.03f);
        Rang?.Invoke();
    }

    protected override void OnStateChanged(byte previous, byte next)
    {
        if ((State)next != State.Vanish) return;
        // Nuage de fumée grise : elle s'y dissipe.
        Vector3 at = transform.position + Vector3.up * 1.4f;
        Fx.Burst(at, GameArt.Lit("FumeeTete", new Color(0.35f, 0.35f, 0.36f), null, 0f), 26, 2.2f, 0.45f, 1.6f, -0.2f);
        GameAudio.Play3D("fou_poof", at, 0.6f, 25f, 0.05f, 0.6f);
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
    }

    protected override void Animate(float dt)
    {
        steps.Update(transform, Vector3.zero, visualSpeed > 0.3f && Current != State.Vanish, Current == State.Hunt ? 1.2f : 0.8f, dt);
        if (animator == null) return;
        string wanted = Current == State.Kill ? "Attack" : visualSpeed < 0.3f ? "Idle" : visualSpeed > 5f ? "Run" : "Walk";
        PlayAnim(wanted, wanted == "Attack" ? 0.05f : 0.2f);
        animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 1.3f, 0.5f, 2f) : wanted == "Run" ? Mathf.Clamp(visualSpeed / 4.5f, 0.8f, 2.2f) : 1f;
    }

    void LateUpdate()
    {
        // Après l'Animator : le bras droit lève le marteau puis l'abat sur la cloche.
        float t = Time.time - strikeAt;
        if (armBone == null || t >= StrikeDuration) return;
        float k = t / StrikeDuration;
        float lift = k < 0.55f ? Mathf.SmoothStep(0f, 1f, k / 0.55f) : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.55f) / 0.45f);
        armBone.rotation = Quaternion.AngleAxis(-150f * lift, transform.right) * armBone.rotation;
    }
}
