using System.Collections.Generic;
using UnityEngine;

// Le Fou (entité ID 7, type Cauchemar, V0.4.3) : grande entité bipède et souriante à tête de soleil, de la taille de
// La Dame. "En recherche", il arpente le terrain à la vitesse de marche d'un joueur, d'une démarche cartoon, et se met
// à courir sans prévenir (x1,5 pendant 2 à 5 s, grelots audibles à 30 m ; 30 % de chances de faire demi-tour), puis
// remarche 4 à 10 s. Un joueur dont la caméra le voit est fixé : le Fou se tourne vers lui, crie et tremble. Regardé
// 2 secondes d'affilée (ou de nouveau dans les 3 secondes après un regard détourné), il attaque : cri, échange de
// position avec le personnage et 20 de dégâts (puis 2 s sans pouvoir échanger). Il passe alors "en chasse" 5 secondes,
// en marchant vers le joueur, les yeux rouges ; un regard de plus d'une seconde relance l'attaque et la chasse.
// À moins de 0,8 m, il immobilise le joueur et le tue. Une fois sur 7 environ (15 %), il danse 3 secondes avant
// d'attaquer : si le joueur ne le regarde plus à la fin, l'attaque est annulée.
public class Fou : Entity
{
    public enum State : byte { Walk = 0, Run = 1, Alert = 2, Dance = 3, Chase = 4, Grab = 5 }

    const float Height = 2.66f;
    const float EyeHeight = 2.3f;
    const float GrabHitTime = 0.45f, GrabDuration = 1.1f;

    public override EntityKind Kind => EntityKind.Fou;
    public override string DisplayName => "Le Fou";
    public override float ChestHeight => 1.7f;
    public override bool Nightmare => true;
    public override Vector3 EyesLocal => eyes != null ? transform.InverseTransformPoint(eyes.position) : new Vector3(0f, EyeHeight, 0.2f);
    public State Current { get => (State)state; private set => state = (byte)value; }

    // Joueurs dont la caméra voit le Fou (signalé par chacun à l'hôte).
    readonly HashSet<string> lookers = new HashSet<string>();
    float stateTimer, runTimer, runDuration, lookTime, awayTime, chaseTimer, swapCooldown;
    bool lookedAway, grabHit;

    public void SetLooking(string playerId, bool looking)
    {
        if (looking) lookers.Add(playerId);
        else lookers.Remove(playerId);
    }

    bool Looks(PlayerPresence p) => Valid(p) && lookers.Contains(p.PlayerId);

