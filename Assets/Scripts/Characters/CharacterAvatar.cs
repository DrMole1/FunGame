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
        animator.runtimeAnimatorController = entry.controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        spine = chest = neck = head = upperArm = lowerArm = null;
        CharacterIndex = index;
        State = AnimState.Idle;
        animator.Play("Idle", 0, 0f);
        model.SetActive(!dead && !hidden);
    }

    public void SetState(AnimState state)
    {
        if (state == State || animator == null) return;
        State = state;
        // Corps caché (téléportation) : rien à animer.
        if (!animator.gameObject.activeInHierarchy) return;
        // Le clip de saut suit la durée raccourcie du saut.
        animator.speed = state == AnimState.Jump ? 1f / GameRules.JumpTimeScale : 1f;
        if (state == AnimState.Jump)
            animator.CrossFadeInFixedTime("Jump", 0.1f, 0, JumpClipOffset);
        else if (state == AnimState.SneakIdle)
            animator.CrossFadeInFixedTime("SneakIdle", 0.25f, 0, SneakIdlePose);
        else
            animator.CrossFadeInFixedTime(state.ToString(), state == AnimState.Sneak || state >= AnimState.Dance ? 0.25f : 0.15f);
    }

    // Gestes joués par-dessus l'animation en cours (le personnage peut marcher en même temps), sur le haut du corps :
    // - Aboiement : le buste se cambre et la tête se relève (inspiration), puis tout se projette d'un coup sec vers
    //   l'avant (le « Wouf »), avant de revenir en douceur.
    // - Ramasser / Poser : le buste se penche, le bras droit va chercher (ou dépose) l'objet au sol.
    // - Utiliser : le bras droit se tend devant soi ; Manger : la main monte à la bouche.
    float gestureStart = -10f;
    PlayerGesture gesture;
    Transform spine, chest, neck, head, upperArm, lowerArm;

    static float GestureDuration(PlayerGesture g) =>
        g == PlayerGesture.Bark ? 0.55f : g == PlayerGesture.Place ? 0.8f : g == PlayerGesture.Eat ? 0.75f : 0.6f;

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
        }
    }

    void LateUpdate()
    {
        float t = Time.time - gestureStart, duration = GestureDuration(gesture);
        if (t < 0f || t > duration || dead || hidden || head == null || !model.activeInHierarchy) return;
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
                float w = Envelope(t, 0.2f, 0.3f, duration);
                // Petites bouchées pendant le maintien.
                float chew = t > 0.2f && t < 0.5f ? Mathf.Abs(Mathf.Sin((t - 0.2f) * 21f)) : 0f;
                Tilt(head, axis, -w * 6f + chew * 4f);
                Tilt(upperArm, axis, -w * 55f);
                Tilt(lowerArm, axis, -w * 95f);
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
