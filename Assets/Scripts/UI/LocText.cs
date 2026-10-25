using UnityEngine;
using UnityEngine.UI;

// Texte d'interface traduit (clé française), mis à jour quand la langue change.
public class LocText : MonoBehaviour
{
    public Text text;
    public string key;

    public void Refresh()
    {
        if (text != null) text.text = Loc.T(key);
    }
}
