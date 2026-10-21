using UnityEngine;

// Visée au centre de l'écran, interactions (E / clic gauche) et sélection d'inventaire (1 à 5).
public class PlayerInteractor : MonoBehaviour
{
    static readonly KeyCode[] SlotKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
    };
    static readonly KeyCode[] PadKeys =
    {
        KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5,
    };

    PlayerStatus status;
    Interactable current;

    public Interactable Current => current;

    void Awake() => status = GetComponent<PlayerStatus>();

    void OnDisable() => SetCurrent(null);

    void Update()
    {
        GameSession session = GameSession.Instance;
        // Dans le Canon, ni ramasser, ni poser, ni interagir : le clic tire.
        bool active = session != null && !PlayerController.InputLocked && !status.Dead && !GameSession.InCannon;
        SetCurrent(active ? FindTarget() : null);
        if (!active) return;

        for (int i = 0; i < SlotKeys.Length; i++)
            if (Input.GetKeyDown(SlotKeys[i]) || Input.GetKeyDown(PadKeys[i]))
                status.Select(i);

        bool click = Input.GetMouseButtonDown(0) && OrbitCamera.Looking && Time.frameCount != OrbitCamera.LockFrame;
        bool use = Input.GetKeyDown(KeyCode.E);
        if ((click || use) && current != null)
        {
            // Interagir n'utilise pas l'objet sélectionné dans l'inventaire (Pogo et la Balise du Hub le lisent au moment du clic).
            current.Interact();
        }
        else if (click && status[status.Selected] != ItemType.None)
        {
            session.UseSelectedItem();
        }
    }

    Interactable FindTarget()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, 14f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            return null;
        var it = hit.collider.GetComponentInParent<Interactable>();
        if (it == null || !it.CanInteract) return null;
        Vector3 pos = transform.position;
        float dist = Vector3.Distance(hit.collider.ClosestPoint(pos), pos);
        return dist <= GameRules.InteractRange ? it : null;
    }

    void SetCurrent(Interactable it)
    {
        if (it == current) return;
        if (current != null) current.SetHighlight(false);
        current = it;
        if (current != null) current.SetHighlight(true);
    }
}
