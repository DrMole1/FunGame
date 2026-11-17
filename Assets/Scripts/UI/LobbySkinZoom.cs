using UnityEngine;
using UnityEngine.EventSystems;

// Menu du lobby uniquement (V0.7.10) : survoler la flèche gauche ou droite du choix du skin zoome progressivement sur le
// personnage ; en quittant la flèche, la caméra retrouve rapidement son champ de vision (OrbitCamera.UpdateLens).
public class LobbySkinZoom : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    static int hovered;
    bool over;

    public static bool Active => hovered > 0;

    public void OnPointerEnter(PointerEventData eventData) => Set(true);
    public void OnPointerExit(PointerEventData eventData) => Set(false);
    // Bouton masqué pendant le survol (lancement de la partie) : le zoom ne doit pas rester enclenché.
    void OnDisable() => Set(false);

    void Set(bool on)
    {
        if (on == over) return;
        over = on;
        hovered += on ? 1 : -1;
    }
}
