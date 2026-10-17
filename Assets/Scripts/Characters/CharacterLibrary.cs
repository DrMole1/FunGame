using UnityEngine;

// Liste des personnages jouables, remplie par le menu "FunGame/Installer les personnages".
public class CharacterLibrary : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public string name;
        public GameObject model;
        public RuntimeAnimatorController controller;
        public float scale = 1f;
    }

    // Hauteur du collider des joueurs (le modèle est posé à sa base).
    public float height = 1.8f;
    public float radius = 0.35f;
    public Entry[] characters = new Entry[0];

    public int Count => characters.Length;
}
