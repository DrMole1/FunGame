using UnityEngine;

// Visée au centre de l'écran, interactions (E / clic gauche) et sélection d'inventaire (1 à 5), touches modifiables (Controls).
public class PlayerInteractor : MonoBehaviour
{
    static readonly GameAction[] SlotActions =
    {
        GameAction.Slot1, GameAction.Slot2, GameAction.Slot3, GameAction.Slot4, GameAction.Slot5,
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

        for (int i = 0; i < SlotActions.Length; i++)
            if (Controls.Down(SlotActions[i]))
                status.Select(i);

        // "Utiliser l'objet" (clic gauche par défaut) interagit ou utilise l'objet ; "Interagir" (E) interagit seulement.
        bool click = Controls.Pressed(GameAction.Use);
        bool use = Controls.Pressed(GameAction.Interact);
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
