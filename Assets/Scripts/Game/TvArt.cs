using UnityEngine;

// V0.7.19 (7.4) : images de la cloche qui balance (Assets/Sprite/Cloche_Balance), affichées par une Télévision chargée.
// L'asset Resources/TvArt les référence (elles restent à leur place dans le projet).
[CreateAssetMenu(menuName = "FunGame/TvArt")]
public class TvArt : ScriptableObject
{
    public Texture2D[] bellFrames;

    static TvArt cached;
    static bool loaded;

    public static TvArt Get()
    {
        if (!loaded)
        {
            loaded = true;
            cached = Resources.Load<TvArt>("TvArt");
        }
        return cached;
    }
}
