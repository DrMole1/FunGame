using System.Collections;
using UnityEngine;

// Dance : danse 1 (celle du Fou), aussi jouée immobile sur un Téléporteur ou une Station Curative ; Dance2 et Dance3 :
// touches O et P ; Dig : creuser un monticule (V0.4.6).
public enum AnimState : byte { Idle, Walk, Run, Jump, Sneak, SneakIdle, Dance, Dig, Dance2, Dance3 }

// Gestes ponctuels du haut du corps, vus par tous les joueurs.
public enum PlayerGesture : byte { Bark, PickUp, Place, Use, Eat }

// Modèle 3D animé d'un joueur (local ou distant), enfant du GameObject physique.
public class CharacterAvatar : MonoBehaviour
{
    // Le clip de saut décolle vers 0,45 s : on saute l'élan, la physique gère l'impulsion.
    const float JumpClipOffset = 0.45f;
    // Marche discrète à l'arrêt : posture baissée figée sur une image du clip où les deux pieds sont au sol.
    const float SneakIdlePose = 2.12f;
    const float DeathDuration = 1.1f;
    // Morts Meshy (Death0 à Death2) : début, fin (au sol) et vitesse de lecture, mesurés sur les clips
    // (électrocuté : convulsions puis chute ; étranglé : on saute le début de la lutte ; à la renverse).
    static readonly (float start, float end, float speed)[] DeathClips = { (0f, 5.4f, 1.6f), (2.5f, 7.8f, 1.6f), (0f, 2.2f, 1f) };
    static int DeathVariants => DeathClips.Length;
    const float HitStop = 0.28f, LieDuration = 2.2f, SinkDuration = 1.2f;

    public int CharacterIndex { get; private set; } = -1;
    public AnimState State { get; private set; }

    Animator animator;
    GameObject model;
    Vector3 modelPos;
    Vector3 modelScale;
    Coroutine deathRoutine;
    bool dead;
    MirkovZipper zipper;

    public void SetCharacter(CharacterLibrary library, int index)
    {
        if (library == null || library.Count == 0) return;
        index = Mathf.Clamp(index, 0, library.Count - 1);
        if (index == CharacterIndex && model != null) return;
        if (model != null) Destroy(model);

        CharacterLibrary.Entry entry = library.characters[index];
        model = Instantiate(entry.model, transform);
        model.name = "Model_" + entry.name;
        // Le GameObject parent est centré sur le collider : on pose les pieds à sa base.
        modelPos = Vector3.down * (library.height * 0.5f);
        modelScale = Vector3.one * entry.scale;
        model.transform.localPosition = modelPos;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = modelScale;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        SetLayerRecursive(model, gameObject.layer);

        if (!model.TryGetComponent(out animator)) animator = model.AddComponent<Animator>();
        spine = chest = neck = head = upperArm = lowerArm = hand = null;
        leftUpperArm = leftLowerArm = leftHand = hips = rightFinger = leftFinger = null;
        leftUpperLeg = rightUpperLeg = leftLowerLeg = rightLowerLeg = leftFoot = rightFoot = null;
        MeasureHands();
        MeasureLegs();
        animator.runtimeAnimatorController = entry.controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // V0.6.13 : Mirkov porte une fermeture éclair dans le dos.
        if (zipper != null) Destroy(zipper.gameObject);
        zipper = index == MirkovZipper.Skin ? MirkovZipper.Attach(transform, model, animator) : null;
        if (heldBoard != null) Destroy(heldBoard);
        heldBoard = null;
        CharacterIndex = index;
        State = AnimState.Idle;
        animator.Play("Idle", 0, 0f);
        ResetIdle();
        model.SetActive(!dead && !hidden);
    }

    // 'restart' : rejoue l'animation même si le personnage est déjà dans cet état (rebond sur le Trampoline en plein saut).
    public void SetState(AnimState state, bool restart = false)
    {
        if ((state == State && !restart) || animator == null) return;
        AnimState previous = State;
        State = state;
        // Corps caché (téléportation) : rien à animer.
        if (!animator.gameObject.activeInHierarchy) return;
        // Le clip de saut suit la durée raccourcie du saut.
        animator.speed = state == AnimState.Jump ? 1f / GameRules.JumpTimeScale : 1f;
        float fade = BlendTime(previous, state);
        if (state == AnimState.Jump)
            animator.CrossFadeInFixedTime("Jump", fade, 0, JumpClipOffset);
        else if (state == AnimState.SneakIdle)
            animator.CrossFadeInFixedTime("SneakIdle", fade, 0, SneakIdlePose);
        else if (state == AnimState.Idle)
        {
            // Retour à l'attente : toujours l'idle principal, la prochaine variante viendra plus tard.
            ResetIdle();
            animator.CrossFadeInFixedTime(IdleStates[0], fade);
        }
        else
            animator.CrossFadeInFixedTime(state.ToString(), fade);
    }

