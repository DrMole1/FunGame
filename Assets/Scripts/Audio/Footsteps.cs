using UnityEngine;

// Bruits de pas d'un personnage (local ou distant), selon son allure et le sol (terre du chapiteau ou damier).
public class Footsteps : MonoBehaviour
{
    CharacterAvatar avatar;
    PlayerPresence presence;
    AnimState last;
    float timer;

    void Awake()
    {
        avatar = GetComponent<CharacterAvatar>();
        presence = GetComponent<PlayerPresence>();
    }

    void Update()
    {
        if (avatar == null || (presence != null && !presence.Alive)) return;
        AnimState state = avatar.State;
        // Réception d'un saut.
        if (last == AnimState.Jump && state != AnimState.Jump) Step(0.8f, 0.85f);
        last = state;

        float interval, volume;
        switch (state)
        {
            case AnimState.Walk: interval = 0.42f; volume = 0.4f; break;
            case AnimState.Run: interval = 0.27f; volume = 0.6f; break;
            // Un pas toutes les 0,94 s : cadence de l'animation de marche discrète.
            case AnimState.Sneak: interval = 0.94f; volume = 0.12f; break;
            default: timer = 0.12f; return;
        }
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer += interval;
        Step(volume, 1f);
    }

    void Step(float volume, float pitch)
    {
        Vector3 feet = transform.position + Vector3.down * 0.85f;
        GameAudio.Play3D(WorldBuilder.IsInHub(feet) ? "step_dirt" : "step_tile", feet, volume, 18f, 0.12f, pitch);
    }
}
