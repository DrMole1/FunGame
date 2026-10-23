using System.Collections.Generic;
using UnityEngine;

// Identifiants du GDD (chapitre 8) ; le Laser (ID 1) est un piège du décor, pas une entité mobile.
public enum EntityKind : byte { Dame = 2, Tour = 3, Pion = 4, Cloche = 5, Spider = 6, Fou = 7, Roi = 8, Cavalier = 9, Reveil = 10 }

// Ce que les entités demandent au jeu (implémenté par GameSession, chez l'hôte).
public interface IEntityHost
{
    // Le coup porte : dégâts et recul chez le joueur visé.
    void Hit(Entity entity, string targetId, int damage, float knockback);
    // Le joueur visé ne peut plus bouger pendant 'seconds'.
    void Immobilize(Entity entity, string targetId, float seconds);
    // Son d'alerte joué uniquement chez ce joueur (La Tour l'a repéré).
    void Alert(Entity entity, string targetId);
    // Le Pion vole un objet ramassable au joueur touché ; 'done' reçoit l'objet (None : rien à voler).
    void Steal(Entity entity, string targetId, System.Action<ItemType> done);
    // Le Pion dépose l'objet volé au sol.
    void DropStolen(Entity entity, ItemType type, Vector3 position);
    // Le Fou échange sa position avec celle du joueur visé : celui-ci est transporté en 'position' (au sol).
    void Swap(Entity entity, string targetId, Vector3 position);
    // Le Roi a accroché (on) ou lâché (off) le joueur avec sa chaîne.
    void Chain(Entity entity, string targetId, bool on);
    // Balises posées dans le labyrinthe (Le Roi s'y dirige).
    IEnumerable<Vector3> BeaconPositions();
    // Secondes depuis la dernière action bruyante du joueur (aboiement, saut, objet ramassé ou posé).
    float NoiseAge(string playerId);
    // Le Réveil explose : dégâts et recul aux joueurs dans le rayon (Hub compris), objets détruits (hors Hub).
    void Explode(Entity entity, Vector3 center, float radius, int damage, float knockback);
}

// Entité mobile : l'hôte simule (Simulate) et envoie position, état, cible, compteur d'événements et données propres
// (Extra) ; les autres clients rejouent la position en douceur (NetSmoother) et réagissent aux changements d'état
// et d'événements (sons, animations), comme l'hôte.
public abstract class Entity : MonoBehaviour
{
    public static IEntityHost Host;

    const float WaypointReach = 0.12f; // points de la grille tous les 0,5 m : on ne coupe pas les angles

    public int Id { get; private set; }
    public abstract EntityKind Kind { get; }
    public abstract string DisplayName { get; }
    public string TargetId { get; protected set; } = "";
    public byte NetState => state;
    // Compteur d'événements ponctuels (cri, scintillement...) : chaque incrément est joué une fois chez tous.
    public int Pulse { get; protected set; }
    // Hauteur du regard des joueurs qui la fixent (La Dame, La Cloche).
    public virtual float ChestHeight => 1f;
    // Type Cauchemar : le personnage tué voit la caméra plonger dans ses yeux (EyesLocal, dans le repère de l'entité).
    public virtual bool Nightmare => false;
    public virtual Vector3 EyesLocal => new Vector3(0f, ChestHeight + 0.6f, 0.2f);
    // Agrandissement du modèle (La Dame, La Tour, Le Roi, Le Cavalier : +25 %).
    protected virtual float ModelScale => 1f;
    // L'entité a fini son rôle (Le Réveil a explosé) : l'hôte la retire de la partie.
    public bool Expired { get; protected set; }

    protected byte state;
    protected MazeNav nav;
    protected System.Random rnd;
    protected readonly List<int> path = new List<int>();
    protected int pathIndex;
    protected float repathTimer;
    public float Speed { get; protected set; }

    // Affichage.
    protected Animator animator;
    protected float visualSpeed;
    readonly NetSmoother smoother = new NetSmoother();
    bool hasNet;
    Vector3 lastPos;
    byte shownState;
    int shownPulse;
    string animState = "";

