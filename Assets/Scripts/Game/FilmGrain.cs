using UnityEngine;

// Grain d'écran sur la caméra du jeu (lobby compris), réglé dans le lobby et le menu pause (0 à 100 %, 100 % par défaut).
// Dessiné juste après la scène : les interfaces (Canvas en Screen Space Overlay) passent par-dessus, sans grain.
[RequireComponent(typeof(Camera))]
public class FilmGrain : MonoBehaviour
{
    const string Key = "grain";
    // Intensité du shader à 100 % : l'image varie d'environ ±9 %.
    const float MaxIntensity = 0.18f;

    static float current = -1f;

    // 0 à 1.
    public static float Value
    {
        get
        {
            if (current < 0f) current = Mathf.Clamp01(PlayerPrefs.GetFloat(Key, 1f));
            return current;
        }
        set
        {
            current = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(Key, current);
        }
    }

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    Material material;

    void OnPostRender()
    {
        if (Value <= 0f) return;
        if (material == null) material = GameArt.FilmGrain;
        if (material == null) return;
        material.SetFloat(IntensityId, Value * MaxIntensity);
        GL.PushMatrix();
        GL.LoadOrtho();
        material.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.TexCoord2(0f, 0f); GL.Vertex3(0f, 0f, 0f);
        GL.TexCoord2(0f, 1f); GL.Vertex3(0f, 1f, 0f);
        GL.TexCoord2(1f, 1f); GL.Vertex3(1f, 1f, 0f);
        GL.TexCoord2(1f, 0f); GL.Vertex3(1f, 0f, 0f);
        GL.End();
        GL.PopMatrix();
    }
}
