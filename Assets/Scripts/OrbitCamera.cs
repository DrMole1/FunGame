using UnityEngine;
using UnityEngine.EventSystems;

// Caméra à la troisième personne qui tourne autour de la cible avec la souris.
public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 7f;
    public float height = 1f;
    public float mouseSensitivity = 3f;
    public float minPitch = -20f;
    public float maxPitch = 70f;

    // Désactivé pendant le lobby : la caméra tourne lentement et la souris reste libre.
    public static bool ControlsEnabled = true;

    float yaw;
    float pitch = 20f;

    void Start()
    {
        yaw = transform.eulerAngles.y;
    }

    void Update()
    {
        // Échap libère la souris, un clic la reverrouille.
        if (!ControlsEnabled)
        {
            if (Cursor.lockState != CursorLockMode.None) LockCursor(false);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
        else if (Input.GetMouseButtonDown(0) && !IsPointerOverUI()) LockCursor(true);
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

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = target.position + Vector3.up * height;
        transform.position = focus - rot * Vector3.forward * distance;
        transform.rotation = rot;
    }

    static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