    // Durée des fondus entre animations : assez longue pour que les changements d'allure restent fluides, plus courte
    // pour le saut (l'élan doit partir tout de suite) et à l'arrivée au sol.
    static float BlendTime(AnimState from, AnimState to)
    {
        if (to == AnimState.Jump) return 0.12f;
        if (from == AnimState.Jump) return 0.2f;
        if (to >= AnimState.Dance || from >= AnimState.Dance) return 0.35f;
        bool still = to == AnimState.Idle || to == AnimState.SneakIdle, wasStill = from == AnimState.Idle || from == AnimState.SneakIdle;
        if (still || wasStill) return 0.3f;
        return 0.25f;
    }

    // Attente : l'idle principal (calme), et de temps en temps l'une des deux variantes (il regarde autour de lui, ou se
    // tient nonchalamment), en long fondu.
    // Purement visuel : chaque client choisit ses variantes.
    static readonly string[] IdleStates = { "Idle", "IdleB", "IdleC" };
    const float IdleSwitchMin = 7f, IdleSwitchMax = 13f, IdleBlend = 1f;
    int idleVariant;
    float nextIdleAt;

    void ResetIdle()
    {
        idleVariant = 0;
        nextIdleAt = Time.time + Random.Range(IdleSwitchMin, IdleSwitchMax);
    }

    void Update()
    {
        // Protocole en main, ou assis au début de la partie : l'attente principale seulement, sans variante où le corps bouge.
        if ((holding || Sitting) && idleVariant != 0 && State == AnimState.Idle && animator != null && animator.gameObject.activeInHierarchy)
        {
            ResetIdle();
            animator.CrossFadeInFixedTime(IdleStates[0], 0.3f);
            return;
        }
        if (holding || Sitting) return;
        if (State != AnimState.Idle || dead || animator == null || !animator.gameObject.activeInHierarchy || Time.time < nextIdleAt) return;
        // Une variante, puis retour à l'idle principal ; les variantes ne s'enchaînent pas entre elles.
        int next = idleVariant != 0 ? 0 : Random.Range(1, IdleStates.Length);
        nextIdleAt = Time.time + (next == 0 ? Random.Range(IdleSwitchMin, IdleSwitchMax) : Random.Range(5f, 9f));
        if (!animator.HasState(0, Animator.StringToHash(IdleStates[next]))) return;
        idleVariant = next;
        animator.CrossFadeInFixedTime(IdleStates[next], IdleBlend);
    }

    // Gestes joués par-dessus l'animation en cours (le personnage peut marcher en même temps), sur le haut du corps :
    // - Aboiement : le buste se cambre et la tête se relève (inspiration), puis tout se projette d'un coup sec vers
    //   l'avant (le « Wouf »), avant de revenir en douceur.
    // - Ramasser / Poser : le buste se penche, le bras droit va chercher (ou dépose) l'objet au sol.
    // - Utiliser : le bras droit se tend devant soi.
    // - Manger (V0.5.1, 1,5 s) : la main droite porte une Cuisse de poulet à la bouche, trois bouchées, puis redescend.
    float gestureStart = -10f;
    PlayerGesture gesture;
    Transform spine, chest, neck, head, upperArm, lowerArm, hand;
    // Cuisse de poulet tenue pendant le geste Manger.
    GameObject eatenProp;

    const float EatDuration = 1.5f;
    static readonly float[] Bites = { 0.45f, 0.8f, 1.12f };

    static float GestureDuration(PlayerGesture g) =>
        g == PlayerGesture.Bark ? 0.55f : g == PlayerGesture.Place ? 0.8f : g == PlayerGesture.Eat ? EatDuration : 0.6f;

