using UnityEngine;

// Sons enregistrés (Assets/Sounds) qui remplacent les sons synthétisés de SoundBank portant la même clé.
// L'asset Resources/GameSounds est rempli par "FunGame/Configurer le jeu", ce qui inclut les fichiers dans le build.
[CreateAssetMenu(menuName = "FunGame/Sons")]
public class GameSounds : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string key;
        public AudioClip clip;
    }

    public Entry[] sounds = new Entry[0];

    public AudioClip Find(string key)
    {
        foreach (var e in sounds) if (e.key == key && e.clip != null) return e.clip;
        return null;
    }
}
