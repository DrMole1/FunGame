using UnityEngine;
using UnityEngine.EventSystems;

// Caméra à la troisième personne qui tourne sans limite autour de la cible avec la souris.
// Molette : distance entre 2 et 5 unités. Elle ne passe jamais sous le personnage ni à travers les murs.
public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 3.5f;
    public float height = 1f;
    public float mouseSensitivity = 3f;
    public float minPitch = -15f;
    public float maxPitch = 75f;

    // Désactivé pendant le lobby : la caméra tourne lentement et la souris reste libre.
    public static bool ControlsEnabled = true;
    // Bloque le verrouillage de la souris (Terminal ouvert).
    public static bool CursorLockBlocked;
    // Image où la souris vient d'être verrouillée : ce clic ne doit pas aussi déclencher d'action.
    public static int LockFrame = -1;

    public static bool Looking => ControlsEnabled && Cursor.lockState == CursorLockMode.Locked;

    float yaw;
    float pitch = 20f;
    float currentDistance;

    void Start()
    {
        yaw = transform.eulerAngles.y;
        currentDistance = distance;
    }

    void Update()
    {
        // Échap libère la souris, un clic la reverrouille.
        if (!ControlsEnabled || CursorLockBlocked)
        {
            if (Cursor.lockState != CursorLockMode.None) LockCursor(false);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && !IsPointerOverUI())
        {
            LockCursor(true);
            LockFrame = Time.frameCount;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
                distance = Mathf.Clamp(distance - wheel * 0.5f, GameRules.CameraMinDistance, GameRules.CameraMaxDistance);
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (!ControlsEnabled)
            yaw += 6f * Time.deltaTime;
        else if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }
        yaw = Mathf.Repeat(yaw, 360f);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = target.position + Vector3.up * height;
        float wanted = ControlsEnabled ? distance : 5f;

        // Les murs rapprochent la caméra au lieu d'être traversés.
        float d = wanted;
        if (Physics.SphereCast(focus, 0.2f, -(rot * Vector3.forward), out RaycastHit hit, wanted,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            d = Mathf.Max(0.3f, hit.distance);
        currentDistance = d < currentDistance ? d : Mathf.Lerp(currentDistance, d, 1f - Mathf.Exp(-8f * Time.deltaTime));

        Vector3 pos = focus - rot * Vector3.forward * currentDistance;
        // Jamais sous le personnage (le pivot est au centre de la capsule).
        pos.y = Mathf.Max(pos.y, target.position.y);
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(focus - pos);
    }

    public float CurrentDistance => currentDistance;

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    public static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
