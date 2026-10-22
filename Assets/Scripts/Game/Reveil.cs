using System.Collections.Generic;
using UnityEngine;

// Le Réveil (entité ID 10, type Monticule de terre, V0.4.7) : il ne sort que d'un monticule creusé (5 % de chances).
// Petit réveil de la taille du Pion, un visage torturé par la souffrance gravé sur le cadran. Sorti de terre, il bondit
// sur place et son mécanisme se déclenche : les cymbales de sa tête tintent par saccades, la sonnerie retentit. Le joueur
// le plus proche devient sa proie : il court vers lui (x1,05 de la marche d'un joueur) en gesticulant, saute une ou deux
// fois, et le poursuit même dans le Hub. Au bout de 5 secondes, il explose : dans un rayon de 2 m, les joueurs subissent
// 50 de dégâts et sont repoussés, et les objets (posés ou à ramasser) sont détruits, sauf dans le Hub.
public class Reveil : Entity
{
    public enum State : byte { Wake = 0, Chase = 1, Explode = 2 }

    const float Height = 1.15f;
    const float HopDuration = 0.4f, HopHeight = 0.45f;

    public override EntityKind Kind => EntityKind.Reveil;
    public override string DisplayName => "Le Réveil";
    public override float ChestHeight => 0.6f;
    public State Current { get => (State)state; private set => state = (byte)value; }

