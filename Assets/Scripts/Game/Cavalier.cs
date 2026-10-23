using System.Collections.Generic;
using UnityEngine;

// Le Cavalier (entité ID 9, type Cauchemar, V0.4.7) : grande entité bipède et mince à tête de cheval, des sabots à la place
// des pieds, armée d'une masse d'armes ; aussi grande que La Dame, elle suit la grille de navigation.
// "En recherche", il rôde d'un pas modérément rapide (moins vite que la course) ; ses bruits de galop s'entendent à 30 m
// dès qu'il se déplace. Il s'arrête parfois pour frapper le sol de sa masse (1,5 s), puis repart (20 % : en arrière).
// Il repère un joueur à 20 m devant lui, ou à 6 m même dans son dos : cri d'alerte, et il passe "en chasse" (un seul joueur
// à la fois), les yeux rouges luisant dans l'obscurité. Tant que le joueur traqué le regarde (caméra, sans mur entre eux),
// il ne bouge plus (il ne se tourne même pas vers lui) ; dès qu'il ne le regarde plus ou se cache derrière un mur, cri de
// terreur, et il court vers lui (3,5 fois la marche d'un joueur). À moins de 0,8 m (même immobile), il immobilise le
// joueur et le frappe de sa masse, puis repart en recherche. Un compteur de distance (remis à 0 au début de la chasse)
// compte les mètres parcourus : à 50 m, ou si le joueur rentre dans le Hub, il abandonne et ne peut plus traquer ce
// joueur pendant 2 secondes.
public class Cavalier : Entity
{
    public enum State : byte { Walk = 0, Stomp = 1, Chase = 2, Strike = 3 }

    const float EyeHeight = 2.4f * GameRules.BigEntityScale, FieldOfView = 120f;
    const float StrikeHitTime = 0.55f, StrikeDuration = 1.3f, StompHitTime = 0.7f;
    const float TerrorInterval = 1.5f;

    public override EntityKind Kind => EntityKind.Cavalier;
    public override string DisplayName => "Le Cavalier";
    public override float ChestHeight => 1.55f * GameRules.BigEntityScale;
    protected override float ModelScale => GameRules.BigEntityScale;
    public override bool Nightmare => true;
    public override Vector3 EyesLocal => new Vector3(0f, EyeHeight, 0.45f);
    public State Current { get => (State)state; private set => state = (byte)value; }
    // Le joueur traqué le regarde (signalé par son client).
    public bool Watched;

    float stateTimer, stompTimer, chased, lastTerror = -10f;
    bool running, struck;
    Vector3 lastPos;
    readonly Dictionary<string, float> ignoreUntil = new Dictionary<string, float>();

    protected override void Init()
    {
        stompTimer = Range(GameRules.CavalierStompMin, GameRules.CavalierStompMax);
        lastPos = transform.position;
    }

    // ================= Simulation (hôte) =================

