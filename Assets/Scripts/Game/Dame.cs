using System.Collections.Generic;
using UnityEngine;

// La Dame (entité ID 2, type Cauchemar). Elle rôde au hasard dans le labyrinthe à la vitesse normale des joueurs,
// prend en chasse le premier personnage qu'elle voit (x1,05 ; x2,2 tant qu'il la regarde) pendant 20 à 30 secondes
// et attaque au contact (100 de dégâts, le survivant est repoussé). Elle ne saute pas, ne tombe pas et ne flotte pas :
// elle suit la grille de navigation (MazeNav), qui passe par les escaliers et les dalles surélevées mais pas au-dessus du vide.
// L'hôte simule son comportement (Simulate) ; les autres clients affichent l'état reçu (SetNetState).
public class Dame : MonoBehaviour
{
    public enum State : byte { Wander = 0, Chase = 1, Attack = 2 }

    const float Height = 2.66f;
    // Elle lance son attaque quand le personnage est à moins de 0,8 m ; le coup touche jusqu'à environ 2,12 m.
    const float AttackStartDistance = 0.8f, ReachVertical = 1.7f;
    const float HitDistance = (1.15f + 0.6f) * 1.1f * 1.1f;
    // Attaque courte : le clip est accéléré pour se jouer en entier pendant AttackDuration.
    const float AttackDuration = 0.7f;
    // Le coup porte quand ses bras sont au plus bas : à 30 % du clip d'attaque (mesuré sur l'animation Meshy).
    const float AttackHitFraction = 0.3f;
    const float AttackHitTime = AttackDuration * AttackHitFraction;
    const float EyeHeight = 2.28f, FieldOfView = 160f;
    // Hors chasse, elle sent un personnage qui passe tout près, devant comme dans son dos.
    const float SenseRadius = 4f;
    const float WaypointReach = 0.12f; // points de la grille tous les 0,5 m : on ne coupe pas les angles
    const float IgnoreAfterChase = 6f;

    public int Id { get; private set; }
    public State Current { get; private set; }
    public string TargetId { get; private set; } = "";
    public bool Watched;

    // Simulation (hôte).
    MazeNav nav;
    System.Random rnd;
    readonly List<int> path = new List<int>();
    int pathIndex;
    float repathTimer, chaseTimer, attackTimer;
    bool hitDone;
    readonly Dictionary<string, float> ignoreUntil = new Dictionary<string, float>();
    System.Action<Dame, string> onHit, onAttack;

    // Affichage.
    Animator animator;
    Vector3 netPos;
    float netYaw;
    bool hasNet;
    Vector3 lastPos;
    float visualSpeed;
    string animState = "";
    float attackClipLength = AttackDuration;

    public float Speed { get; private set; }

