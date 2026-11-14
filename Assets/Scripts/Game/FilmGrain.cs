using UnityEngine;

// Grain d'écran sur la caméra du jeu (lobby compris), réglé dans le lobby et le menu pause (0 à 100 %, 100 % par défaut).
// Dessiné juste après la scène : les interfaces (Canvas en Screen Space Overlay) passent par-dessus, sans grain.
// La même passe assombrit progressivement la scène quand la souris survole le menu du lobby ou le menu pause.
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
            // Écrit tout de suite : dans le navigateur, seul PlayerPrefs.Save() enregistre le réglage pour la prochaine ouverture du jeu.
            PlayerPrefs.Save();
        }
    }

    // Souris sur le menu du lobby ou le menu pause (mis à jour par LobbyUI).
    public static bool MenuHover;
    // Luminosité de la scène quand la souris est sur un menu, et durée du fondu (aller comme retour).
    const float MenuBrightness = 0.15f;
    const float DimFade = 0.5f;
    float dim;

    // Obnubilation (chapitre 3.4, V0.6.9) : le grain monte en 2 secondes jusqu'à 300 % du réglage du joueur, puis redescend de même.
    public static bool Obsessed;
    const float ObsessedGrain = 3f, ObsessedFade = 2f;
    float obsession;

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    static readonly int SpotId = Shader.PropertyToID("_Spot"), SpotAmountId = Shader.PropertyToID("_SpotAmount");
    Material material;

    // V0.7.4 (Note 04) : souris sur le menu du lobby, la scène devient noire sauf le personnage (pour bien le voir en changeant de
    // skin), éclairé d'en haut par un projecteur. Mis à jour par LobbyUI.
    public static Transform Spotlit;
    const float SpotBrightness = 0.02f, SpotIntensity = 2.6f;
    float spot;
    Vector4 spotRect;
    Light spotLight;
    Camera cam;

    void Update()
    {
        dim = Mathf.MoveTowards(dim, MenuHover ? 1f : 0f, Time.unscaledDeltaTime / DimFade);
        obsession = Mathf.MoveTowards(obsession, Obsessed ? 1f : 0f, Time.deltaTime / ObsessedFade);
        spot = Mathf.MoveTowards(spot, MenuHover && Spotlit != null && Spotlit.gameObject.activeInHierarchy ? 1f : 0f, Time.unscaledDeltaTime / DimFade);
        UpdateSpot();
    }

    // Ellipse à l'écran autour du personnage (des pieds au-dessus de la tête), et projecteur au-dessus de lui.
    void UpdateSpot()
    {
        if (spot <= 0f || Spotlit == null)
        {
            if (spotLight != null) spotLight.enabled = false;
            return;
        }
        if (cam == null) cam = GetComponent<Camera>();
        // Boîte englobante des maillages visibles du personnage (le skin choisi), projetée à l'écran.
        bool any = false;
        Bounds body = default;
        foreach (var r in Spotlit.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
            if (!any) { body = r.bounds; any = true; }
            else body.Encapsulate(r.bounds);
        }
        if (!any) body = new Bounds(Spotlit.position, new Vector3(0.8f, 2f, 0.8f));
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        bool visible = false;
        for (int k = 0; k < 8; k++)
        {
            Vector3 corner = body.center + Vector3.Scale(body.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
            Vector3 v = cam.WorldToViewportPoint(corner);
            if (v.z <= 0f) continue;
            visible = true;
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }
        if (visible)
        {
            // Une ellipse qui couvre la boîte, avec une marge : son bord doux commence à 55 % du rayon.
            Vector2 half = (max - min) * 0.5f;
            spotRect = new Vector4((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, half.x * 1.45f + 0.015f, half.y * 1.35f + 0.015f);
        }
        if (spotLight == null)
        {
            spotLight = new GameObject("ProjecteurLobby").AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.color = new Color(1f, 0.95f, 0.85f);
            spotLight.range = 9f;
            spotLight.spotAngle = 38f;
            spotLight.innerSpotAngle = 20f;
            spotLight.shadows = LightShadows.Soft;
        }
        spotLight.enabled = true;
        spotLight.transform.SetPositionAndRotation(Spotlit.position + Vector3.up * 5f, Quaternion.Euler(90f, 0f, 0f));
        spotLight.intensity = SpotIntensity * Mathf.SmoothStep(0f, 1f, spot);
    }

    void OnPostRender()
    {
        if (Value <= 0f && dim <= 0f) return;
        if (material == null) material = GameArt.FilmGrain;
        if (material == null) return;
        material.SetFloat(IntensityId, Value * MaxIntensity * Mathf.Lerp(1f, ObsessedGrain, obsession));
        float k = Mathf.SmoothStep(0f, 1f, dim);
        material.SetFloat(BrightnessId, Mathf.Lerp(1f, Mathf.Lerp(MenuBrightness, SpotBrightness, spot), k));
        material.SetVector(SpotId, spotRect);
        material.SetFloat(SpotAmountId, Mathf.SmoothStep(0f, 1f, spot));
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