    public override void Simulate(float dt, IReadOnlyList<PlayerPresence> players)
    {
        stateTimer += dt;
        var target = Find(players, TargetId);
        switch (Current)
        {
            case State.Walk:
            case State.Stomp:
            {
                foreach (var p in players)
                {
                    if (!Valid(p) || ignoreUntil.TryGetValue(p.PlayerId, out float until) && Time.time < until) continue;
                    if (Sees(p, EyeHeight, GameRules.CavalierSightRange, FieldOfView) || Senses(p)) { BeginChase(p); return; }
                }
                if (Current == State.Stomp)
                {
                    // Il frappe le sol de sa masse, puis repart (20 % : en arrière).
                    Speed = 0f;
                    if (stateTimer < GameRules.CavalierStompDuration) break;
                    Current = State.Walk;
                    stateTimer = 0f;
                    stompTimer = Range(GameRules.CavalierStompMin, GameRules.CavalierStompMax);
                    if (rnd.NextDouble() < GameRules.CavalierTurnBackChance) PlanPath(NodeAround(-transform.forward), 0f);
                    else ClearPath();
                    break;
                }
                Speed = GameRules.BaseSpeed * GameRules.CavalierWalkMultiplier;
                stompTimer -= dt;
                if (stompTimer <= 0f)
                {
                    Current = State.Stomp;
                    stateTimer = 0f;
                    Speed = 0f;
                    break;
                }
                Wander(dt, 10f, 40f);
                break;
            }

            case State.Chase:
            {
                // Le joueur traqué rentre dans le Hub (ou meurt) : il abandonne.
                if (!Valid(target)) { StopChase(); break; }
                if (InReach(target, GameRules.CavalierHitDistance)) { BeginStrike(); break; }
                bool watched = Watched && ClearLine(transform.position + Vector3.up * EyeHeight, target.Position + Vector3.up * 0.4f);
                if (watched)
                {
                    // Regardé : figé sur place, sans même se tourner vers lui.
                    Speed = 0f;
                    running = false;
                    break;
                }
                if (!running)
                {
                    running = true;
                    repathTimer = 0f;
                    // Cri de terreur quand il s'élance (pas plus d'un toutes les 1,5 s).
                    if (Time.time - lastTerror > TerrorInterval) { lastTerror = Time.time; Pulse++; }
                }
                Speed = GameRules.BaseSpeed * GameRules.CavalierRunMultiplier;
                Pursue(target, dt);
                chased += Horizontal(transform.position - lastPos).magnitude;
                if (chased >= GameRules.CavalierChaseDistance) StopChase();
                break;
            }

            case State.Strike:
                Speed = 0f;
                if (target != null) Face(target.Position, dt);
                if (!struck && stateTimer >= StrikeHitTime)
                {
                    struck = true;
                    if (target != null && target.Alive) Host?.Hit(this, TargetId, GameRules.CavalierDamage, 0f);
                }
                if (stateTimer >= StrikeDuration) StopChase(false);
                break;
        }
        lastPos = transform.position;
    }

    void BeginChase(PlayerPresence p)
    {
        Current = State.Chase;
        stateTimer = 0f;
        TargetId = p.PlayerId;
        chased = 0f;
        running = false;
        Watched = false;
        ClearPath();
        repathTimer = 0f;
    }

    void BeginStrike()
    {
        Current = State.Strike;
        stateTimer = 0f;
        struck = false;
        ClearPath();
        Host?.Immobilize(this, TargetId, StrikeDuration);
    }

    // Retour "en recherche" ; après un abandon, 2 secondes sans pouvoir traquer ce joueur.
    void StopChase(bool ignore = true)
    {
        if (ignore && !string.IsNullOrEmpty(TargetId)) ignoreUntil[TargetId] = Time.time + GameRules.CavalierIgnore;
        Current = State.Walk;
        stateTimer = 0f;
        TargetId = "";
        Watched = false;
        running = false;
        stompTimer = Range(GameRules.CavalierStompMin, GameRules.CavalierStompMax);
        ClearPath();
    }

    // À moins de 6 m (3 m en marche discrète), devant comme dans son dos, sans mur entre eux.
    bool Senses(PlayerPresence p)
    {
        Vector3 d = p.Position - transform.position;
        float radius = GameRules.CavalierSenseRadius * (p.Sneaking ? 0.5f : 1f);
        if (Horizontal(d).magnitude > radius || Mathf.Abs(d.y - 0.9f) > 2.5f) return false;
        return ClearLine(transform.position + Vector3.up * 1.5f, p.Position + Vector3.up * 0.4f);
    }

    // ================= Modèle =================

    // Repli en primitives (sous "Modele", agrandi de 25 %) : jambes à sabots, buste mince, long cou, tête de cheval,
    // masse d'armes dans la main droite. Le modèle Meshy, s'il existe, se charge depuis Resources/CavalierArt.
    Transform legL, legR, armR, armL, head;
    Renderer[] eyes;
    Light eyeLight;
    float stepTimer, walkPhase, strikeShownAt = -10f, stompShownAt = -10f;
    bool stompPlayed, strikePlayed;