    public void PlayGesture(PlayerGesture g)
    {
        if (animator == null || dead) return;
        gesture = g;
        gestureStart = Time.time;
        if (head == null && animator.isHuman)
        {
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        CacheBones();
        if (g == PlayerGesture.Eat && eatenProp == null) eatenProp = BuildDrumstick(transform);
    }

    void CacheBones()
    {
        if (animator == null || !animator.isHuman || leftUpperArm != null) return;
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightFinger = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        leftFinger = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
    }

    // ---------- Postures tenues (V0.6.13), jouées par-dessus l'animation en cours ----------
    // - Protocole tenu à deux mains devant la poitrine (objet sélectionné dans l'inventaire).
    // - Bras droit tendu vers l'avant tant que le clic est maintenu sur un glyphe neutre (il se lève puis se baisse).
    // - Note 06 du chapitre 3 : à l'arrivée dans la partie, assis au sol 0,3 seconde, puis il se relève.
    Transform leftUpperArm, leftLowerArm, leftHand, hips, leftUpperLeg, rightUpperLeg, leftLowerLeg, rightLowerLeg, leftFoot, rightFoot;
    Transform rightFinger, leftFinger;
    // Protocole en main : sa taille, et la hauteur des prises (part de sa hauteur) où les mains le tiennent par les côtés.
    const float BoardScale = 0.85f, GripHeight = 0.55f;
    bool holding, armRaised;
    float holdWeight, armWeight, sitStart = -10f;
    GameObject heldBoard;
    public const float SitHold = 0.3f, StandUpDuration = 0.9f;

    public void SetHolding(bool on) => holding = on;
    // Note 14 du chapitre 3.2 (V0.6.14) : assis par terre tant que le joueur reste accroupi sans bouger. Il s'assoit en
    // douceur et se relève très vite.
    bool seated;
    float seatedWeight;
    public void SetSeated(bool on) => seated = on;
    public void SetArmRaised(bool on) => armRaised = on;
    public void PlaySitUp() => sitStart = Time.time;
    public bool Sitting => Time.time - sitStart < SitHold + StandUpDuration;

    // Poids de la position assise : 1 pendant SitHold, puis le personnage se relève.
    float SitWeight()
    {
        float t = Time.time - sitStart;
        if (t < 0f || t > SitHold + StandUpDuration) return 0f;
        return t < SitHold ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - SitHold) / StandUpDuration);
    }

    void UpdatePoses()
    {
        bool visible = !dead && !hidden && model != null && model.activeInHierarchy && animator != null && animator.isHuman;
        holdWeight = Mathf.MoveTowards(holdWeight, holding && visible ? 1f : 0f, Time.deltaTime * 5f);
        armWeight = Mathf.MoveTowards(armWeight, armRaised && visible ? 1f : 0f, Time.deltaTime * 4f);
        seatedWeight = Mathf.MoveTowards(seatedWeight, seated && visible ? 1f : 0f, Time.deltaTime * (seated ? 2.5f : 6f));
        float sit = visible ? Mathf.Max(SitWeight(), Mathf.SmoothStep(0f, 1f, seatedWeight)) : 0f;
        if (model != null && deathRoutine == null && !dead) model.transform.localPosition = modelPos;
        if (!visible)
        {
            if (heldBoard != null) heldBoard.SetActive(false);
            return;
        }
        CacheBones();
        Vector3 axis = model.transform.right;
        if (sit > 0f && deathRoutine == null) SitPose(sit);
        if (armWeight > 0f)
        {
            Tilt(upperArm, axis, -80f * armWeight);
            Tilt(lowerArm, axis, -8f * armWeight);
        }
        if (holdWeight > 0f && chest != null) HoldBoard();
        else if (heldBoard != null) heldBoard.SetActive(false);
    }

    // Assis par terre, jambes allongées devant soi, puis il se relève d'un mouvement souple. Tout dépend du skin (mesuré
    // au repos, MeasureLegs) : les hanches descendent jusqu'à l'épaisseur de la cuisse au-dessus du sol, chaque jambe est
    // posée par IK talon au sol (orteils vers le haut), les mains se posent sur les cuisses. En se relevant, les pieds
    // restent au sol : seules les hanches remontent.
    void SitPose(float sit)
    {
        if (!legsMeasured || hips == null) return;
        Transform m = model.transform;
        Vector3 fwd = m.forward, right = m.right, up = m.up;
        float groundY = transform.TransformPoint(modelPos).y;
        // Pieds de l'animation, avant de descendre : ils sont au sol.
        Vector3 leftStand = leftFoot.position, rightStand = rightFoot.position;
        Quaternion leftFootRot = leftFoot.rotation, rightFootRot = rightFoot.rotation;
        float hipH = hips.position.y - m.position.y;
        float drop = sit * Mathf.Max(0f, hipH - seatHeight);
        m.localPosition = modelPos + Vector3.down * drop;
        // Fondu rapide vers la pose IK : à mi-relevé, les jambes sont déjà pliées, sans à-coup à la fin.
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sit * 2.5f));
        Tilt(spine, right, 10f * sit);
        Tilt(chest, right, 6f * sit);
        foreach (int side in new[] { 1, -1 })
        {
            Transform upLeg = side > 0 ? rightUpperLeg : leftUpperLeg, lowLeg = side > 0 ? rightLowerLeg : leftLowerLeg;
            Transform foot = side > 0 ? rightFoot : leftFoot;
            Vector3 stand = (side > 0 ? rightStand : leftStand);
            Quaternion footRot = side > 0 ? rightFootRot : leftFootRot;
            Vector3 hip = upLeg.position;
            // Talon au sol devant la hanche, jambe presque tendue, pieds un peu écartés.
            float targetY = groundY + ankleHeight;
            float dy = Mathf.Max(0f, hip.y - targetY), reach = legLength * 0.95f;
            float ahead = Mathf.Sqrt(Mathf.Max(0f, reach * reach - dy * dy));
            float lateral = Vector3.Dot(hip - hips.position, right) * 1.4f;
            Vector3 seated = hips.position + Vector3.Project(hip - hips.position, up) + fwd * ahead + right * lateral;
            seated.y = targetY;
            Vector3 target = Vector3.Lerp(stand, seated, sit);
            target.y = Mathf.Max(target.y, targetY);
            Quaternion u0 = upLeg.rotation, l0 = lowLeg.rotation;
            // Genou vers le haut et l'avant.
            Vector3 pole = hip + fwd * (legLength * 0.5f) + up * legLength;
            SolveArm(upLeg, lowLeg, foot, target, pole, 1f);
            upLeg.rotation = Quaternion.Slerp(u0, upLeg.rotation, k);
            lowLeg.rotation = Quaternion.Slerp(l0, lowLeg.rotation, k);
            // Pied : la pointe se relève (talon au sol) au lieu de suivre le tibia dans le sol.
            Quaternion heel = Quaternion.AngleAxis(-60f * sit, right) * footRot;
            foot.rotation = Quaternion.Slerp(foot.rotation, heel, k);

            // Main posée sur la cuisse, un peu avant le genou, paume vers le bas, doigts vers l'avant.
            Transform arm = side > 0 ? upperArm : leftUpperArm, fore = side > 0 ? lowerArm : leftLowerArm, wrist = side > 0 ? hand : leftHand;
            if (arm == null || fore == null || wrist == null) continue;
            Vector3 thigh = Vector3.Lerp(upLeg.position, lowLeg.position, 0.6f) + up * (thighRadius + 0.02f) + right * (side * thighRadius * 0.3f);
            Quaternion a0 = arm.rotation, f0 = fore.rotation, w0 = wrist.rotation;
            Vector3 elbowPole = arm.position + right * (side * armLength) - fwd * (armLength * 0.3f);
            SolveArm(arm, fore, wrist, thigh, elbowPole, 1f);
            if (handsMeasured)
            {
                Vector3 fingers = (fwd - up * 0.3f).normalized;
                Vector3 palm = Vector3.ProjectOnPlane(-up, fingers).normalized;
                Vector3 fa = side > 0 ? rightFingerAxis : leftFingerAxis, pa = side > 0 ? rightPalmAxis : leftPalmAxis;
                wrist.rotation = Quaternion.LookRotation(fingers, palm) * Quaternion.Inverse(Quaternion.LookRotation(fa, pa));
            }
            arm.rotation = Quaternion.Slerp(a0, arm.rotation, k);
            fore.rotation = Quaternion.Slerp(f0, fore.rotation, k);
            wrist.rotation = Quaternion.Slerp(w0, wrist.rotation, k);
        }
    }

    // Mesures des jambes au repos : longueur (hanche → cheville), hauteur de la cheville et épaisseur de la cuisse (sur le
    // maillage : distance des sommets de la cuisse à l'axe de l'os). Assis, l'articulation de la hanche est à une épaisseur
    // de cuisse au-dessus du sol.
    float legLength = 0.4f, ankleHeight = 0.08f, thighRadius = 0.07f, seatHeight = 0.08f;
    bool legsMeasured;

    void MeasureLegs()
    {
        legsMeasured = false;
        if (hips == null || leftUpperLeg == null || rightUpperLeg == null || leftLowerLeg == null || rightLowerLeg == null || leftFoot == null || rightFoot == null) return;
        float ground = model.transform.position.y;
        legLength = 0.5f * (Vector3.Distance(leftUpperLeg.position, leftLowerLeg.position) + Vector3.Distance(leftLowerLeg.position, leftFoot.position)
                          + Vector3.Distance(rightUpperLeg.position, rightLowerLeg.position) + Vector3.Distance(rightLowerLeg.position, rightFoot.position));
        if (legLength < 0.05f) return;
        ankleHeight = Mathf.Clamp(0.5f * (leftFoot.position.y + rightFoot.position.y) - ground, 0.01f, legLength * 0.3f);
        thighRadius = MeasureThigh();
        if (thighRadius <= 0f) thighRadius = legLength * 0.16f;
        seatHeight = Mathf.Clamp(thighRadius * 1.05f, 0.03f, legLength * 0.4f);
        legsMeasured = true;
    }

    float MeasureThigh()
    {
        var dist = new System.Collections.Generic.List<float>();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh mesh = smr.sharedMesh;
            if (!smr.enabled || mesh == null || !mesh.isReadable) continue;
            BoneWeight[] weights = mesh.boneWeights;
            Transform[] bones = smr.bones;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            Vector3[] verts = baked.vertices;
            Destroy(baked);
            if (weights.Length != verts.Length) continue;
            Matrix4x4 w = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            for (int i = 0; i < verts.Length; i++)
            {
                int b = weights[i].boneIndex0;
                if (b < 0 || b >= bones.Length) continue;
                Transform top = bones[b] == leftUpperLeg ? leftUpperLeg : bones[b] == rightUpperLeg ? rightUpperLeg : null;
                if (top == null) continue;
                Transform knee = top == leftUpperLeg ? leftLowerLeg : rightLowerLeg;
                Vector3 p = w.MultiplyPoint3x4(verts[i]), a = top.position, ab = knee.position - a;
                float t = Vector3.Dot(p - a, ab) / ab.sqrMagnitude;
                if (t < 0.2f || t > 0.6f) continue;
                dist.Add(Vector3.Distance(p, a + ab * t));
            }
        }
        if (dist.Count < 10) return 0f;
        dist.Sort();
        return dist[(int)(dist.Count * 0.75f)];
    }

    // Mesures de chaque skin, prises sur sa pose de repos (T-pose, paumes vers le bas) juste après l'instanciation :
    // longueur des bras et, pour chaque main, l'axe des doigts et la normale de la paume dans le repère
    // de l'os de la main. Les rigs n'orientent pas tous leurs os de la même façon : sans cela, la main n'est bien tournée
    // que sur le skin pour lequel les angles ont été réglés.
    float armLength = 0.5f, boardScale = BoardScale;
    Vector3 rightFingerAxis, rightPalmAxis, leftFingerAxis, leftPalmAxis;
    bool handsMeasured;
    // Longueur des bras (épaule → poignet) de Jerk, sur qui la taille du Protocole a été réglée.
    const float ReferenceArm = 0.5f;

    void MeasureHands()
    {
        handsMeasured = false;
        if (animator == null || !animator.isHuman) return;
        CacheBones();
        if (upperArm == null || lowerArm == null || hand == null || leftUpperArm == null || leftLowerArm == null || leftHand == null) return;
        armLength = 0.5f * (Vector3.Distance(upperArm.position, lowerArm.position) + Vector3.Distance(lowerArm.position, hand.position)
                          + Vector3.Distance(leftUpperArm.position, leftLowerArm.position) + Vector3.Distance(leftLowerArm.position, leftHand.position));
        if (armLength < 0.05f) return;
        boardScale = BoardScale * Mathf.Clamp(armLength / ReferenceArm, 0.72f, 1f);
        Vector3 down = -model.transform.up;
        MeasureHand(lowerArm, hand, down, out rightFingerAxis, out rightPalmAxis);
        MeasureHand(leftLowerArm, leftHand, down, out leftFingerAxis, out leftPalmAxis);
        handsMeasured = true;
    }

    static void MeasureHand(Transform lower, Transform wrist, Vector3 down, out Vector3 finger, out Vector3 palm)
    {
        Vector3 f = (wrist.position - lower.position).normalized;
        Vector3 p = Vector3.ProjectOnPlane(down, f).normalized;
        finger = Quaternion.Inverse(wrist.rotation) * f;
        palm = Quaternion.Inverse(wrist.rotation) * p;
    }

    // Le Protocole tenu à deux mains devant la poitrine, le texte tourné vers le personnage (incliné, comme quand on lit) :
    // chaque main le saisit par un côté, la paume contre la tranche et les doigts passés derrière la plaque, les coudes
    // vers le bas (IK à deux os). Tout est proportionnel au skin : hauteur des épaules, longueur des bras.
    void HoldBoard()
    {
        if (heldBoard == null)
        {
            heldBoard = ProtocolBoard.Build(transform, boardScale);
            SetLayerRecursive(heldBoard, gameObject.layer);
        }
        heldBoard.SetActive(holdWeight > 0.5f);
        Vector3 fwd = model.transform.forward, right = model.transform.right, upDir = model.transform.up;
        Tilt(head, right, 12f * holdWeight);
        Transform board = heldBoard.transform;
        float w = ProtocolBoard.Width * boardScale, h = ProtocolBoard.Height * boardScale;
        board.rotation = Quaternion.LookRotation(-fwd, upDir) * Quaternion.Euler(-20f, 0f, 0f);
        // Les prises (milieu des côtés, à GripHeight de la hauteur) : sous les épaules et devant elles, à portée de bras
        // coudes pliés, quelle que soit la taille du personnage.
        Vector3 shoulders = upperArm != null && leftUpperArm != null ? (upperArm.position + leftUpperArm.position) * 0.5f : chest.position + upDir * 0.15f;
        Vector3 gripCenter = shoulders + fwd * (armLength * 0.62f) - upDir * (armLength * 0.52f);
        board.position = gripCenter - board.up * (h * GripHeight);
        float halfW = w * 0.5f;
        Vector3 back = -board.forward;   // côté opposé au personnage
        foreach (int side in new[] { 1, -1 })
        {
            Transform up = side > 0 ? upperArm : leftUpperArm, low = side > 0 ? lowerArm : leftLowerArm;
            Transform wrist = side > 0 ? hand : leftHand;
            if (up == null || low == null || wrist == null) continue;
            Vector3 outward = right * side;
            // Le côté du personnage (droite / gauche) est à -X de la plaque, qui lui fait face.
            Vector3 edge = gripCenter + board.right * (-side * halfW);
            // La main : doigts dressés le long de la tranche, penchés vers l'arrière de la plaque, paume contre le bord.
            Vector3 fingers = (board.up * 0.85f + back * 0.4f - outward * 0.1f).normalized;
            Vector3 palm = Vector3.ProjectOnPlane(-outward, fingers).normalized;
            // Le poignet sous la prise, la main décalée vers l'extérieur de l'épaisseur de la paume.
            float handLen = armLength * 0.2f;
            Vector3 grip = edge - fingers * (handLen * 0.5f) + outward * (armLength * 0.05f) - back * (armLength * 0.08f);
            Vector3 pole = up.position - upDir * armLength + outward * (armLength * 0.4f) - fwd * (armLength * 0.3f);
            SolveArm(up, low, wrist, grip, pole, holdWeight);
            if (handsMeasured)
            {
                Vector3 fingerAxis = side > 0 ? rightFingerAxis : leftFingerAxis, palmAxis = side > 0 ? rightPalmAxis : leftPalmAxis;
                Quaternion target = Quaternion.LookRotation(fingers, palm) * Quaternion.Inverse(Quaternion.LookRotation(fingerAxis, palmAxis));
                wrist.rotation = Quaternion.Slerp(wrist.rotation, target, holdWeight);
            }
        }
    }

    // IK analytique à deux os : l'épaule et le coude tournent pour que le poignet atteigne 'target', le coude du côté de 'pole'.
    static void SolveArm(Transform upper, Transform lower, Transform wrist, Vector3 target, Vector3 pole, float weight)
    {
        Vector3 a = upper.position, b = lower.position, c = wrist.position;
        float ab = Vector3.Distance(a, b), bc = Vector3.Distance(b, c);
        if (ab < 1e-4f || bc < 1e-4f) return;
        Vector3 t = Vector3.Lerp(c, target, weight);
        Vector3 toT = t - a;
        float at = Mathf.Clamp(toT.magnitude, Mathf.Abs(ab - bc) + 1e-3f, ab + bc - 1e-3f);
        Vector3 dir = toT.normalized;
        Vector3 bendAxis = Vector3.Cross(dir, pole - a);
        if (bendAxis.sqrMagnitude < 1e-6f) bendAxis = Vector3.Cross(dir, Vector3.down);
        bendAxis.Normalize();
        float angle = Mathf.Acos(Mathf.Clamp((ab * ab + at * at - bc * bc) / (2f * ab * at), -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 elbow = a + Quaternion.AngleAxis(angle, bendAxis) * dir * ab;
        upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(wrist.position - lower.position, t - lower.position) * lower.rotation;
    }


    // Cuisse de poulet (modèle Meshy, à défaut une version en primitives), d'environ 28 cm.
    static GameObject BuildDrumstick(Transform parent)
    {
        var prop = GameArt.SpawnModel("CuisseDePoulet", parent, 0.28f);
        if (prop == null)
        {
            prop = new GameObject("CuisseDePoulet");
            prop.transform.SetParent(parent, false);
            GameArt.Prim(PrimitiveType.Sphere, prop.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.09f, 0.11f, 0.09f), GameArt.Lit("CuisseViande", new Color(0.7f, 0.4f, 0.15f)));
            GameArt.Prim(PrimitiveType.Cylinder, prop.transform, new Vector3(0f, 0.13f, 0f), new Vector3(0.025f, 0.04f, 0.025f), GameArt.Lit("CuisseOs", new Color(0.95f, 0.92f, 0.85f)));
        }
        foreach (var c in prop.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var r in prop.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return prop;
    }

    void HideDrumstick()
    {
        if (eatenProp != null) Destroy(eatenProp);
        eatenProp = null;
    }

    void LateUpdate()
    {
        UpdatePoses();
        float t = Time.time - gestureStart, duration = GestureDuration(gesture);
        if (t < 0f || t > duration || dead || hidden || head == null || !model.activeInHierarchy)
        {
            if (eatenProp != null) HideDrumstick();
            return;
        }
        // Angles positifs : vers l'avant (buste, tête) ; négatifs sur le bras : il se lève devant soi.
        Vector3 axis = model.transform.right;
        switch (gesture)
        {
            case PlayerGesture.Bark:
            {
                // Recul (0 → 0,12 s), coup vers l'avant (→ 0,2 s), puis retour.
                float lean = t < 0.12f ? -Mathf.SmoothStep(0f, 1f, t / 0.12f)
                    : t < 0.2f ? Mathf.Lerp(-1f, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.12f) / 0.08f))
                    : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.2f) / (duration - 0.2f));
                Tilt(chest, axis, lean * (lean > 0f ? 9f : 6f));
                Tilt(neck, axis, lean * (lean > 0f ? 8f : 10f));
                Tilt(head, axis, lean * (lean > 0f ? 6f : 14f));
                break;
            }
            case PlayerGesture.PickUp:
            case PlayerGesture.Place:
            {
                bool place = gesture == PlayerGesture.Place;
                float w = Envelope(t, place ? 0.3f : 0.22f, place ? 0.18f : 0.1f, duration);
                Tilt(spine, axis, w * 20f);
                Tilt(chest, axis, w * 16f);
                Tilt(neck, axis, w * 8f);
                Tilt(head, axis, w * 10f);
                Tilt(upperArm, axis, -w * (place ? 50f : 42f));
                Tilt(lowerArm, axis, -w * 12f);
                break;
            }
            case PlayerGesture.Use:
            {
                float w = Envelope(t, 0.15f, 0.15f, duration);
                Tilt(chest, axis, w * 4f);
                Tilt(head, axis, w * 6f);
                Tilt(upperArm, axis, -w * 70f);
                Tilt(lowerArm, axis, -w * 15f);
                break;
            }
            case PlayerGesture.Eat:
            {
                float w = Envelope(t, 0.25f, 0.95f, duration);
                // Trois bouchées : la tête plonge vers la cuisse, que la main ramène un peu plus près.
                float bite = 0f;
                foreach (float at in Bites) bite = Mathf.Max(bite, Mathf.Clamp01(1f - Mathf.Abs(t - at) / 0.12f));
                bite = Mathf.SmoothStep(0f, 1f, bite);
                Tilt(neck, axis, bite * 6f);
                Tilt(head, axis, -w * 6f + bite * 12f);
                Tilt(upperArm, axis, -w * 55f - bite * 6f);
                Tilt(lowerArm, axis, -w * 95f - bite * 10f);
                if (eatenProp != null && hand != null)
                {
                    // Tenue par l'os (en haut du modèle, qui pointe ici vers le bas et l'avant) : la partie charnue
                    // remonte vers la bouche.
                    Vector3 bone = (model.transform.forward * 0.5f - model.transform.up).normalized;
                    eatenProp.transform.rotation = Quaternion.FromToRotation(Vector3.up, bone);
                    eatenProp.transform.position = hand.position - bone * 0.2f;
                }
                break;
            }
        }
    }

    // Montée (attack), maintien (hold) puis retour jusqu'à la fin du geste.
    static float Envelope(float t, float attack, float hold, float duration) =>
        t < attack ? Mathf.SmoothStep(0f, 1f, t / attack)
        : t < attack + hold ? 1f
        : 1f - Mathf.SmoothStep(0f, 1f, (t - attack - hold) / (duration - attack - hold));

    static void Tilt(Transform bone, Vector3 axis, float angle)
    {
        if (bone != null) bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
    }

    // En téléportation, le corps disparaît (remplacé par une boule d'énergie).
    bool hidden;

    public void SetHidden(bool value)
    {
        hidden = value;
        if (model != null) model.SetActive(!dead && !hidden);
        // De retour : l'animation reprend sur l'état courant.
        if (!hidden && !dead && animator != null && model.activeInHierarchy) animator.Play(State.ToString(), 0, 0f);
    }

    public void SetDead(bool value)
    {
        if (value == dead) return;
        dead = value;
        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = null;
        if (model == null) return;
        if (dead && isActiveAndEnabled)
        {
            deathRoutine = StartCoroutine(DeathAnimation());
        }
        else
        {
            model.SetActive(!dead);
            model.transform.localPosition = modelPos;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = modelScale;
            if (!dead)
            {
                State = AnimState.Idle;
                animator.speed = 1f;
                animator.Play("Idle", 0, 0f);
                ResetIdle();
            }
        }
    }

    // Mort : arrêt sur image (le temps se fige), puis une des animations de mort Meshy jouée en entier ;
    // le corps reste un moment au sol, puis s'enfonce et disparaît.
    IEnumerator DeathAnimation()
    {
        int variant = Random.Range(0, DeathVariants);
        int hash = Animator.StringToHash("Death" + variant);
        if (animator == null || !animator.HasState(0, hash))
        {
            yield return FallbackDeath();
            yield break;
        }
        Transform t = model.transform;
        // Le coup fige le personnage, secoué de spasmes.
        animator.speed = 0f;
        for (float time = 0f; time < HitStop; time += Time.deltaTime)
        {
            t.localPosition = modelPos + Random.insideUnitSphere * 0.03f;
            yield return null;
        }
        t.localPosition = modelPos;
        var clip = DeathClips[variant];
        animator.speed = clip.speed;
        animator.CrossFadeInFixedTime(hash, 0.06f, 0, clip.start);
        yield return new WaitForSeconds((clip.end - clip.start) / clip.speed);
        // Figé au sol.
        animator.speed = 0f;
        yield return new WaitForSeconds(LieDuration);
        // Le corps s'enfonce dans le sol.
        for (float time = 0f; time < SinkDuration; time += Time.deltaTime)
        {
            t.localPosition = modelPos + Vector3.down * (Mathf.SmoothStep(0f, 1f, time / SinkDuration) * 0.7f);
            yield return null;
        }
        model.SetActive(false);
        t.localPosition = modelPos;
        deathRoutine = null;
    }

    // Quota non atteint : le corps vole en morceaux (Gibs) et le modèle disparaît aussitôt, sans animation de mort.
    // Rend false si le personnage n'était pas visible (déjà mort, caché, en téléportation).
    public bool Explode(Vector3 center)
    {
        if (model == null || dead || hidden || !model.activeInHierarchy) return false;
        HideDrumstick();
        Gibs.Spawn(model, animator, center);
        dead = true;
        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = null;
        model.SetActive(false);
        return true;
    }

    // Sans animation de mort : le personnage bascule, rétrécit et disparaît.
    IEnumerator FallbackDeath()
    {
        Transform t = model.transform;
        float side = Random.value < 0.5f ? -1f : 1f;
        for (float time = 0f; time < DeathDuration; time += Time.deltaTime)
        {
            float k = time / DeathDuration;
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 1.8f));
            t.localRotation = Quaternion.Euler(0f, k * 140f * side, 85f * fall * side);
            float shrink = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, k - 0.45f) / 0.55f);
            t.localScale = modelScale * Mathf.Max(0.01f, shrink);
            t.localPosition = modelPos + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.4f);
            yield return null;
        }
        model.SetActive(false);
        deathRoutine = null;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }

    // Ajoute (ou réutilise) le collider capsule aux dimensions des personnages.
    public static CapsuleCollider EnsureCapsule(GameObject go, CharacterLibrary library)
    {
        if (!go.TryGetComponent(out CapsuleCollider capsule)) capsule = go.AddComponent<CapsuleCollider>();
        capsule.height = library.height;
        capsule.radius = library.radius;
        capsule.center = Vector3.zero;
        return capsule;
    }

    // Marche discrète : capsule plus basse, sa base (les pieds) ne bouge pas.
    public static void SetCrouched(CapsuleCollider capsule, CharacterLibrary library, bool crouched)
    {
        float h = library.height * (crouched ? GameRules.SneakHeightFactor : 1f);
        capsule.height = h;
        capsule.center = Vector3.down * ((library.height - h) * 0.5f);
    }
}
