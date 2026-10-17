using UnityEngine;

// Personnage d'un autre joueur : suit en douceur les positions, l'animation et l'état reçus du réseau.
public class RemotePlayer : MonoBehaviour
{
    public string PlayerId { get; private set; }
    public string PlayerName { get; private set; }
    public int Hp { get; private set; } = GameRules.MaxHp;
    public bool Dead => presence.Alive == false;

    Vector3 targetPos;
    float targetYaw;
    bool hasTarget;
    CharacterAvatar avatar;
    PlayerPresence presence;
    NameTag nameTag;
    Collider body;

    public static RemotePlayer Create(string id, string playerName, Color color, CharacterLibrary library, int character, Vector3 position)
    {
        var go = new GameObject("Remote_" + playerName);
        go.layer = 2; // Ignore Raycast : ne bloque ni la visée ni la caméra
        go.transform.position = position;

        // Cinématique : les autres joueurs sont des obstacles qui poussent le joueur local.
        var rp = go.AddComponent<RemotePlayer>();
        rp.body = CharacterAvatar.EnsureCapsule(go, library);
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        rp.PlayerId = id;
        rp.PlayerName = playerName;
        rp.targetPos = position;
        rp.avatar = go.AddComponent<CharacterAvatar>();
        rp.avatar.SetCharacter(library, character);
        rp.presence = go.AddComponent<PlayerPresence>();
        rp.presence.PlayerId = id;

        rp.nameTag = go.AddComponent<NameTag>();
        rp.nameTag.height = library.height * 0.5f + 0.35f;
        rp.nameTag.Set(playerName, color);
        return rp;
    }

    public void SetCharacter(CharacterLibrary library, int character) => avatar.SetCharacter(library, character);

    public void SetTarget(Vector3 pos, float yaw, AnimState anim, int hp, int flags)
    {
        targetPos = pos;
        targetYaw = yaw;
        Hp = hp;
        bool dead = (flags & NetworkGame.FlagDead) != 0;
        presence.Sneaking = (flags & NetworkGame.FlagSneak) != 0;
        if (dead != Dead)
        {
            presence.Alive = !dead;
            avatar.SetDead(dead);
            nameTag.Show(!dead);
            body.enabled = !dead;
        }
        if (!dead) avatar.SetState(anim);
        if (!hasTarget)
        {
            hasTarget = true;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }
    }

    void Update()
    {
        if (!hasTarget) return;
        if ((transform.position - targetPos).sqrMagnitude > 25f)
        {
            transform.position = targetPos;
        }
        else
        {
            float k = 1f - Mathf.Exp(-15f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, k);
        }
        float yk = 1f - Mathf.Exp(-12f * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), yk);
    }
}
