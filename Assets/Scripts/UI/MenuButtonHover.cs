using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Survol d'un bouton des menus (lobby et pause) : petit son et légère aura beige sur le contour du coup de pinceau.
// Un bouton sélectionné (drapeau de la langue choisie) garde l'aura en permanence.
public class MenuButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    static readonly Color AuraBeige = new Color(0.96f, 0.86f, 0.68f, 0.55f);

    Button button;
    Outline aura;
    bool hovered, selected;

    public static void AddTo(GameObject root)
    {
        foreach (var b in root.GetComponentsInChildren<Button>(true))
            if (b.GetComponent<MenuButtonHover>() == null) b.gameObject.AddComponent<MenuButtonHover>();
    }

    public bool Selected
    {
        get => selected;
        set { selected = value; Refresh(); }
    }

    // Bouton à image colorée (drapeau) : l'Outline reprendrait les couleurs de l'image, l'aura est donc un cadre beige uni
    // placé derrière le bouton.
    Graphic frame;

    public void UseFrame(Graphic beigeFrame)
    {
        frame = beigeFrame;
        frame.color = new Color(AuraBeige.r, AuraBeige.g, AuraBeige.b, 1f);
        aura.enabled = false;
        Refresh();
    }

    void Awake()
    {
        button = GetComponent<Button>();
        aura = gameObject.AddComponent<Outline>();
        aura.effectColor = AuraBeige;
        aura.effectDistance = new Vector2(3f, -3f);
        aura.useGraphicAlpha = true;
        Refresh();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (!button.interactable) return;
        hovered = true;
        Refresh();
        GameAudio.Play2D("tick", 0.35f, 0.05f);
    }

    public void OnPointerExit(PointerEventData e)
    {
        hovered = false;
        Refresh();
    }

    // Menu fermé pendant le survol : l'aura de survol ne reste pas allumée à la réouverture.
    void OnDisable()
    {
        hovered = false;
        Refresh();
    }

    void Refresh()
    {
        bool on = hovered || selected;
        if (frame != null) frame.enabled = on;
        else if (aura != null) aura.enabled = on;
    }
}