    protected override void BuildVisual()
    {
        if (!LoadCreature("CavalierArt")) BuildFallback();
        else if (animator.isHuman) AttachToBones();
        // Yeux rouges qui luisent en chasse (avec une petite lumière rouge).
        Material red = GameArt.Emissive("CavalierEyes", new Color(0.6f, 0.02f, 0.02f), new Color(2.4f, 0.1f, 0.05f));
        Transform eyeParent = head != null ? head : transform;
        Vector3 eyeBase = head != null ? Vector3.zero : new Vector3(0f, EyeHeight, 0.3f);
        eyes = new Renderer[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            var eye = GameArt.Prim(PrimitiveType.Sphere, eyeParent, eyeBase + new Vector3(side * 0.14f, 0.08f, 0.12f), Vector3.one * 0.07f, red);
            eyes[i] = eye.GetComponent<Renderer>();
            eyes[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            eyes[i].enabled = false;
        }
        eyeLight = new GameObject("LueurYeux").AddComponent<Light>();
        eyeLight.transform.SetParent(eyeParent, false);
        eyeLight.transform.localPosition = eyeBase + new Vector3(0f, 0.08f, 0.35f);
        eyeLight.type = LightType.Point;
        eyeLight.color = new Color(1f, 0.1f, 0.05f);
        eyeLight.range = 3f;
        eyeLight.intensity = 1.6f;
        eyeLight.shadows = LightShadows.None;
        eyeLight.enabled = false;
    }

    // Modèle Meshy riggé : masse d'armes (Resources/Art/MasseCavalier, debout, manche en bas) dans la main droite, le long
    // de l'avant-bras ; les yeux suivent la tête. Tailles en mètres réels malgré l'échelle des os.
    void AttachToBones()
    {
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform fore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        if (hand != null && fore != null)
        {
            var grip = new GameObject("Masse").transform;
            grip.SetParent(hand, false);
            grip.localScale = Vector3.one / Mathf.Max(0.0001f, hand.lossyScale.x);
            Vector3 along = (hand.position - fore.position).normalized;
            Vector3 tilt = Vector3.Cross(along, transform.forward).normalized;
            grip.rotation = Quaternion.AngleAxis(35f, tilt) * Quaternion.FromToRotation(Vector3.up, along);
            grip.position = hand.position + along * 0.05f;
            var mace = GameArt.SpawnModel("MasseCavalier", grip, 1.5f);
            if (mace != null) mace.transform.localPosition = Vector3.down * 0.3f;
        }
        Transform skull = animator.GetBoneTransform(HumanBodyBones.Head);
        if (skull != null)
        {
            head = new GameObject("Yeux").transform;
            head.SetParent(skull, false);
            head.localScale = Vector3.one / Mathf.Max(0.0001f, skull.lossyScale.x);
            head.rotation = transform.rotation;
            head.position = skull.position + transform.forward * 0.12f + Vector3.up * 0.06f;
        }
    }

    void BuildFallback()
    {
        var model = new GameObject("Modele").transform;
        model.SetParent(transform, false);
        Material hide = GameArt.Lit("CavalierPeau", new Color(0.16f, 0.13f, 0.11f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.1f);
        Material bone = GameArt.Lit("CavalierCrane", new Color(0.72f, 0.68f, 0.6f), null, 0.25f);
        Material hoof = GameArt.Lit("CavalierSabot", new Color(0.06f, 0.05f, 0.05f), null, 0.5f);
        Material mane = GameArt.Lit("CavalierCrin", new Color(0.03f, 0.03f, 0.03f), null, 0.05f);
        Material iron = GameArt.Lit("CavalierFer", new Color(0.22f, 0.22f, 0.23f), GameArt.Noise("rust", 64, 0.55f, 0.35f, 23), 0.55f);

        legL = Limb(model, new Vector3(-0.17f, 1.25f, 0f), 1.25f, 0.13f, hide, hoof);
        legR = Limb(model, new Vector3(0.17f, 1.25f, 0f), 1.25f, 0.13f, hide, hoof);
        GameArt.Prim(PrimitiveType.Cube, model, new Vector3(0f, 1.3f, 0f), new Vector3(0.45f, 0.18f, 0.24f), hide);
        GameArt.Prim(PrimitiveType.Cylinder, model, new Vector3(0f, 1.75f, 0f), new Vector3(0.38f, 0.42f, 0.26f), hide);
        GameArt.Prim(PrimitiveType.Cube, model, new Vector3(0f, 2.15f, 0f), new Vector3(0.62f, 0.14f, 0.3f), hide);
        // Long cou incliné vers l'avant et tête de cheval (crâne allongé, museau, oreilles, crinière).
        var neck = GameArt.Prim(PrimitiveType.Cylinder, model, new Vector3(0f, 2.42f, 0.08f), new Vector3(0.16f, 0.3f, 0.2f), hide);
        neck.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
        head = new GameObject("Tete").transform;
        head.SetParent(model, false);
        head.localPosition = new Vector3(0f, 2.72f, 0.2f);
        var skull = GameArt.Prim(PrimitiveType.Cube, head, new Vector3(0f, 0f, 0.12f), new Vector3(0.24f, 0.26f, 0.42f), bone);
        skull.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);
        var snout = GameArt.Prim(PrimitiveType.Cube, head, new Vector3(0f, -0.14f, 0.38f), new Vector3(0.18f, 0.18f, 0.32f), bone);
        snout.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
        foreach (float side in new[] { -1f, 1f })
        {
            var ear = GameArt.Prim(PrimitiveType.Cube, head, new Vector3(side * 0.08f, 0.2f, -0.02f), new Vector3(0.05f, 0.16f, 0.06f), bone);
            ear.transform.localRotation = Quaternion.Euler(-15f, 0f, side * -15f);
        }
        GameArt.Prim(PrimitiveType.Cube, model, new Vector3(0f, 2.55f, -0.06f), new Vector3(0.05f, 0.45f, 0.16f), mane);
        armL = Limb(model, new Vector3(-0.38f, 2.1f, 0f), 1.05f, 0.09f, hide, hide);
        armR = Limb(model, new Vector3(0.38f, 2.1f, 0f), 1.05f, 0.09f, hide, hide);
        // Masse d'armes : manche dans la main droite, tête hérissée de pointes.
        var mace = new GameObject("Masse").transform;
        mace.SetParent(armR, false);
        mace.localPosition = new Vector3(0f, -1.05f, 0f);
        mace.localRotation = Quaternion.Euler(70f, 0f, 0f);
        GameArt.Prim(PrimitiveType.Cylinder, mace, new Vector3(0f, 0.35f, 0f), new Vector3(0.05f, 0.45f, 0.05f), GameArt.Lit("CavalierManche", new Color(0.18f, 0.11f, 0.06f), null, 0.2f));
        GameArt.Prim(PrimitiveType.Sphere, mace, new Vector3(0f, 0.85f, 0f), Vector3.one * 0.26f, iron);
        for (int k = 0; k < 6; k++)
        {
            Vector3 dir = Quaternion.Euler(k < 3 ? 0f : 90f, k * 60f, 0f) * Vector3.forward;
            var spike = GameArt.Prim(PrimitiveType.Cube, mace, new Vector3(0f, 0.85f, 0f) + dir * 0.15f, new Vector3(0.05f, 0.05f, 0.12f), iron);
            spike.transform.localRotation = Quaternion.LookRotation(dir);
        }
    }

    // Membre pendant depuis 'pivot' (pour se balancer), de longueur 'length' ; extrémité (sabot, main) au bout.
    static Transform Limb(Transform parent, Vector3 pivot, float length, float width, Material mat, Material end)
    {
        var limb = new GameObject("Membre").transform;
        limb.SetParent(parent, false);
        limb.localPosition = pivot;
        GameArt.Prim(PrimitiveType.Cylinder, limb, new Vector3(0f, -length * 0.5f, 0f), new Vector3(width, length * 0.5f, width), mat);
        GameArt.Prim(PrimitiveType.Cube, limb, new Vector3(0f, -length + 0.06f, 0.03f), new Vector3(width * 1.5f, 0.12f, width * 1.9f), end);
        return limb;
    }

    // ================= Affichage =================

    protected override void OnStateChanged(byte previous, byte next)
    {
        Vector3 mouth = transform.position + Vector3.up * EyeHeight;
        if ((State)next == State.Chase && (State)previous <= State.Stomp) GameAudio.Play3D("cavalier_alert", mouth, 1f, 40f, 0f);
        if ((State)next == State.Strike) { strikeShownAt = Time.time; strikePlayed = false; }
        if ((State)next == State.Stomp) { stompShownAt = Time.time; stompPlayed = false; }
    }

    // Il s'élance vers le joueur qui ne le regarde plus : cri de terreur.
    protected override void OnPulse() => GameAudio.Play3D("cavalier_terror", transform.position + Vector3.up * EyeHeight, 1f, 40f, 0f);

    protected override void Animate(float dt)
    {
        bool hunting = Current == State.Chase || Current == State.Strike;
        if (eyes != null) foreach (var e in eyes) if (e != null) e.enabled = hunting;
        if (eyeLight != null) eyeLight.enabled = hunting;

        // Galop : sabots qui frappent le sol, au rythme de son allure, à moins de 30 m.
        if (visualSpeed > 0.3f)
        {
            stepTimer -= dt * Mathf.Clamp(visualSpeed / 3f, 0.6f, 3f);
            if (stepTimer <= 0f)
            {
                stepTimer = 0.5f;
                GameAudio.Play3D("cavalier_gallop", transform.position, 0.8f, GameRules.CavalierGallopRange, 0.08f);
            }
        }

        if (animator != null)
        {
            string wanted = Current == State.Strike || Current == State.Stomp ? "Attack" : visualSpeed < 0.3f ? "Idle" : visualSpeed > 5.5f ? "Run" : "Walk";
            PlayAnim(wanted);
            animator.speed = wanted == "Walk" ? Mathf.Clamp(visualSpeed / 4.8f, 0.6f, 2f) : wanted == "Run" ? Mathf.Clamp(visualSpeed / 8.8f, 0.8f, 2f) : 1f;
        }
        AnimateFallback(dt);
        PlayImpacts();
    }

    // Jambes et bras qui se balancent, masse levée puis abattue (frappe du joueur, frappe du sol).
    void AnimateFallback(float dt)
    {
        if (legL == null) return;
        walkPhase += dt * visualSpeed * 2.2f;
        float swing = Mathf.Sin(walkPhase) * Mathf.Clamp01(visualSpeed / 2f) * 32f;
        legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
        armL.localRotation = Quaternion.Euler(-swing * 0.6f, 0f, -6f);
        float blow = BlowAngle();
        armR.localRotation = Quaternion.Euler(blow >= 0f ? blow : swing * 0.6f, 0f, 6f);
    }

    // Angle du bras droit pendant un coup (-1 : aucun coup) : levé haut, puis abattu d'un coup sec.
    float BlowAngle()
    {
        float t = Current == State.Strike ? Time.time - strikeShownAt : Current == State.Stomp ? Time.time - stompShownAt : -1f;
        float hit = Current == State.Strike ? StrikeHitTime : StompHitTime;
        if (t < 0f) return -1f;
        if (t < hit - 0.12f) return Mathf.Lerp(0f, -165f, Mathf.SmoothStep(0f, 1f, t / (hit - 0.12f)));
        if (t < hit) return Mathf.Lerp(-165f, Current == State.Stomp ? -10f : -70f, (t - (hit - 0.12f)) / 0.12f);
        return Current == State.Stomp ? -10f : -70f;
    }

    void PlayImpacts()
    {
        if (Current == State.Stomp && !stompPlayed && Time.time - stompShownAt >= StompHitTime)
        {
            stompPlayed = true;
            Vector3 ground = transform.position + transform.forward * 1.1f;
            GameAudio.Play3D("cavalier_stomp", ground, 1f, 30f, 0.05f);
            Fx.Burst(ground + Vector3.up * 0.1f, Fx.Dirt, 12, 3f, 0.08f, 0.8f);
        }
        if (Current == State.Strike && !strikePlayed && Time.time - strikeShownAt >= StrikeHitTime)
        {
            strikePlayed = true;
            GameAudio.Play3D("cavalier_strike", transform.position + transform.forward + Vector3.up, 1f, 30f, 0.05f);
        }
    }
}
