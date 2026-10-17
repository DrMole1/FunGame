using UnityEngine;

// Personnage d'un autre joueur : suit en douceur les positions et l'animation reçues du réseau.
public class RemotePlayer : MonoBehaviour
{
    public string PlayerId { get; private set; }

    Vector3 targetPos;
    float targetYaw;
    bool hasTarget;
    CharacterAvatar avatar;

    public static RemotePlayer Create(string id, string playerName, Color color, CharacterLibrary library, int character, Vector3 position)
    {
        var go = new GameObject("Remote_" + playerName);
        go.transform.position = position;

        // Cinématique : les autres joueurs sont des obstacles qui poussent le joueur local.
        CharacterAvatar.EnsureCapsule(go, library);
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        var rp = go.AddComponent<RemotePlayer>();
        rp.PlayerId = id;
        rp.targetPos = position;
        rp.avatar = go.AddComponent<CharacterAvatar>();
        rp.avatar.SetCharacter(library, character);

        var tag = go.AddComponent<NameTag>();
        tag.height = library.height * 0.5f + 0.35f;
        tag.Set(playerName, color);
        return rp;
    }

    public void SetTarget(Vector3 pos, float yaw, AnimState anim)
    {
        targetPos = pos;
        targetYaw = yaw;
        avatar.SetState(anim);
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
