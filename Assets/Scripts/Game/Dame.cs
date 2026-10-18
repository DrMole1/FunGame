using System.Collections.Generic;
using UnityEngine;

// La Dame (entité ID 2, type Cauchemar). Elle rôde au hasard dans le labyrinthe à la vitesse normale des joueurs,
// prend en chasse le premier personnage qu'elle voit (x1,5 ; x2,2 tant qu'il la regarde) pendant 20 à 30 secondes
// et attaque au contact (100 de dégâts, le survivant est repoussé). Elle ne saute pas mais emprunte les escaliers.
// L'hôte simule son comportement (Simulate) ; les autres clients affichent l'état reçu (SetNetState).
public class Dame : MonoBehaviour
{
    public enum State : byte { Wander = 0, Chase = 1, Attack = 2 }

    const float Height = 2.31f;
    const float ReachHorizontal = 1.15f, ReachVertical = 1.7f;
    const float AttackDuration = 1.5f, AttackHitTime = 0.6f;
    const float EyeHeight = 1.98f, FieldOfView = 160f;
    const float WaypointReach = 0.45f;
    const float IgnoreAfterChase = 6f;

    public int Id { get; private set; }
    public State Current { get; private set; }
    public string TargetId { get; private set; } = "";
    public bool Watched;

    // Simulation (hôte).
    MazeNav nav;
    Transform structure;
    System.Random rnd;
    readonly List<int> path = new List<int>();
    int pathIndex;
    float repathTimer, chaseTimer, attackTimer;
    bool hitDone;
    readonly Dictionary<string, float> ignoreUntil = new Dictionary<string, float>();
    readonly RaycastHit[] hits = new RaycastHit[8];
    System.Action<Dame, string> onHit;

    // Affichage.
    Animator animator;
    Vector3 netPos;
    float netYaw;
    bool hasNet;
    Vector3 lastPos;
    float visualSpeed;
    string animState = "";

    public float Speed { get; private set; }

    public static Dame Create(Transform parent, int id, Vector3 pos, float yaw, MazeNav nav, Transform structure, System.Action<Dame, string> onHit)
    {
        var go = new GameObject("La Dame " + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var dame = go.AddComponent<Dame>();
        dame.Id = id;
        dame.nav = nav;
        dame.structure = structure;
        dame.onHit = onHit;
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
                    if (target != null && InReach(target, 0.6f)) onHit?.Invoke(this, TargetId);
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
                int myNode = nav.NodeAt(transform.position), targetNode = nav.NodeAt(target.Position);
                // Même cellule (ou voisine et visible) : droit sur le personnage ; sinon, par le labyrinthe.
                if (myNode == targetNode || (Sees(target, false) && Horizontal(target.Position - transform.position).magnitude < 6f))
                    MoveTowards(target.Position, dt);
                else
                {
                    if (repathTimer <= 0f || pathIndex >= path.Count) PlanPath(targetNode);
                    FollowPath(dt);
                }
                if (InReach(target, 0f)) StartAttack(target.PlayerId);
                break;
            }

            default:
            {
                Speed = GameRules.BaseSpeed;
                Watched = false;
                // Au contact d'un joueur, elle l'attaque ; sinon elle cherche une proie du regard.
                foreach (var p in players)
                {
                    if (!Valid(p)) continue;
                    if (InReach(p, 0f)) { BeginChase(p); StartAttack(p.PlayerId); return; }
                    if (ignoreUntil.TryGetValue(p.PlayerId, out float until) && Time.time < until) continue;
                    if (Sees(p, true)) { BeginChase(p); return; }
                }
                if (pathIndex >= path.Count)
                {
                    int node = nav.NodeAt(transform.position);
                    int zone = node >= 0 ? nav.ZoneOfNode(node) : 0;
                    PlanPath(nav.RandomNode(zone, rnd, transform.position, 15f));
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
    }

    static PlayerPresence Find(IReadOnlyList<PlayerPresence> players, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var p in players) if (p.PlayerId == id) return p;
        return null;
    }

    // Cible possible : vivante, hors du Hub (endroit sûr).
    static bool Valid(PlayerPresence p) => p != null && p.Alive && p.isActiveAndEnabled && !WorldBuilder.IsInHub(p.Position);

    bool InReach(PlayerPresence p, float margin)
    {
        Vector3 d = p.Position - transform.position;
        return Horizontal(d).magnitude < ReachHorizontal + margin && Mathf.Abs(d.y - 0.9f) < ReachVertical;
    }

    // Repère un personnage : à portée (moitié moins en marche discrète), dans son champ de vision et sans obstacle.
    bool Sees(PlayerPresence p, bool useFieldOfView)
    {
        Vector3 eye = transform.position + Vector3.up * EyeHeight;
        Vector3 target = p.Position + Vector3.up * 0.4f;
        Vector3 d = target - eye;
        float range = GameRules.DameSightRange * (p.Sneaking ? 0.5f : 1f);
        if (d.magnitude > range) return false;
        if (useFieldOfView && d.magnitude > 2.5f && Vector3.Angle(transform.forward, Horizontal(d)) > FieldOfView * 0.5f) return false;
        return !Physics.Linecast(eye, target, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    void PlanPath(int goal)
    {
        repathTimer = 0.5f;
        path.Clear();
        path.AddRange(nav.Path(nav.NodeAt(transform.position), goal));
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

    void MoveTowards(Vector3 goal, float dt)
    {
        Vector3 d = Horizontal(goal - transform.position);
        float dist = d.magnitude;
        if (dist < 0.01f) return;
        Vector3 step = d / dist * Mathf.Min(dist, Speed * dt);
        Vector3 p = transform.position + step;
        p.y = GroundAt(p, transform.position.y);
        transform.position = p;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-8f * dt));
    }

    // Sol du bâti sous la Dame (escaliers, dalles) ; au-dessus d'une fosse, elle glisse au même niveau.
    float GroundAt(Vector3 p, float current)
    {
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, current + 1.2f, p.z), Vector3.down, hits, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int k = 0; k < n; k++)
            if (structure != null && hits[k].transform.IsChildOf(structure) && hits[k].point.y > best) best = hits[k].point.y;
        return float.IsNegativeInfinity(best) ? current : best;
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
            string wanted = Current == State.Attack ? "Attack" : visualSpeed > GameRules.BaseSpeed * 1.2f ? "Run" : visualSpeed > 0.3f ? "Walk" : "Idle";
            if (wanted != animState)
            {
                animState = wanted;
                animator.CrossFadeInFixedTime(wanted, 0.2f);
            }
            animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 1.3f, 0.6f, 2f) : wanted == "Run" ? Mathf.Clamp(visualSpeed / 4.5f, 0.8f, 2.2f) : 1f;
        }
    }
}
