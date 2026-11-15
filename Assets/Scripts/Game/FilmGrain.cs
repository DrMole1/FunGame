using System.Collections.Generic;
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
    // V0.7.5 (Note 02) : le reste de la scène est un peu moins sombre (avant : 0,02), et la zone éclairée a la même taille et la
    // même position pour tous les skins (la plus grande, mesurée une fois sur l'ensemble des skins).
    // V0.7.5 : la zone éclairée est un cylindre vertical autour du personnage (même taille quel que soit l'angle de la caméra qui
    // tourne autour de lui), couvrant tous les skins. Les skins sont mesurés un par image dès l'arrivée dans le lobby (sans
    // à-coup), et le résultat est gardé pour les lancements suivants.
    public static Transform Spotlit;
    const float SpotBrightness = 0.08f, SpotIntensity = 2.6f;
    const string SpotCacheKey = "spotSkins1_";
    // Dans le repère du personnage (centre du collider) : bas et haut des skins, et plus grand écart à l'axe vertical.
    static float skinMinY, skinMaxY, skinRadius;
    static bool hasSkinShape, shapeDone;
    static int measureIndex = -1;
    static readonly List<Vector3> bakedVertices = new List<Vector3>();
    static Mesh baked;
    float spot;
    Vector4 spotRect;
    Light spotLight;
    Camera cam;

    void Update()
    {
        dim = Mathf.MoveTowards(dim, MenuHover ? 1f : 0f, Time.unscaledDeltaTime / DimFade);
        obsession = Mathf.MoveTowards(obsession, Obsessed ? 1f : 0f, Time.deltaTime / ObsessedFade);
        spot = Mathf.MoveTowards(spot, MenuHover && Spotlit != null && Spotlit.gameObject.activeInHierarchy ? 1f : 0f, Time.unscaledDeltaTime / DimFade);
        if (Spotlit != null && !shapeDone) MeasureNextSkin(Spotlit);
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
        float minY = hasSkinShape ? skinMinY : -1f, maxY = hasSkinShape ? skinMaxY : 1.1f, radius = hasSkinShape ? skinRadius : 0.5f;
        Vector3 basePos = Spotlit.position, up = Vector3.up;
        Vector3 mid = basePos + up * ((minY + maxY) * 0.5f);
        Vector3 v0 = cam.WorldToViewportPoint(mid);
        if (v0.z > 0f)
        {
            // Hauteur : du bas au haut des skins ; largeur : le rayon du cylindre, toujours de face pour la caméra.
            Vector3 right = cam.transform.right;
            Vector3 top = cam.WorldToViewportPoint(basePos + up * maxY), bottom = cam.WorldToViewportPoint(basePos + up * minY);
            Vector3 side = cam.WorldToViewportPoint(mid + right * radius);
            float halfY = Mathf.Abs(top.y - bottom.y) * 0.5f, halfX = Mathf.Abs(side.x - v0.x);
            // Une ellipse qui couvre le cylindre, avec une marge : son bord doux commence à 55 % du rayon.
            spotRect = new Vector4(v0.x, (top.y + bottom.y) * 0.5f, halfX * 1.45f + 0.015f, halfY * 1.35f + 0.015f);
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

    // Un skin par image : posé hors de vue, en pose d'attente, comme dans CharacterAvatar, ses maillages mesurés.
    static void MeasureNextSkin(Transform player)
    {
        var library = player.GetComponent<CharacterLibrary>();
        if (library == null || library.Count == 0) { shapeDone = true; return; }
        string key = SpotCacheKey + library.Count;
        if (measureIndex < 0)
        {
            // Déjà mesurés lors d'un lancement précédent.
            string saved = PlayerPrefs.GetString(key, "");
            string[] p = saved.Split(';');
            if (p.Length == 3 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out skinMinY)
                && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out skinMaxY)
                && float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out skinRadius))
            {
                hasSkinShape = shapeDone = true;
                return;
            }
            measureIndex = 0;
        }
        if (measureIndex >= library.Count)
        {
            shapeDone = true;
            if (baked != null) Object.Destroy(baked);
            if (hasSkinShape)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                PlayerPrefs.SetString(key, skinMinY.ToString(ci) + ";" + skinMaxY.ToString(ci) + ";" + skinRadius.ToString(ci));
                PlayerPrefs.Save();
            }
            return;
        }
        var entry = library.characters[measureIndex++];
        if (entry == null || entry.model == null) return;
        if (baked == null) baked = new Mesh();
        Vector3 origin = new Vector3(0f, -5000f, 0f);
        var model = Object.Instantiate(entry.model, origin + Vector3.down * (library.height * 0.5f), Quaternion.identity);
        model.transform.localScale = Vector3.one * entry.scale;
        if (!model.TryGetComponent(out Animator animator)) animator = model.AddComponent<Animator>();
        if (entry.controller != null)
        {
            animator.runtimeAnimatorController = entry.controller;
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
        }
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (r is SkinnedMeshRenderer smr)
            {
                // BakeMesh sans l'échelle : sommets posés, à replacer avec la position et la rotation du renderer seulement.
                smr.BakeMesh(baked);
                baked.GetVertices(bakedVertices);
                Vector3 at = smr.transform.position - origin;
                Quaternion rot = smr.transform.rotation;
                foreach (var v in bakedVertices) Add(at + rot * v);
            }
            else
            {
                Bounds b = r.bounds;
                Add(b.min - origin);
                Add(b.max - origin);
            }
        }
        Object.Destroy(model);
    }

    static void Add(Vector3 p)
    {
        float radius = new Vector2(p.x, p.z).magnitude;
        if (!hasSkinShape)
        {
            skinMinY = skinMaxY = p.y;
            skinRadius = radius;
            hasSkinShape = true;
            return;
        }
        skinMinY = Mathf.Min(skinMinY, p.y);
        skinMaxY = Mathf.Max(skinMaxY, p.y);
        skinRadius = Mathf.Max(skinRadius, radius);
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