    float stateTimer;
    readonly List<float> jumps = new List<float>();

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        stateTimer += dt;
        switch (Current)
        {
            case State.Wake:
                Speed = 0f;
                if (stateTimer < GameRules.ReveilWake) break;
                Current = State.Chase;
                stateTimer = 0f;
                TargetId = Nearest(players)?.PlayerId ?? "";
                // Une ou deux fois pendant la poursuite, il saute.
                jumps.Clear();
                int count = 1 + rnd.Next(2);
                for (int k = 0; k < count; k++) jumps.Add(Range(0.6f, GameRules.ReveilChase - 0.6f));
                break;

            case State.Chase:
            {
                var target = Find(players, TargetId);
                if (target == null || !target.Alive) target = Nearest(players);
                TargetId = target != null ? target.PlayerId : "";
                Speed = GameRules.BaseSpeed * GameRules.ReveilSpeedMultiplier;
                if (target != null) Chase(target, dt);
                for (int k = jumps.Count - 1; k >= 0; k--)
                    if (stateTimer >= jumps[k]) { jumps.RemoveAt(k); Pulse++; }
                if (stateTimer < GameRules.ReveilChase) break;
                Current = State.Explode;
                stateTimer = 0f;
                Speed = 0f;
                Host?.Explode(this, transform.position + Vector3.up * 0.5f, GameRules.ReveilBlastRadius, GameRules.ReveilDamage, GameRules.ReveilKnockback);
                break;
            }

            case State.Explode:
                // Le temps que tous les joueurs reçoivent l'explosion, puis il disparaît.
                Speed = 0f;
                if (stateTimer > 0.5f) Expired = true;
                break;
        }
    }

    // Le joueur vivant le plus proche, où qu'il soit (Hub compris).
    PlayerPresence Nearest(IReadOnlyList<PlayerPresence> players)
    {
        PlayerPresence best = null;
        float bestDist = float.MaxValue;
        foreach (var p in players)
        {
            if (p == null || !p.Alive || !p.isActiveAndEnabled) continue;
            float d = (p.Position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    // Sur la grille du labyrinthe ; dans le Hub (hors grille) ou tout près, droit sur le joueur.
    void Chase(PlayerPresence target, float dt)
    {
        Vector3 feet = Feet(target);
        if (WorldBuilder.IsInHub(feet) || WorldBuilder.IsInHub(transform.position) || nav.NodeAt(feet) < 0)
        {
            ClearPath();
            MoveTowards(feet, dt);
            return;
        }
        Pursue(target, dt);
    }

    // ================= Modèle =================

    // Repli en primitives : boîtier rond de laiton, cadran pâle au visage torturé (yeux creusés, bouche hurlante),
    // deux cymbales et leur marteau sur le dessus, petits pieds et bras qui gesticulent.
    Transform body, armL, armR, hammer;
    AudioSource alarm;
    float hopShownAt = -10f, shake;

    protected override void BuildVisual()
    {
        // Modèle Meshy (Resources/Art/Reveil, statique : le code le fait bondir et trembler), sinon des primitives.
        var model = new GameObject("Modele").transform;
        model.SetParent(transform, false);
        if (GameArt.SpawnModel("Reveil", model, Height) == null)
        {
            Destroy(model.gameObject);
            model.name = "Ancien";
            BuildFallback();
            model = transform.Find("Modele");
        }
        body = model;
    }

    void BuildFallback()
    {
        var model = new GameObject("Modele").transform;
        model.SetParent(transform, false);
        Material brass = GameArt.Lit("ReveilLaiton", new Color(0.62f, 0.45f, 0.18f), GameArt.Noise("rust", 64, 0.55f, 0.35f, 23), 0.7f);
        Material dial = GameArt.Lit("ReveilCadran", new Color(0.82f, 0.78f, 0.66f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.2f);
        Material carved = GameArt.Lit("ReveilGravure", new Color(0.05f, 0.03f, 0.02f), null, 0.1f);
        Material metal = GameArt.Metal;

        Vector3 center = new Vector3(0f, 0.62f, 0f);
        var shell = GameArt.Prim(PrimitiveType.Cylinder, model, center, new Vector3(0.78f, 0.17f, 0.78f), brass);
        shell.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var face = GameArt.Prim(PrimitiveType.Cylinder, model, center + Vector3.forward * 0.17f, new Vector3(0.66f, 0.01f, 0.66f), dial);
        face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // Visage gravé : orbites creuses inclinées (sourcils de douleur), bouche ouverte qui hurle, aiguilles tordues.
        foreach (float side in new[] { -1f, 1f })
        {
            var eye = GameArt.Prim(PrimitiveType.Sphere, model, center + new Vector3(side * 0.12f, 0.09f, 0.18f), new Vector3(0.1f, 0.07f, 0.02f), carved);
            eye.transform.localRotation = Quaternion.Euler(0f, 0f, side * 25f);
            var brow = GameArt.Prim(PrimitiveType.Cube, model, center + new Vector3(side * 0.12f, 0.17f, 0.18f), new Vector3(0.13f, 0.02f, 0.01f), carved);
            brow.transform.localRotation = Quaternion.Euler(0f, 0f, side * -28f);
        }
        GameArt.Prim(PrimitiveType.Sphere, model, center + new Vector3(0f, -0.12f, 0.18f), new Vector3(0.12f, 0.16f, 0.02f), carved);
        var hand = GameArt.Prim(PrimitiveType.Cube, model, center + new Vector3(0.05f, 0.02f, 0.185f), new Vector3(0.015f, 0.2f, 0.005f), carved);
        hand.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
        // Cymbales et marteau.
        foreach (float side in new[] { -1f, 1f })
        {
            var cup = GameArt.Prim(PrimitiveType.Sphere, model, center + new Vector3(side * 0.22f, 0.4f, -0.02f), new Vector3(0.28f, 0.16f, 0.28f), brass);
            cup.transform.localRotation = Quaternion.Euler(0f, 0f, side * -25f);
            GameArt.Prim(PrimitiveType.Cylinder, model, center + new Vector3(side * 0.16f, 0.33f, -0.02f), new Vector3(0.03f, 0.06f, 0.03f), metal);
        }
        hammer = new GameObject("Marteau").transform;
        hammer.SetParent(model, false);
        hammer.localPosition = center + new Vector3(0f, 0.3f, -0.02f);
        GameArt.Prim(PrimitiveType.Cylinder, hammer, new Vector3(0f, 0.1f, 0f), new Vector3(0.025f, 0.1f, 0.025f), metal);
        GameArt.Prim(PrimitiveType.Sphere, hammer, new Vector3(0f, 0.2f, 0f), Vector3.one * 0.07f, metal);
        // Pieds et bras.
        foreach (float side in new[] { -1f, 1f })
        {
            var foot = GameArt.Prim(PrimitiveType.Cylinder, model, new Vector3(side * 0.2f, 0.12f, 0f), new Vector3(0.05f, 0.12f, 0.05f), metal);
            foot.transform.localRotation = Quaternion.Euler(0f, 0f, side * 20f);
            GameArt.Prim(PrimitiveType.Sphere, model, new Vector3(side * 0.24f, 0.03f, 0.03f), new Vector3(0.12f, 0.06f, 0.16f), metal);
            var arm = new GameObject(side < 0f ? "BrasG" : "BrasD").transform;
            arm.SetParent(model, false);
            arm.localPosition = center + new Vector3(side * 0.38f, 0.02f, 0f);
            GameArt.Prim(PrimitiveType.Cylinder, arm, new Vector3(0f, -0.15f, 0f), new Vector3(0.035f, 0.15f, 0.035f), metal);
            GameArt.Prim(PrimitiveType.Sphere, arm, new Vector3(0f, -0.31f, 0f), Vector3.one * 0.07f, brass);
            if (side < 0f) armL = arm; else armR = arm;
        }
    }

    // ================= Affichage =================

    protected override void OnStateChanged(byte previous, byte next)
    {
        if ((State)next == State.Explode) Blast();
    }

    // Un saut pendant la poursuite.
    protected override void OnPulse() => hopShownAt = Time.time;

    protected override void Animate(float dt)
    {
        if (Current == State.Explode) return;
        // La sonnerie se déclenche dès qu'il sort de terre (après le premier bond), et ne s'arrête qu'avec l'explosion.
        if (alarm == null)
        {
            hopShownAt = Time.time;
            alarm = GameAudio.Loop3D("reveil_alarm", transform, Vector3.up * 0.9f, 0.8f, 30f);
            if (alarm != null) alarm.Play();
        }
        if (body == null) return;
        // Bonds (sortie du monticule, sauts) et tremblement saccadé de la sonnerie.
        float h = Time.time - hopShownAt;
        float hop = h < HopDuration ? Mathf.Sin(Mathf.PI * h / HopDuration) * HopHeight : 0f;
        shake += dt * 38f;
        float rattle = Mathf.Sin(shake) * (Mathf.Repeat(Time.time * 3f, 1f) < 0.7f ? 1f : 0.2f);
        body.localPosition = Vector3.up * hop + Vector3.right * rattle * 0.015f;
        body.localRotation = Quaternion.Euler(0f, 0f, rattle * 5f);
        if (hammer != null) hammer.localRotation = Quaternion.Euler(0f, 0f, rattle * 35f);
        // Bras qui gesticulent en courant.
        float wave = Mathf.Sin(Time.time * 16f) * (Current == State.Chase ? 70f : 25f);
        if (armL != null) armL.localRotation = Quaternion.Euler(wave, 0f, -40f - wave * 0.3f);
        if (armR != null) armR.localRotation = Quaternion.Euler(-wave, 0f, 40f - wave * 0.3f);
        if (animator != null)
        {
            PlayAnim(visualSpeed < 0.3f ? "Idle" : "Run");
            animator.speed = 1.6f;
        }
    }

    // Explosion, vue et entendue par tous : boule de feu, éclats de laiton, fumée ; secousse de la caméra à proximité.
    void Blast()
    {
        if (alarm != null) { alarm.Stop(); Destroy(alarm.gameObject); }
        Vector3 at = transform.position + Vector3.up * 0.5f;
        GameAudio.Play3D("reveil_boom", at, 1f, 40f, 0.05f);
        Fx.Burst(at, GameArt.Emissive("Flammeche", new Color(1f, 0.5f, 0.1f), new Color(1.5f, 0.6f, 0.1f)), 26, 6f, 0.12f, 0.7f);
        Fx.Burst(at, GameArt.Lit("EclatsLaiton", new Color(0.62f, 0.45f, 0.18f), null, 0.6f), 18, 5f, 0.08f, 1.2f);
        Fx.Burst(at, Fx.SmokeMaterial, 14, 2.5f, 0.35f, 1.3f, -0.1f);
        Camera cam = Camera.main;
        if (cam != null)
        {
            float d = Vector3.Distance(cam.transform.position, at);
            if (d < 10f) OrbitCamera.Shake(Mathf.Lerp(0.9f, 0.15f, d / 10f));
        }
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
    }
}
