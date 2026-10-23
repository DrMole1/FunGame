using System.Collections.Generic;
using UnityEngine;

// La Cloche (entité ID 5, type Normal) : petite cloche de bronze sur pattes, de la vitesse d'un joueur qui marche.
// Immobile au départ, elle "obnubile" tout joueur qui entre dans son champ de vision (360°, 25 m) : sa caméra est forcée
// de la regarder et il prend des dégâts toutes les 2 secondes, jusqu'à ce qu'un mur coupe la ligne de vue. Elle scintille
// (animation et son) en obnubilant. Elle marche ensuite vers le premier joueur obnubilé, jusqu'à 2 m (puis scintille),
// ou vers un autre joueur obnubilé s'il lui échappe. Sans plus personne d'obnubilé, elle part en courant (x1,5) vers un
// endroit de sa zone à 50-70 m ("en recherche", sans obnubiler), puis redevient immobile.
public class Cloche : Entity
{
    public enum State : byte { Idle = 0, Approach = 1, Search = 2 }

    const float EyeHeight = 0.7f;

    public override EntityKind Kind => EntityKind.Cloche;
    public override string DisplayName => "La Cloche";
    public override float ChestHeight => 0.65f;
    public State Current { get => (State)state; private set => state = (byte)value; }

    // Joueurs obnubilés, dans l'ordre où ils l'ont été.
    readonly List<string> obsessed = new List<string>();
    readonly List<string> scratch = new List<string>();
    bool arrived;
    float searchTimer;

    public bool Obsesses(string playerId) => obsessed.Contains(playerId);

    // Scintillement.
    Light flash;
    Renderer[] bodies;
    MaterialPropertyBlock block;
    float shimmer;

    protected override void BuildVisual()
    {
        if (!LoadCreature("ClocheArt"))
        {
            Material bronze = GameArt.Lit("ClocheBronze", new Color(0.72f, 0.6f, 0.32f), null, 0.7f);
            GameArt.Prim(PrimitiveType.Sphere, transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.5f, 0.55f, 0.5f), bronze);
            GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.12f, 0.25f, 0.12f), bronze);
        }
        bodies = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        flash = new GameObject("Scintillement").AddComponent<Light>();
        flash.transform.SetParent(transform, false);
        flash.transform.localPosition = new Vector3(0f, 0.8f, 0.4f);
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.85f, 0.5f);
        flash.range = 5f;
        flash.shadows = LightShadows.None;
        flash.enabled = false;
    }

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        if (Current == State.Search)
        {
            obsessed.Clear();
            TargetId = "";
            Speed = GameRules.BaseSpeed * GameRules.ClocheSearchMultiplier;
            searchTimer += dt;
            FollowPath(dt);
            if (PathDone || searchTimer > 30f)
            {
                Current = State.Idle;
                Speed = 0f;
            }
            return;
        }

        // Qui la voit (et qu'elle voit) : 360°, 25 m ; seul un mur coupe la vision, même en marche discrète.
        scratch.Clear();
        foreach (var p in players)
            if (Valid(p) && Sees(p, EyeHeight, GameRules.ClocheSightRange, 360f, false)) scratch.Add(p.PlayerId);
        obsessed.RemoveAll(id => !scratch.Contains(id));
        bool newcomer = false;
        foreach (string id in scratch)
            if (!obsessed.Contains(id)) { obsessed.Add(id); newcomer = true; }
        if (newcomer) Pulse++;

        if (obsessed.Count == 0)
        {
            if (Current == State.Approach) BeginSearch();
            return;
        }
        // Elle va vers le premier joueur obnubilé (ou le suivant, si le premier lui a échappé).
        if (Current == State.Idle || !obsessed.Contains(TargetId))
        {
            Current = State.Approach;
            TargetId = obsessed[0];
            arrived = false;
            repathTimer = 0f;
        }
        var target = Find(players, TargetId);
        if (target == null) return;
        Speed = GameRules.BaseSpeed;
        if (HorizontalDistance(target) > GameRules.ClocheStopDistance)
        {
            arrived = false;
            Pursue(target, dt);
        }
        else
        {
            Face(target.Position, dt);
            if (!arrived) Pulse++;
            arrived = true;
        }
    }

    void BeginSearch()
    {
        Current = State.Search;
        TargetId = "";
        searchTimer = 0f;
        int here = nav.NodeAt(transform.position);
        int zone = here >= 0 ? nav.ZoneOfNode(here) : 0;
        PlanPath(nav.RandomNode(zone, rnd, transform.position, GameRules.ClocheSearchMin, GameRules.ClocheSearchMax), 0f);
    }

    public override string Extra => string.Join("+", obsessed);

    protected override void ApplyExtra(string extra)
    {
        obsessed.Clear();
        if (extra.Length > 0) obsessed.AddRange(extra.Split('+'));
    }

    // ================= Affichage =================

    // Toutes Cloches confondues : jamais deux tintements à moins d'1 seconde d'intervalle.
    const float RingInterval = 1f;
    static float lastRing = float.NegativeInfinity;

    protected override void OnPulse()
    {
        shimmer = 1f;
        if (Time.time - lastRing < RingInterval) return;
        lastRing = Time.time;
        GameAudio.Play3D("cloche_ring", transform.position + Vector3.up * 0.7f, 1f, 40f, 0.04f);
    }

    protected override void Animate(float dt)
    {
        if (shimmer > 0f || flash.enabled)
        {
            shimmer = Mathf.Max(0f, shimmer - dt / 0.9f);
            float k = shimmer * (0.6f + 0.4f * Mathf.Sin(Time.time * 40f));
            flash.enabled = shimmer > 0f;
            flash.intensity = 3f * k;
            block.SetColor("_Color", Color.Lerp(Color.white, new Color(2.2f, 1.9f, 1.3f), k));
            foreach (var r in bodies) if (r != null) r.SetPropertyBlock(block);
        }
        if (animator == null) return;
        string wanted = visualSpeed < 0.3f ? "Idle" : Current == State.Search ? "Run" : "Walk";
        PlayAnim(wanted);
        animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 1.4f, 0.6f, 2f) : wanted == "Run" ? Mathf.Clamp(visualSpeed / 4f, 0.8f, 2f) : 1f;
    }
}