    protected override void Init() => runTimer = Range(GameRules.FouWalkMin, GameRules.FouWalkMax);

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        stateTimer += dt;
        swapCooldown -= dt;
        var target = Find(players, TargetId);
        switch (Current)
        {
            case State.Walk:
            case State.Run:
                // Uniquement "en recherche" : le premier joueur qui le regarde est fixé.
                foreach (var p in players)
                    if (Looks(p)) { BeginAlert(p); return; }
                if (Current == State.Walk)
                {
                    Speed = GameRules.BaseSpeed;
                    runTimer -= dt;
                    if (runTimer <= 0f)
                    {
                        Current = State.Run;
                        stateTimer = 0f;
                        runDuration = Range(GameRules.FouRunMin, GameRules.FouRunMax);
                        // 30 % : il fait demi-tour et court dans la direction inverse.
                        if (rnd.NextDouble() < GameRules.FouTurnBackChance) PlanPath(NodeAround(-transform.forward), 0f);
                    }
                }
                else
                {
                    Speed = GameRules.BaseSpeed * GameRules.FouRunMultiplier;
                    if (stateTimer >= runDuration) Search();
                }
                Wander(dt, 15f, 45f);
                break;

            case State.Alert:
                Speed = 0f;
                if (!Valid(target)) { Search(); break; }
                Face(target.Position, dt);
                if (Looks(target))
                {
                    lookTime += dt;
                    // Regardé de nouveau dans les 3 secondes après avoir détourné les yeux, ou 2 secondes d'affilée.
                    if (lookedAway || lookTime >= GameRules.FouStareTime) LaunchAttack(target);
                }
                else
                {
                    lookedAway = true;
                    lookTime = 0f;
                    awayTime += dt;
                    if (awayTime >= GameRules.FouLookAgainWindow) Search();
                }
                break;

            case State.Dance:
                Speed = 0f;
                if (!Valid(target)) { Search(); break; }
                if (stateTimer < GameRules.FouDanceDuration) break;
                // La danse finie, il attaque si le joueur le fixe toujours ; sinon il rôde à nouveau.
                if (Looks(target) && swapCooldown <= 0f) Attack(target);
                else Search();
                break;

            case State.Chase:
                if (!Valid(target)) { Search(); break; }
                chaseTimer -= dt;
                if (chaseTimer <= 0f) { Search(); break; }
                Speed = GameRules.BaseSpeed;
                Pursue(target, dt);
                lookTime = Looks(target) ? lookTime + dt : 0f;
                if (InReach(target, GameRules.FouKillDistance)) { BeginGrab(); break; }
                if (lookTime > GameRules.FouChaseStareTime && swapCooldown <= 0f) LaunchAttack(target);
                break;

            case State.Grab:
                Speed = 0f;
                if (target != null) Face(target.Position, dt);
                if (!grabHit && stateTimer >= GrabHitTime)
                {
                    grabHit = true;
                    // Le joueur perd tous ses PV.
                    if (target != null && target.Alive) Host?.Hit(this, TargetId, GameRules.MaxHp, 0f);
                }
                if (stateTimer >= GrabDuration) Search();
                break;
        }
    }

    void BeginAlert(PlayerPresence p)
    {
        Current = State.Alert;
        stateTimer = lookTime = awayTime = 0f;
        lookedAway = false;
        TargetId = p.PlayerId;
        ClearPath();
    }

    // 15 % : une danse de 3 secondes avant l'attaque.
    void LaunchAttack(PlayerPresence target)
    {
        // Il ne peut pas échanger de position pendant 2 secondes après un échange : il attend.
        if (swapCooldown > 0f) return;
        if (rnd.NextDouble() < GameRules.FouDanceChance)
        {
            Current = State.Dance;
            stateTimer = 0f;
            ClearPath();
        }
        else Attack(target);
    }

    // Cri d'attaque, échange de position avec le personnage et dégâts ; puis la chasse (ou sa relance).
    void Attack(PlayerPresence target)
    {
        Vector3 mine = transform.position;
        Vector3 theirs = Feet(target);
        int node = nav.NodeAt(theirs);
        Vector3 landing = node >= 0 && Vector3.Distance(nav.Position(node), theirs) < 1.5f ? nav.Position(node) : theirs;
        Host?.Swap(this, TargetId, mine);
        Host?.Hit(this, TargetId, GameRules.FouDamage, 0f);
        transform.position = landing;
        Vector3 back = Horizontal(mine - landing);
        if (back.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(back);
        Pulse++;
        swapCooldown = GameRules.FouSwapCooldown;
        Current = State.Chase;
        stateTimer = lookTime = 0f;
        chaseTimer = GameRules.FouChaseDuration;
        ClearPath();
        repathTimer = 0f;
    }

    void BeginGrab()
    {
        Current = State.Grab;
        stateTimer = 0f;
        grabHit = false;
        ClearPath();
        Host?.Immobilize(this, TargetId, GrabDuration);
    }

    // Retour à l'état "en recherche", en marchant.
    void Search()
    {
        Current = State.Walk;
        stateTimer = 0f;
        TargetId = "";
        runTimer = Range(GameRules.FouWalkMin, GameRules.FouWalkMax);
        ClearPath();
    }

    // Un point de la grille de sa zone, à 15-45 m, dans la direction voulue.
    int NodeAround(Vector3 direction)
    {
        int here = nav.NodeAt(transform.position);
        int zone = here >= 0 ? nav.ZoneOfNode(here) : 0;
        int best = -1;
        for (int k = 0; k < 12; k++)
        {
            int node = nav.RandomNode(zone, rnd, transform.position, 15f, 45f);
            if (node < 0) continue;
            best = node;
            if (Vector3.Dot(Horizontal(nav.Position(node) - transform.position), direction) > 0f) break;
        }
        return best;
    }

    // ================= Modèle =================

    Transform body, head, armL, armR, legL, legR;
    Renderer[] eyeRenderers;
    Material eyeDark, eyeRed;
    Light eyeGlow;
    Transform modelRoot, eyes;
    AudioSource bells;
    // Modèle Meshy : position des yeux par rapport à l'os de la tête, vitesses de marche et de course des clips.
    const float ModelEyeUp = 0.2f, ModelEyeForward = 0.28f;
    const float ModelWalkSpeed = 1.6f, ModelRunSpeed = 5f;
    float walkPhase, danceTime, alertTime;
    bool eyesRed;

    protected override void BuildVisual()
    {
        eyeRed = GameArt.Emissive("FouEyeRed", new Color(1f, 0.1f, 0.05f), new Color(3f, 0.15f, 0.05f));
        if (LoadCreature("FouArt"))
        {
            BuildModelEyes();
            return;
        }
        Material coat = GameArt.Lit("FouCoat", new Color(0.13f, 0.13f, 0.15f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.1f);
        Material collar = GameArt.Lit("FouCollar", new Color(0.2f, 0.2f, 0.22f), null, 0.15f);
        // Visage pâle, un peu lumineux : on le distingue dans la pénombre.
        Material skin = GameArt.Emissive("FouSkin", new Color(0.93f, 0.89f, 0.8f), new Color(0.22f, 0.2f, 0.17f));
        Material mouth = GameArt.Lit("FouMouth", new Color(0.18f, 0.03f, 0.02f), null, 0.3f);
        Material teeth = GameArt.Lit("FouTeeth", new Color(0.97f, 0.95f, 0.85f), null, 0.6f);
        Material sun = GameArt.Emissive("FouSun", new Color(1f, 0.75f, 0.15f), new Color(0.9f, 0.55f, 0.05f));
        eyeDark = GameArt.Lit("FouEye", new Color(0.04f, 0.03f, 0.03f), null, 0.8f);

        float s = Height / 2.66f;
        body = new GameObject("Corps").transform;
        body.SetParent(transform, false);

        // Jambes : pivots aux hanches.
        legL = Pivot("JambeG", body, new Vector3(-0.17f, 1.05f, 0f) * s);
        legR = Pivot("JambeD", body, new Vector3(0.17f, 1.05f, 0f) * s);
        foreach (var leg in new[] { legL, legR })
        {
            GameArt.Prim(PrimitiveType.Cube, leg, new Vector3(0f, -0.5f, 0f) * s, new Vector3(0.17f, 1f, 0.2f) * s, coat);
            GameArt.Prim(PrimitiveType.Cube, leg, new Vector3(0f, -1.0f, 0.08f) * s, new Vector3(0.2f, 0.1f, 0.36f) * s, collar);
        }
        // Long manteau étroit et haut col.
        GameArt.Prim(PrimitiveType.Cube, body, new Vector3(0f, 1.5f, 0f) * s, new Vector3(0.56f, 0.95f, 0.32f) * s, coat);
        GameArt.Prim(PrimitiveType.Cube, body, new Vector3(0f, 1.08f, 0f) * s, new Vector3(0.6f, 0.22f, 0.36f) * s, coat);
        GameArt.Prim(PrimitiveType.Cylinder, body, new Vector3(0f, 2.02f, 0f) * s, new Vector3(0.36f, 0.1f, 0.34f) * s, collar);
        // Bras longs et fins : pivots aux épaules.
        armL = Pivot("BrasG", body, new Vector3(-0.34f, 1.92f, 0f) * s);
        armR = Pivot("BrasD", body, new Vector3(0.34f, 1.92f, 0f) * s);
        foreach (var arm in new[] { armL, armR })
        {
            GameArt.Prim(PrimitiveType.Cube, arm, new Vector3(0f, -0.42f, 0f) * s, new Vector3(0.13f, 0.86f, 0.15f) * s, coat);
            GameArt.Prim(PrimitiveType.Sphere, arm, new Vector3(0f, -0.9f, 0f) * s, new Vector3(0.13f, 0.16f, 0.1f) * s, skin);
        }

        // Tête de soleil : visage pâle, sourire immense, rayons tout autour.
        head = Pivot("Tete", body, new Vector3(0f, 2.12f, 0f) * s);
        GameArt.Prim(PrimitiveType.Sphere, head, new Vector3(0f, 0.2f, 0f) * s, new Vector3(0.42f, 0.46f, 0.38f) * s, skin);
        GameArt.Prim(PrimitiveType.Sphere, head, new Vector3(0f, 0.11f, 0.15f) * s, new Vector3(0.3f, 0.1f, 0.12f) * s, mouth);
        GameArt.Prim(PrimitiveType.Cube, head, new Vector3(0f, 0.135f, 0.19f) * s, new Vector3(0.24f, 0.03f, 0.03f) * s, teeth);
        var eyeL = GameArt.Prim(PrimitiveType.Sphere, head, new Vector3(-0.085f, 0.27f, 0.165f) * s, new Vector3(0.07f, 0.04f, 0.04f) * s, eyeDark);
        var eyeR = GameArt.Prim(PrimitiveType.Sphere, head, new Vector3(0.085f, 0.27f, 0.165f) * s, new Vector3(0.07f, 0.04f, 0.04f) * s, eyeDark);
        eyeRenderers = new[] { eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() };
        const int rays = 16;
        for (int k = 0; k < rays; k++)
        {
            float a = k * 360f / rays;
            float length = (k % 2 == 0 ? 0.36f : 0.24f) * s;
            var ray = new GameObject("Rayon").transform;
            ray.SetParent(head, false);
            ray.localPosition = new Vector3(0f, 0.2f, -0.04f) * s;
            ray.localRotation = Quaternion.Euler(0f, 0f, a) * Quaternion.Euler(-12f, 0f, 0f);
            // Rayon effilé, légèrement ondulé (deux segments).
            var seg = GameArt.Prim(PrimitiveType.Cube, ray, new Vector3(0f, 0.21f * s + length * 0.3f, 0f), new Vector3(0.11f * s, length * 0.6f, 0.035f * s), sun);
            seg.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            var tip = GameArt.Prim(PrimitiveType.Cube, ray, new Vector3(0.012f * s, 0.21f * s + length * 0.78f, 0f), new Vector3(0.065f * s, length * 0.45f, 0.03f * s), sun);
            tip.transform.localRotation = Quaternion.Euler(0f, 0f, -10f);
        }

        // Lueur rouge des yeux, en chasse.
        eyeGlow = new GameObject("Yeux").AddComponent<Light>();
        eyeGlow.transform.SetParent(head, false);
        eyeGlow.transform.localPosition = new Vector3(0f, 0.27f, 0.32f) * s;
        eyeGlow.type = LightType.Point;
        eyeGlow.color = new Color(1f, 0.08f, 0.04f);
        eyeGlow.range = 3f;
        eyeGlow.intensity = 2f;
        eyeGlow.shadows = LightShadows.None;
        eyeGlow.enabled = false;
    }

    // Modèle Meshy : deux yeux rouges lumineux, cachés hors de la chasse, suivent l'os de la tête.
    void BuildModelEyes()
    {
        modelRoot = animator.transform;
        Transform headBone = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        Transform parent = headBone != null ? headBone : transform;
        eyes = new GameObject("YeuxRouges").transform;
        eyes.SetParent(parent, false);
        // Échelle et orientation monde fixes, quelle que soit celle de l'os.
        eyes.localScale = Vector3.one / Mathf.Max(0.0001f, parent.lossyScale.x);
        eyes.SetPositionAndRotation(headBone != null ? headBone.position + transform.up * ModelEyeUp + transform.forward * ModelEyeForward
                                                     : transform.position + Vector3.up * EyeHeight + transform.forward * 0.2f, transform.rotation);
        var l = GameArt.Prim(PrimitiveType.Sphere, eyes, new Vector3(-0.086f, 0f, 0f), new Vector3(0.05f, 0.026f, 0.02f), eyeRed);
        var r = GameArt.Prim(PrimitiveType.Sphere, eyes, new Vector3(0.046f, 0f, 0f), new Vector3(0.05f, 0.026f, 0.02f), eyeRed);
        eyeRenderers = new[] { l.GetComponent<Renderer>(), r.GetComponent<Renderer>() };
        foreach (var e in eyeRenderers) e.enabled = false;
        eyeGlow = new GameObject("Yeux").AddComponent<Light>();
        eyeGlow.transform.SetParent(eyes, false);
        eyeGlow.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        eyeGlow.type = LightType.Point;
        eyeGlow.color = new Color(1f, 0.08f, 0.04f);
        eyeGlow.range = 3f;
        eyeGlow.intensity = 2f;
        eyeGlow.shadows = LightShadows.None;
        eyeGlow.enabled = false;
    }

    static Transform Pivot(string name, Transform parent, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    // ================= Affichage =================

    protected override void OnStateChanged(byte previous, byte next)
    {
        Vector3 mouth = transform.position + Vector3.up * EyeHeight;
        switch ((State)next)
        {
            case State.Alert:
                alertTime = 0f;
                GameAudio.Play3D("fou_alert", mouth, 1f, 35f, 0.06f);
                break;
            case State.Dance:
                danceTime = 0f;
                GameAudio.Play3D("fou_dance", mouth, 0.9f, 30f, 0f);
                break;
            case State.Grab:
                GameAudio.Play3D("fou_attack", mouth, 1f, 30f, 0.1f);
                break;
        }
    }

    // Attaque : cri (différent du cri d'alerte) au moment de l'échange de position.
    protected override void OnPulse() => GameAudio.Play3D("fou_attack", transform.position + Vector3.up * EyeHeight, 1f, 35f, 0.05f);

    protected override void Animate(float dt)
    {
        // Grelots, à moins de 30 mètres, tant qu'il court.
        bool running = Current == State.Run;
        if (running && bells == null) bells = GameAudio.Loop3D("fou_bells", transform, Vector3.up * 2.2f, 0.8f, GameRules.FouBellsRange);
        if (bells != null && running != bells.isPlaying)
        {
            if (running) bells.Play();
            else bells.Stop();
        }

        bool red = Current == State.Chase || Current == State.Grab;
        if (red != eyesRed)
        {
            eyesRed = red;
            foreach (var r in eyeRenderers)
                if (r == null) continue;
                else if (animator != null) r.enabled = red;
                else r.sharedMaterial = red ? eyeRed : eyeDark;
            eyeGlow.enabled = red;
        }

        if (animator != null)
        {
            AnimateModel(dt);
            return;
        }
        if (body == null) return;
        float speed = visualSpeed;
        walkPhase += dt * Mathf.Clamp(speed, 0f, 8f) * 1.9f;
        float swing = Mathf.Sin(walkPhase);
        float moving = Mathf.Clamp01(speed / 1.5f);
        Vector3 bodyPos = Vector3.zero;
        Quaternion bodyRot = Quaternion.identity, headRot = Quaternion.identity;
        float armSwing = 0f, legSwing = 0f, armsUp = 0f;

        switch (Current)
        {
            case State.Alert:
            {
                // Saccades malaisantes : tremblements secs de tout le corps, tête qui se tord.
                alertTime += dt;
                float t = alertTime * 26f;
                Vector3 j = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f);
                float jerk = Mathf.Floor(alertTime * 9f) % 2f == 0f ? 1f : -1f;
                bodyPos = new Vector3(j.x * 0.07f, Mathf.Abs(j.y) * 0.04f, j.z * 0.05f);
                bodyRot = Quaternion.Euler(j.z * 8f, j.x * 10f, j.y * 9f);
                headRot = Quaternion.Euler(j.y * 25f, j.x * 30f, 18f * jerk + j.z * 20f);
                armSwing = j.x * 40f;
                armsUp = 15f + j.y * 30f;
                break;
            }
            case State.Dance:
            {
                // Pas de danse cartoon : sautille, se déhanche et agite les bras au-dessus de la tête.
                danceTime += dt;
                float b = danceTime * Mathf.PI * 4f;
                bodyPos = new Vector3(Mathf.Sin(b * 0.5f) * 0.15f, Mathf.Abs(Mathf.Sin(b)) * 0.18f, 0f);
                bodyRot = Quaternion.Euler(0f, Mathf.Sin(b * 0.25f) * 50f, Mathf.Sin(b * 0.5f) * 14f);
                headRot = Quaternion.Euler(0f, 0f, -Mathf.Sin(b * 0.5f) * 25f);
                armsUp = 150f;
                armSwing = Mathf.Sin(b) * 35f;
                legSwing = Mathf.Sin(b) * 25f;
                break;
            }
            default:
            {
                // Démarche cartoon : rebonds exagérés, balancement de tout le corps, grands moulinets de bras.
                bool fast = Current == State.Run || speed > GameRules.BaseSpeed * 1.2f;
                float bounce = Mathf.Abs(Mathf.Sin(walkPhase)) * (fast ? 0.16f : 0.1f) * moving;
                bodyPos = new Vector3(0f, bounce, 0f);
                bodyRot = Quaternion.Euler((fast ? 14f : 4f) * moving, swing * 8f * moving, swing * (fast ? 6f : 9f) * moving);
                headRot = Quaternion.Euler(0f, 0f, -swing * 12f * moving);
                armSwing = swing * (fast ? 70f : 50f) * moving;
                legSwing = swing * (fast ? 45f : 32f) * moving;
                if (Current == State.Grab) armsUp = 80f;
                break;
            }
        }
        body.localPosition = bodyPos;
        body.localRotation = bodyRot;
        head.localRotation = headRot;
        armL.localRotation = Quaternion.Euler(armSwing - armsUp, 0f, -6f - armsUp * 0.15f);
        armR.localRotation = Quaternion.Euler(-armSwing - armsUp, 0f, 6f + armsUp * 0.15f);
        legL.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(legSwing, 0f, 0f);
    }

    // Modèle Meshy : démarche "Funky Walk", course, danse, cri ; en alerte, des saccades secouent tout le corps.
    void AnimateModel(float dt)
    {
        bool fast = Current == State.Run || visualSpeed > GameRules.BaseSpeed * 1.2f;
        string wanted = Current == State.Alert || Current == State.Grab ? "Scream" : Current == State.Dance ? "Dance"
            : visualSpeed < 0.3f ? "Idle" : fast ? "Run" : "Walk";
        PlayAnim(wanted);
        animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / ModelWalkSpeed, 0.6f, 1.8f)
            : wanted == "Run" ? Mathf.Clamp(visualSpeed / ModelRunSpeed, 0.7f, 1.8f) : 1f;
        if (Current == State.Alert)
        {
            alertTime += dt;
            float t = alertTime * 26f;
            Vector3 j = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, Mathf.PerlinNoise(t, t) - 0.5f);
            float jerk = Mathf.Floor(alertTime * 9f) % 2f == 0f ? 1f : -1f;
            modelRoot.localPosition = new Vector3(j.x * 0.07f, Mathf.Abs(j.y) * 0.04f, j.z * 0.05f);
            modelRoot.localRotation = Quaternion.Euler(j.z * 8f, j.x * 12f, j.y * 9f + jerk * 4f);
        }
        else
        {
            modelRoot.localPosition = Vector3.zero;
            modelRoot.localRotation = Quaternion.identity;
        }
    }
}
