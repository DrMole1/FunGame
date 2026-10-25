using System.Collections.Generic;
using UnityEngine;

// La Dame (entité ID 2, type Cauchemar, V0.4.6). Elle rôde au hasard dans le labyrinthe à la vitesse normale des joueurs ;
// ses pas résonnent en écho jusqu'à 30 m. Dès qu'elle voit un personnage à 20 m (ou qu'il passe à moins de 4 m d'elle),
// elle crie et le prend en chasse (x1,05 ; x2,2 tant qu'il la regarde) pendant 12 à 15 secondes, et attaque au contact
// (100 de dégâts, le survivant est repoussé). Elle ne saute pas, ne tombe pas et ne flotte pas : elle suit la grille de
// navigation (MazeNav), qui passe par les escaliers et les dalles surélevées mais pas au-dessus du vide.
// V0.4.7 : "en recherche", il lui arrive de pousser un cri de souffrance (tête renversée, 1,5 s, sans cesser d'avancer),
// à 12 secondes d'intervalle au moins.
public class Dame : Entity
{
    public enum State : byte { Wander = 0, Chase = 1, Attack = 2 }

    // Elle lance son attaque quand le personnage est à moins de 0,8 m ; le coup touche jusqu'à environ 2,12 m.
    const float AttackStartDistance = 0.8f;
    const float HitDistance = (1.15f + 0.6f) * 1.1f * 1.1f * GameRules.BigEntityScale;
    // Attaque courte : le clip est accéléré pour se jouer en entier pendant AttackDuration.
    const float AttackDuration = 0.7f;
    // Le coup porte quand ses bras sont au plus bas : à 30 % du clip d'attaque (mesuré sur l'animation Meshy).
    const float AttackHitFraction = 0.3f;
    const float AttackHitTime = AttackDuration * AttackHitFraction;
    const float EyeHeight = 2.28f * GameRules.BigEntityScale, FieldOfView = 160f;
    // Hors chasse, elle sent un personnage qui passe tout près, devant comme dans son dos.
    const float SenseRadius = 4f;
    const float IgnoreAfterChase = 6f;

    public override EntityKind Kind => EntityKind.Dame;
    public override string DisplayName => "La Dame";
    public override float ChestHeight => 1.4f * GameRules.BigEntityScale;
    protected override float ModelScale => GameRules.BigEntityScale;
    public override bool Nightmare => true;
    public override Vector3 EyesLocal => new Vector3(0f, EyeHeight, 0.22f);
    public State Current { get => (State)state; private set => state = (byte)value; }
    public bool Watched;

    float chaseTimer, attackTimer, wailTimer;
    bool hitDone;
    readonly Dictionary<string, float> ignoreUntil = new Dictionary<string, float>();
    float attackClipLength = AttackDuration;

