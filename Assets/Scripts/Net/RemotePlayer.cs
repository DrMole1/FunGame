using UnityEngine;

// Cube d'un autre joueur : suit en douceur les positions reçues du réseau.
public class RemotePlayer : MonoBehaviour
{
    public string PlayerId { get; private set; }

    Vector3 targetPos;
    float targetYaw;
    bool hasTarget;

    public static RemotePlayer Create(string id, string playerName, Color color, Material template, Vector3 position)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Remote_" + playerName;
        go.transform.position = position;

        var rend = go.GetComponent<Renderer>();
        rend.material = new Material(template) { color = color };

        // Cinématique : les autres joueurs sont des obstacles qui poussent le cube local.
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        var rp = go.AddComponent<RemotePlayer>();
        rp.PlayerId = id;
        rp.targetPos = position;
        go.AddComponent<NameTag>().Set(playerName, color);
        return rp;
    }

    public void SetTarget(Vector3 pos, float yaw)
    {
        targetPos = pos;
        targetYaw = yaw;
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
