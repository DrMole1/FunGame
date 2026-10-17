using UnityEngine;

public enum AnimState : byte { Idle, Walk, Run, Jump }

// Modèle 3D animé d'un joueur (local ou distant), enfant du GameObject physique.
public class CharacterAvatar : MonoBehaviour
{
    // Le clip de saut décolle vers 0,45 s : on saute l'élan, la physique gère l'impulsion.
    const float JumpClipOffset = 0.45f;

    public int CharacterIndex { get; private set; } = -1;
    public AnimState State { get; private set; }

    Animator animator;
    GameObject model;

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
        model.transform.localPosition = Vector3.down * (library.height * 0.5f);
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * entry.scale;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);

        if (!model.TryGetComponent(out animator)) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = entry.controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        CharacterIndex = index;
        State = AnimState.Idle;
        animator.Play("Idle", 0, 0f);
    }

    public void SetState(AnimState state)
    {
        if (state == State || animator == null) return;
        State = state;
        if (state == AnimState.Jump)
            animator.CrossFadeInFixedTime("Jump", 0.1f, 0, JumpClipOffset);
        else
            animator.CrossFadeInFixedTime(state.ToString(), 0.15f);
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
}