    protected override void Init() => wailTimer = Range(GameRules.DameWailMin, GameRules.DameWailMax);

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
            foreach (var clip in art.controller.animationClips)
                if (clip.name == "Attack") attackClipLength = clip.length;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (animator.isHuman)
            {
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
                chestBone = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
            }
        }
        else
        {
            // Repli : robe, voile et couronne en primitives.
            Material robe = GameArt.Lit("DameRobe", new Color(0.55f, 0.55f, 0.56f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
            Material mask = GameArt.Lit("DameMask", new Color(0.85f, 0.84f, 0.8f), null, 0.6f);
            Material iron = GameArt.Lit("DameCrown", new Color(0.12f, 0.12f, 0.13f), null, 0.7f);
            GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.9f, 0.8f, 0.7f), robe);
            GameArt.Prim(PrimitiveType.Sphere, transform, new Vector3(0f, 1.62f, 0f), new Vector3(0.95f, 0.5f, 0.75f), robe);
            GameArt.Prim(PrimitiveType.Sphere, transform, new Vector3(0f, 1.85f, 0f), new Vector3(0.36f, 0.44f, 0.38f), robe);
            GameArt.Prim(PrimitiveType.Sphere, transform, new Vector3(0f, 1.86f, 0.12f), new Vector3(0.24f, 0.32f, 0.16f), mask);
            GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 2.07f, 0f), new Vector3(0.28f, 0.06f, 0.28f), iron);
        }
    }

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        switch (Current)
        {
            case State.Attack:
                attackTimer += dt;
                if (!hitDone && attackTimer >= AttackHitTime)
                {
                    hitDone = true;
                    var target = Find(players, TargetId);
                    if (target != null && InReach(target, HitDistance)) Host?.Hit(this, TargetId, GameRules.DameDamage, GameRules.DameKnockback);
                }
                if (attackTimer >= AttackDuration)
                {
                    var target = Find(players, TargetId);
                    if (chaseTimer > 0f && Valid(target)) Current = State.Chase;
                    else StopChase();
                }
                break;

            case State.Chase:
            {
                chaseTimer -= dt;
                var target = Find(players, TargetId);
                if (chaseTimer <= 0f || !Valid(target)) { StopChase(); break; }
                Speed = GameRules.BaseSpeed * (Watched ? GameRules.DameWatchedMultiplier : GameRules.DameChaseMultiplier);
                Pursue(target, dt);
                if (InReach(target, AttackStartDistance)) StartAttack(target.PlayerId);
                break;
            }

            default:
            {
                Speed = GameRules.BaseSpeed;
                Watched = false;
                // Au contact d'un joueur, elle l'attaque ; tout près d'elle, elle le chasse ; sinon elle cherche une proie du regard.
                foreach (var p in players)
                {
                    if (!Valid(p)) continue;
                    if (InReach(p, AttackStartDistance)) { BeginChase(p); StartAttack(p.PlayerId); return; }
                    if (Senses(p)) { BeginChase(p); return; }
                    if (ignoreUntil.TryGetValue(p.PlayerId, out float until) && Time.time < until) continue;
                    if (Sees(p, EyeHeight, GameRules.DameSightRange, FieldOfView)) { BeginChase(p); return; }
                }
                // Cri de souffrance, de temps en temps (au moins 12 s d'écart).
                wailTimer -= dt;
                if (wailTimer <= 0f)
                {
                    wailTimer = Range(GameRules.DameWailMin, GameRules.DameWailMax);
                    Pulse++;
                }
                Wander(dt);
                break;
            }
        }
    }

    void BeginChase(PlayerPresence p)
    {
        Current = State.Chase;
        TargetId = p.PlayerId;
        chaseTimer = Range(GameRules.DameChaseMin, GameRules.DameChaseMax);
        Watched = false;
        repathTimer = 0f;
    }

    void StopChase()
    {
        if (!string.IsNullOrEmpty(TargetId)) ignoreUntil[TargetId] = Time.time + IgnoreAfterChase;
        Current = State.Wander;
        TargetId = "";
        Watched = false;
        ClearPath();
    }

    void StartAttack(string targetId)
    {
        Current = State.Attack;
        TargetId = targetId;
        attackTimer = 0f;
        hitDone = false;
        Speed = 0f;
        // Le joueur visé ne peut plus bouger jusqu'à la fin de son geste.
        Host?.Immobilize(this, targetId, AttackDuration);
    }

    // Personnage à moins de 4 m (2 m en marche discrète), quel que soit l'angle, sans mur entre eux.
    bool Senses(PlayerPresence p)
    {
        Vector3 d = p.Position - transform.position;
        float radius = SenseRadius * (p.Sneaking ? 0.5f : 1f);
        if (Horizontal(d).magnitude > radius || Mathf.Abs(d.y - 0.9f) > 2.5f) return false;
        return ClearLine(transform.position + Vector3.up * 1.2f, p.Position + Vector3.up * 0.4f);
    }

    // ================= Affichage =================

    protected override void OnStateChanged(byte previous, byte next)
    {
        // Elle crie en repérant un joueur (Dame_Cri), pitché au hasard. GDD 12 (V0.5.2) : Dame_Cri et Dame_Attaque
        // ne sont entendus que par le joueur visé.
        if ((State)next == State.Chase && (State)previous == State.Wander && TargetIsLocal)
            GameAudio.Play3DSmooth("dame_scream", transform.position + Vector3.up * 1.7f, 1f, GameRules.DameScreamRange, RandomPitch());
        if ((State)next == State.Attack && TargetIsLocal) GameAudio.Play3D("dame_attack", transform.position + Vector3.up * 1.4f, 1f, 25f, 0f);
    }

    public override string KillSound => "dame_kill";

    // Cri de souffrance (en recherche, Dame_Cri2) : son et geste, la tête renversée.
    Transform headBone, neckBone, chestBone;
    float wailShownAt = -10f;

    protected override void OnPulse()
    {
        wailShownAt = Time.time;
        GameAudio.Play3DSmooth("dame_wail", transform.position + Vector3.up * 1.7f, 1f, GameRules.DameScreamRange, RandomPitch());
    }

    void LateUpdate()
    {
        // Après l'Animator : le buste se cambre, la tête bascule en arrière et tremble, le temps du cri.
        float t = Time.time - wailShownAt;
        if (t >= GameRules.DameWailDuration || Current != State.Wander) return;
        float w = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / GameRules.DameWailDuration));
        float shake = Mathf.Sin(t * 47f) * 4f + Mathf.Sin(t * 31f) * 3f;
        Vector3 side = transform.right;
        if (chestBone != null) chestBone.rotation = Quaternion.AngleAxis(-14f * w, side) * chestBone.rotation;
        if (neckBone != null) neckBone.rotation = Quaternion.AngleAxis((-18f + shake * 0.4f) * w, side) * neckBone.rotation;
        if (headBone != null) headBone.rotation = Quaternion.AngleAxis((-24f + shake) * w, side) * Quaternion.AngleAxis(shake * w, transform.up) * headBone.rotation;
    }

    readonly GameAudio.FadedLoop steps = new GameAudio.FadedLoop("dame_step", 0.7f, GameRules.DameStepRange, 0.35f);

    protected override void Animate(float dt)
    {
        // Pas résonnant en écho (Dame_Deplacement, en boucle), à moins de 30 m : tempo plus rapide quand elle court.
        steps.Update(transform, Vector3.zero, visualSpeed > 0.3f && Current != State.Attack, Current == State.Chase ? 1.3f : 1f, dt);
        if (animator == null) return;
        // En chasse, elle court (même à peine plus vite que sa marche).
        string wanted = Current == State.Attack ? "Attack" : visualSpeed < 0.3f ? "Idle" : Current == State.Chase ? "Run" : "Walk";
        // L'attaque démarre presque sans fondu : le geste reste calé sur le moment du coup.
        PlayAnim(wanted, wanted == "Attack" ? 0.03f : 0.2f);
        animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 1.3f, 0.6f, 2f) : wanted == "Run" ? Mathf.Clamp(visualSpeed / 4.5f, 0.8f, 2.2f) : wanted == "Attack" ? attackClipLength / AttackDuration : 1f;
    }
}