    // 'onAttack' : l'attaque commence contre ce joueur (il est immobilisé) ; 'onHit' : le coup porte.
    public static Dame Create(Transform parent, int id, Vector3 pos, float yaw, MazeNav nav, System.Action<Dame, string> onHit,
        System.Action<Dame, string> onAttack = null)
    {
        var go = new GameObject("La Dame " + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var dame = go.AddComponent<Dame>();
        dame.Id = id;
        dame.nav = nav;
        dame.onHit = onHit;
        dame.onAttack = onAttack;
        dame.rnd = new System.Random(id * 7919 + 13);
        dame.netPos = dame.lastPos = pos;
        dame.netYaw = yaw;
        dame.BuildVisual();
        return dame;
    }

    void BuildVisual()
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

    public void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        switch (Current)
        {
            case State.Attack:
                attackTimer += dt;
                if (!hitDone && attackTimer >= AttackHitTime)
                {
                    hitDone = true;
                    var target = Find(players, TargetId);
                    if (target != null && InReach(target, HitDistance)) onHit?.Invoke(this, TargetId);
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
                repathTimer -= dt;
                Vector3 feet = Feet(target);
                // Chemin recalculé souvent de près, plus rarement de loin (le calcul coûte quelques millisecondes).
                if (repathTimer <= 0f)
                    PlanPath(nav.NodeAt(feet), Mathf.Clamp(Horizontal(feet - transform.position).magnitude * 0.04f, 0.3f, 1.2f), 8000);
                // Au bout du chemin, tout près et au même niveau : droit sur le personnage.
                if (pathIndex >= path.Count && Horizontal(feet - transform.position).magnitude < 1f && Mathf.Abs(feet.y - transform.position.y) < 0.4f)
                    MoveTowards(new Vector3(feet.x, transform.position.y, feet.z), dt);
                else FollowPath(dt);
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
                    if (Sees(p, true)) { BeginChase(p); return; }
                }
                if (pathIndex >= path.Count)
                {
                    int node = nav.NodeAt(transform.position);
                    int zone = node >= 0 ? nav.ZoneOfNode(node) : 0;
                    PlanPath(nav.RandomNode(zone, rnd, transform.position, 10f, 40f), 0f);
                }
                FollowPath(dt);
                break;
            }
        }
    }

    void BeginChase(PlayerPresence p)
    {
        Current = State.Chase;
        TargetId = p.PlayerId;
        chaseTimer = Mathf.Lerp(GameRules.DameChaseMin, GameRules.DameChaseMax, (float)rnd.NextDouble());
        Watched = false;
        repathTimer = 0f;
    }

    void StopChase()
    {
        if (!string.IsNullOrEmpty(TargetId)) ignoreUntil[TargetId] = Time.time + IgnoreAfterChase;
        Current = State.Wander;
        TargetId = "";
        Watched = false;
        path.Clear();
        pathIndex = 0;
    }

    void StartAttack(string targetId)
    {
        Current = State.Attack;
        TargetId = targetId;
        attackTimer = 0f;
        hitDone = false;
        Speed = 0f;
        onAttack?.Invoke(this, targetId);
    }

    public static float AttackLength => AttackDuration;

    static PlayerPresence Find(IReadOnlyList<PlayerPresence> players, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var p in players) if (p.PlayerId == id) return p;
        return null;
    }

    // Cible possible : vivante, hors du Hub (endroit sûr).
    static bool Valid(PlayerPresence p) => p != null && p.Alive && p.isActiveAndEnabled && !WorldBuilder.IsInHub(p.Position);

    bool InReach(PlayerPresence p, float distance)
    {
        Vector3 d = p.Position - transform.position;
        return Horizontal(d).magnitude < distance && Mathf.Abs(d.y - 0.9f) < ReachVertical;
    }

    // Personnage à moins de 4 m (2 m en marche discrète), quel que soit l'angle, sans mur entre eux.
    bool Senses(PlayerPresence p)
    {
        Vector3 d = p.Position - transform.position;
        float radius = SenseRadius * (p.Sneaking ? 0.5f : 1f);
        if (Horizontal(d).magnitude > radius || Mathf.Abs(d.y - 0.9f) > 2.5f) return false;
        return !Physics.Linecast(transform.position + Vector3.up * 1.2f, p.Position + Vector3.up * 0.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    // Repère un personnage : à portée (moitié moins en marche discrète), dans son champ de vision et sans obstacle.
    bool Sees(PlayerPresence p, bool useFieldOfView)
    {
        Vector3 eye = transform.position + Vector3.up * EyeHeight;
        Vector3 target = p.Position + Vector3.up * 0.4f;
        Vector3 d = target - eye;
        float range = GameRules.DameSightRange * (p.Sneaking ? 0.5f : 1f);
        if (d.magnitude > range) return false;
        if (useFieldOfView && Vector3.Angle(transform.forward, Horizontal(d)) > FieldOfView * 0.5f) return false;
        return !Physics.Linecast(eye, target, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    // Pieds du personnage (sa position est le centre de sa capsule).
    static Vector3 Feet(PlayerPresence p) => p.Position + Vector3.down * 0.9f;

    void PlanPath(int goal, float delay, int maxExpansions = 20000)
    {
        repathTimer = delay;
        path.Clear();
        if (goal >= 0) path.AddRange(nav.Path(nav.NodeAt(transform.position), goal, maxExpansions));
        pathIndex = path.Count > 1 ? 1 : path.Count;
    }

    void FollowPath(float dt)
    {
        if (pathIndex >= path.Count) return;
        Vector3 wp = nav.Position(path[pathIndex]);
        if (Horizontal(wp - transform.position).magnitude < WaypointReach)
        {
            pathIndex++;
            if (pathIndex >= path.Count) return;
            wp = nav.Position(path[pathIndex]);
        }
        MoveTowards(wp, dt);
    }

    // Avance vers 'goal' (un point de la grille, ou le personnage au même niveau) ; la hauteur suit le segment,
    // donc les escaliers, sans jamais quitter le sol.
    void MoveTowards(Vector3 goal, float dt)
    {
        Vector3 d = Horizontal(goal - transform.position);
        float dist = d.magnitude;
        if (dist < 0.01f) return;
        float move = Mathf.Min(dist, Speed * dt);
        Vector3 p = transform.position + d / dist * move;
        p.y = Mathf.Lerp(transform.position.y, goal.y, move / dist);
        transform.position = p;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-8f * dt));
    }

    static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ================= Réseau et affichage =================

    public void SetNetState(Vector3 pos, float yaw, State state, string target)
    {
        netPos = pos;
        netYaw = yaw;
        if (state != Current) OnStateChanged(state);
        Current = state;
        TargetId = target;
        if (!hasNet)
        {
            hasNet = true;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }
    }

    public void OnStateChanged(State next)
    {
        if (next == State.Chase && Current == State.Wander) GameAudio.Play3D("dame_scream", transform.position + Vector3.up * 1.7f, 1f, 35f, 0.05f);
        if (next == State.Attack) GameAudio.Play3D("dame_attack", transform.position + Vector3.up * 1.4f, 1f, 25f, 0.05f);
    }

    // L'hôte appelle ceci avant de modifier l'état, pour jouer les mêmes sons que les clients.
    State lastSimState;

    public void Present(bool isHost, float dt)
    {
        if (isHost)
        {
            if (Current != lastSimState) OnStateChanged(Current);
        }
        else if (hasNet)
        {
            float k = 1f - Mathf.Exp(-12f * dt);
            transform.position = (transform.position - netPos).sqrMagnitude > 36f ? netPos : Vector3.Lerp(transform.position, netPos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, netYaw, 0f), 1f - Mathf.Exp(-10f * dt));
        }
        lastSimState = Current;

        // Vitesse affichée (identique chez tous) : animations.
        float moved = Horizontal(transform.position - lastPos).magnitude / Mathf.Max(dt, 0.0001f);
        lastPos = transform.position;
        visualSpeed = Mathf.Lerp(visualSpeed, moved, 1f - Mathf.Exp(-6f * dt));
        if (animator != null)
        {
            // En chasse, elle court (même à peine plus vite que sa marche).
            string wanted = Current == State.Attack ? "Attack" : visualSpeed < 0.3f ? "Idle" : Current == State.Chase ? "Run" : "Walk";
            if (wanted != animState)
            {
                animState = wanted;
                // L'attaque démarre presque sans fondu : le geste reste calé sur le moment du coup.
                animator.CrossFadeInFixedTime(wanted, wanted == "Attack" ? 0.03f : 0.2f);
            }
            animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 1.3f, 0.6f, 2f) : wanted == "Run" ? Mathf.Clamp(visualSpeed / 4.5f, 0.8f, 2.2f) : wanted == "Attack" ? attackClipLength / AttackDuration : 1f;
        }
    }
}
