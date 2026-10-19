using System.Collections;
using UnityEngine;

public enum AnimState : byte { Idle, Walk, Run, Jump, Sneak, SneakIdle }

// Modèle 3D animé d'un joueur (local ou distant), enfant du GameObject physique.
public class CharacterAvatar : MonoBehaviour
{
    // Le clip de saut décolle vers 0,45 s : on saute l'élan, la physique gère l'impulsion.
    const float JumpClipOffset = 0.45f;
    // Marche discrète à l'arrêt : posture baissée figée sur une image du clip où les deux pieds sont au sol.
    const float SneakIdlePose = 2.12f;
    const float DeathDuration = 1.1f;

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

        CharacterIndex = index;
        State = AnimState.Idle;
        animator.Play("Idle", 0, 0f);
        model.SetActive(!dead);
    }

    public void SetState(AnimState state)
    {
        if (state == State || animator == null) return;
        State = state;
        // Le clip de saut suit la durée raccourcie du saut.
        animator.speed = state == AnimState.Jump ? 1f / GameRules.JumpTimeScale : 1f;
        if (state == AnimState.Jump)
            animator.CrossFadeInFixedTime("Jump", 0.1f, 0, JumpClipOffset);
        else if (state == AnimState.SneakIdle)
            animator.CrossFadeInFixedTime("SneakIdle", 0.25f, 0, SneakIdlePose);
        else
            animator.CrossFadeInFixedTime(state.ToString(), state == AnimState.Sneak ? 0.25f : 0.15f);
    }

    // Mort : le personnage bascule, rétrécit et disparaît.
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

    IEnumerator DeathAnimation()
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