    public static T Create<T>(Transform parent, int id, Vector3 pos, float yaw, MazeNav nav) where T : Entity
    {
        var go = new GameObject(typeof(T).Name + " " + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var e = go.AddComponent<T>();
        e.Id = id;
        e.nav = nav;
        e.rnd = new System.Random(id * 7919 + 13 + (int)e.Kind * 104729);
        e.lastPos = pos;
        e.BuildVisual();
        var model = go.transform.Find("Modele");
        if (model != null) model.localScale *= e.ModelScale;
        AddGlow(go);
        e.Init();
        return e;
    }

    // Légère lueur propre (de la couleur de la texture) : les entités se devinent dans l'obscurité du labyrinthe.
    // Les matériaux déjà lumineux (yeux, auras) ou transparents gardent leur rendu.
    const float GlowLevel = 0.07f;

    static void AddGlow(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty("_EmissionColor") || m.IsKeywordEnabled("_EMISSION") || m.renderQueue > 2500) continue;
                m = new Material(m) { name = m.name + " (lueur)" };
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty("_EmissionMap") && m.mainTexture != null) m.SetTexture("_EmissionMap", m.mainTexture);
                Color c = m.HasProperty("_Color") ? m.color : Color.white;
                m.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * GlowLevel);
                mats[i] = m;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    protected abstract void BuildVisual();
    protected virtual void Init() { }
    public abstract void Simulate(float dt, IReadOnlyList<PlayerPresence> players);

    // Données propres à l'entité, sans ',' ';' ni '|'.
    public virtual string Extra => "";
    protected virtual void ApplyExtra(string extra) { }

    // ================= Réseau et affichage =================

    public void SetNetState(double time, Vector3 pos, float yaw, byte netState, string target, int pulse, string extra)
    {
        smoother.Push(time, pos, yaw);
        state = netState;
        TargetId = target;
        Pulse = pulse;
        ApplyExtra(extra);
        if (!hasNet)
        {
            hasNet = true;
            shownState = netState;
            shownPulse = pulse;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }
    }

    // Chez tous (hôte compris) : sons et animations selon l'état affiché.
    public void Present(bool isHost, float dt)
    {
        if (!isHost && hasNet)
        {
            smoother.Sample(out Vector3 pos, out float yaw);
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }
        if (isHost || hasNet)
        {
            if (state != shownState) OnStateChanged(shownState, state);
            shownState = state;
            if (Pulse != shownPulse) OnPulse();
            shownPulse = Pulse;
        }
        float moved = Horizontal(transform.position - lastPos).magnitude / Mathf.Max(dt, 0.0001f);
        lastPos = transform.position;
        visualSpeed = Mathf.Lerp(visualSpeed, moved, 1f - Mathf.Exp(-6f * dt));
        Animate(dt);
    }

    protected virtual void OnStateChanged(byte previous, byte next) { }
    protected virtual void OnPulse() { }
    protected virtual void Animate(float dt) { }

    // Son joué chez tous quand l'entité tue un joueur (GDD 12), ou null.
    public virtual string KillSound => null;
    // Son joué chez tous quand l'entité blesse un joueur sans le tuer (GDD 12, V0.4.9), ou null ; il sert aussi quand elle
    // tue, si elle n'a pas de son de mise à mort.
    public virtual string HurtSound => null;

    public void PlayKillSound() => PlayAt(KillSound ?? HurtSound);

    public void PlayHurtSound() => PlayAt(HurtSound);

    void PlayAt(string clip)
    {
        if (clip != null) GameAudio.Play3D(clip, transform.position + Vector3.up * ChestHeight, 1f, 35f, 0f);
    }

    // Modèle Meshy animé (Resources/<art>) ; faux si absent.
    protected bool LoadCreature(string art)
    {
        var data = Resources.Load<CreatureArt>(art);
        if (data == null || data.model == null) return false;
        var model = Instantiate(data.model, transform);
        model.name = "Modele";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * data.scale;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = data.controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        return true;
    }

    protected void PlayAnim(string wanted, float fade = 0.2f)
    {
        if (animator == null || wanted == animState) return;
        animState = wanted;
        animator.CrossFadeInFixedTime(wanted, fade);
    }

    protected string CurrentAnim => animState;

    // ================= Outils de simulation (hôte) =================

    protected static PlayerPresence Find(IReadOnlyList<PlayerPresence> players, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var p in players) if (p.PlayerId == id) return p;
        return null;
    }

    // Personnage d'un joueur, chez tous (affichage).
    protected static PlayerPresence FindPresence(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var p in PlayerPresence.All) if (p.PlayerId == id) return p;
        return null;
    }

    // Cible possible : vivante, hors du Hub (endroit sûr).
    protected static bool Valid(PlayerPresence p) => p != null && p.Alive && p.isActiveAndEnabled && !WorldBuilder.IsInHub(p.Position);

    // Pieds du personnage (sa position est le centre de sa capsule).
    protected static Vector3 Feet(PlayerPresence p) => p.Position + Vector3.down * 0.9f;

    protected static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

    protected float HorizontalDistance(PlayerPresence p) => Horizontal(p.Position - transform.position).magnitude;

    protected bool InReach(PlayerPresence p, float distance, float vertical = 1.7f)
    {
        Vector3 d = p.Position - transform.position;
        return Horizontal(d).magnitude < distance && Mathf.Abs(d.y - 0.9f) < vertical;
    }

    // Seuls les murs et le décor coupent la vue (les joueurs et les entités n'ont pas de collider bloquant).
    protected static bool ClearLine(Vector3 from, Vector3 to) =>
        !Physics.Linecast(from, to, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

    // Voit le personnage : à portée (moitié moins en marche discrète, GDD 3.1), dans le champ de vision et sans mur.
    protected bool Sees(PlayerPresence p, float eyeHeight, float range, float fieldOfView, bool sneakHalves = true)
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 target = p.Position + Vector3.up * 0.4f;
        Vector3 d = target - eye;
        if (sneakHalves && p.Sneaking) range *= 0.5f;
        if (d.magnitude > range) return false;
        if (fieldOfView < 360f && Vector3.Angle(transform.forward, Horizontal(d)) > fieldOfView * 0.5f) return false;
        return ClearLine(eye, target);
    }

    protected void PlanPath(int goal, float delay, int maxExpansions = 20000)
    {
        repathTimer = delay;
        path.Clear();
        if (goal >= 0) path.AddRange(nav.Path(nav.NodeAt(transform.position), goal, maxExpansions));
        pathIndex = path.Count > 1 ? 1 : path.Count;
    }

    protected void ClearPath()
    {
        path.Clear();
        pathIndex = 0;
    }

    protected bool PathDone => pathIndex >= path.Count;

    protected void FollowPath(float dt)
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

    // Poursuite d'un personnage sur la grille ; tout près et au même niveau, droit sur lui.
    protected void Pursue(PlayerPresence target, float dt)
    {
        Vector3 feet = Feet(target);
        repathTimer -= dt;
        if (repathTimer <= 0f)
            PlanPath(nav.NodeAt(feet), Mathf.Clamp(Horizontal(feet - transform.position).magnitude * 0.04f, 0.3f, 1.2f), 8000);
        if (pathIndex >= path.Count && Horizontal(feet - transform.position).magnitude < 1f && Mathf.Abs(feet.y - transform.position.y) < 0.4f)
            MoveTowards(new Vector3(feet.x, transform.position.y, feet.z), dt);
        else FollowPath(dt);
    }

    // Promenade au hasard dans la zone de l'entité.
    protected void Wander(float dt, float minDistance = 10f, float maxDistance = 40f)
    {
        if (pathIndex >= path.Count)
        {
            int node = nav.NodeAt(transform.position);
            int zone = node >= 0 ? nav.ZoneOfNode(node) : 0;
            PlanPath(nav.RandomNode(zone, rnd, transform.position, minDistance, maxDistance), 0f);
        }
        FollowPath(dt);
    }

    // Avance vers 'goal' (un point de la grille, ou le personnage au même niveau) ; la hauteur suit le segment,
    // donc les escaliers, sans jamais quitter le sol.
    protected void MoveTowards(Vector3 goal, float dt)
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

    protected void Face(Vector3 point, float dt)
    {
        Vector3 d = Horizontal(point - transform.position);
        if (d.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-8f * dt));
    }

    protected float Range(float min, float max) => Mathf.Lerp(min, max, (float)rnd.NextDouble());

    // Point de la grille de sa zone, à 10-40 m par défaut, de préférence dans la direction donnée (demi-tour ou tout droit).
    protected int NodeAround(Vector3 direction, float minDistance = 10f, float maxDistance = 40f)
    {
        int here = nav.NodeAt(transform.position);
        int zone = here >= 0 ? nav.ZoneOfNode(here) : 0;
        int best = -1;
        for (int k = 0; k < 12; k++)
        {
            int node = nav.RandomNode(zone, rnd, transform.position, minDistance, maxDistance);
            if (node < 0) continue;
            best = node;
            if (Vector3.Dot(Horizontal(nav.Position(node) - transform.position), direction) > 0f) break;
        }
        return best;
    }

    // Pitch au hasard entre -1 et +1 demi-ton.
    protected static float RandomPitch() => Mathf.Pow(2f, Random.Range(-1f, 1f) / 12f);
}
