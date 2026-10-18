using UnityEngine;

// Modèle animé de La Dame. L'asset Resources/DameArt est créé par "FunGame/Installer La Dame"
// à partir des fichiers Meshy de Assets/Models/Dame/ (modèle riggé, marche, course, attaque).
[CreateAssetMenu(menuName = "FunGame/La Dame")]
public class DameArt : ScriptableObject
{
    public GameObject model;
    public RuntimeAnimatorController controller;
    public float scale = 1f;
}
