using UnityEngine;

// Modèle animé d'une entité Meshy (La Tour, La Cloche). Les assets Resources/<Nom>Art sont créés par
// "FunGame/Installer les entités V0.4" à partir des fichiers de Assets/Models/<Nom>/.
[CreateAssetMenu(menuName = "FunGame/Entité animée")]
public class CreatureArt : ScriptableObject
{
    public GameObject model;
    public RuntimeAnimatorController controller;
    public float scale = 1f;
}
