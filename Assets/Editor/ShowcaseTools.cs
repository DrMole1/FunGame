using System.IO;
using UnityEngine;
using UnityEngine.UI;

// Outils de mise en scène pour les visuels promotionnels (dossier Showcase à la racine du projet), utilisés en mode Play :
// figurants (autres joueurs), lumières d'appoint, capture haute définition d'un point de vue, habillage d'affiche.
public static class ShowcaseTools
{
    public static readonly string Folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Showcase"));

    // Figurant : un personnage animé (skin 0 à 3) à une position, tourné vers 'yaw', dans l'état d'animation voulu.
    public static RemotePlayer Extra(string name, int skin, Vector3 feet, float yaw, AnimState anim, Color tagColor)
    {
        var library = NetworkGame.Instance.LocalPlayer.GetComponent<CharacterLibrary>();
        Vector3 center = feet + Vector3.up * library.height * 0.5f;
        var rp = RemotePlayer.Create("vitrine-" + name, name, tagColor, library, skin, center);
        rp.SetTarget(Time.realtimeSinceStartupAsDouble, center, yaw, anim, 100, 0);
        return rp;
    }

    public static Light AddLight(Vector3 pos, Color color, float intensity, float range)
    {
        var l = new GameObject("Vitrine_Lumiere").AddComponent<Light>();
        l.transform.position = pos;
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;
        return l;
    }

    public static Camera MakeCamera(Vector3 pos, Vector3 lookAt, float fov)
    {
        var cam = new GameObject("Vitrine_Camera").AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(lookAt);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 80f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.enabled = false;
        return cam;
    }

    // Rendu de la caméra en 'width' x 'height' (suréchantillonné x2 puis réduit) enregistré en PNG dans Showcase.
    public static string Capture(Camera cam, int width, int height, string fileName)
    {
        Directory.CreateDirectory(Folder);
        var big = new RenderTexture(width * 2, height * 2, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = big;
        cam.aspect = (float)width / height;
        cam.Render();
        var small = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(big, small);
        RenderTexture.active = small;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        string path = Path.Combine(Folder, fileName);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(small);
        Object.Destroy(big);
        Object.Destroy(tex);
        return path;
    }

    // ---------- Affiches ----------

    // Habillage d'affiche rendu par la caméra : dégradés sombres, titre sur un coup de pinceau rouge, accroche, mentions.
    public static GameObject PosterOverlay(Camera cam, string tagline, string footer, Color brush)
    {
        var go = new GameObject("Vitrine_Affiche", typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.2f;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1000, 1500);
        scaler.matchWidthOrHeight = 1f;
        Transform root = go.transform;

        Gradient(root, true, 0.42f, 0.92f);
        Gradient(root, false, 0.34f, 0.95f);

        // Coup de pinceau derrière le titre, puis le titre.
        var stroke = UiKit.Panel(root, "Pinceau", new Vector2(970, 160), brush);
        UiKit.Paint(stroke, brush, 41);
        Anchor(stroke.transform, new Vector2(0.5f, 1f), new Vector2(0, -150));
        var title = Text(root, "PAVLOV'S THEORY", 92, Color.white, new Vector2(960, 150));
        Anchor(title.transform, new Vector2(0.5f, 1f), new Vector2(0, -150));
        title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);

        PosterCaptions(root, tagline, footer);
        return go;
    }

    // Accroche (sous le titre) et mentions (en bas) d'une affiche, avec un contour noir pour rester lisibles.
    static void PosterCaptions(Transform root, string tagline, string footer)
    {
        var tag = Text(root, tagline, 38, new Color(0.92f, 0.92f, 0.95f), new Vector2(900, 120));
        Anchor(tag.transform, new Vector2(0.5f, 1f), new Vector2(0, -330));
        Contour(tag);
        var foot = Text(root, footer, 26, new Color(0.85f, 0.85f, 0.88f), new Vector2(940, 110));
        Anchor(foot.transform, new Vector2(0.5f, 0f), new Vector2(0, 80));
        Contour(foot);
    }

    // Accroche d'une bannière, avec un contour noir.
    static void BannerCaption(Transform root, string tagline)
    {
        var tag = Text(root, tagline, 40, new Color(0.92f, 0.92f, 0.95f), new Vector2(1000, 120));
        Anchor(tag.transform, new Vector2(0f, 0.5f), new Vector2(70, -80), new Vector2(0f, 0.5f));
        tag.alignment = TextAnchor.UpperCenter;
        Contour(tag);
    }

    // Contour noir : douze copies noires du texte, décalées tout autour, dessinées derrière lui.
    static void Contour(Text t)
    {
        const float d = 4f;
        int index = t.transform.GetSiblingIndex();
        for (int k = 0; k < 12; k++)
        {
            float a = k * Mathf.PI / 6f;
            var copy = Object.Instantiate(t.gameObject, t.transform.parent);
            copy.name = "Contour";
            var ct = copy.GetComponent<Text>();
            ct.color = Color.black;
            ct.rectTransform.anchoredPosition = t.rectTransform.anchoredPosition + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            copy.transform.SetSiblingIndex(index);
        }
    }

    // Repose les accroches et mentions (avec contour) sur une image déjà capturée : mêmes textes, mêmes positions,
    // le titre et la scène restent tels quels. 'banner' : mise en page de la bannière 16:9.
    public static string RetouchCaptions(string fileName, bool banner, string tagline, string footer)
    {
        string path = Path.Combine(Folder, fileName);
        var background = new Texture2D(2, 2);
        background.LoadImage(File.ReadAllBytes(path));
        int width = background.width, height = background.height;

        var cam = MakeCamera(new Vector3(0f, -900f, 0f), new Vector3(0f, -900f, 1f), 60f);
        cam.cullingMask = 1 << 5;
        var go = new GameObject("Vitrine_Retouche", typeof(Canvas), typeof(CanvasScaler));
        go.layer = 5;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.2f;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = banner ? new Vector2(1920, 1080) : new Vector2(1000, 1500);
        scaler.matchWidthOrHeight = 1f;
        var bg = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
        bg.transform.SetParent(go.transform, false);
        var rt = (RectTransform)bg.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        bg.GetComponent<RawImage>().texture = background;
        if (banner) BannerCaption(go.transform, tagline);
        else PosterCaptions(go.transform, tagline, footer);
        foreach (Transform t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = 5;

        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = target;
        cam.aspect = (float)width / height;
        // Premier rendu : la police dynamique ajoute les caractères à son atlas ; le second rendu est le bon.
        for (int pass = 0; pass < 2; pass++)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var text in go.GetComponentsInChildren<Text>()) text.SetAllDirty();
            Canvas.ForceUpdateCanvases();
            cam.Render();
        }
        RenderTexture.active = target;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(background);
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(cam.gameObject);
        return path;
    }

    // Bannière horizontale (vignette 16:9) : titre à gauche sur pinceau, accroche dessous.
    public static GameObject BannerOverlay(Camera cam, string tagline, Color brush)
    {
        var go = new GameObject("Vitrine_Banniere", typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.2f;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        Transform root = go.transform;
        var side = new GameObject("Degrade", typeof(RectTransform), typeof(RawImage));
        side.transform.SetParent(root, false);
        var rt = (RectTransform)side.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0.6f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = side.GetComponent<RawImage>();
        img.texture = GradientTexture(true);
        img.color = new Color(0f, 0f, 0f, 0.85f);
        var stroke = UiKit.Panel(root, "Pinceau", new Vector2(1000, 170), brush);
        UiKit.Paint(stroke, brush, 43);
        Anchor(stroke.transform, new Vector2(0f, 0.5f), new Vector2(70, 80), new Vector2(0f, 0.5f));
        var title = Text(root, "PAVLOV'S THEORY", 92, Color.white, new Vector2(1000, 170));
        Anchor(title.transform, new Vector2(0f, 0.5f), new Vector2(70, 80), new Vector2(0f, 0.5f));
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        BannerCaption(root, tagline);
        return go;
    }

    static Text Text(Transform parent, string text, int size, Color color, Vector2 box)
    {
        var t = UiKit.Label(parent, text, size, FontStyle.Bold, color, box.y);
        t.rectTransform.sizeDelta = box;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        return t;
    }

    static void Anchor(Transform t, Vector2 anchor, Vector2 pos, Vector2? pivot = null)
    {
        var rt = (RectTransform)t;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? new Vector2(0.5f, anchor.y);
        rt.anchoredPosition = pos;
    }

    static void Gradient(Transform parent, bool top, float height, float alpha)
    {
        var go = new GameObject(top ? "DegradeHaut" : "DegradeBas", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, top ? 1f - height : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<RawImage>();
        img.texture = GradientTexture(false);
        img.color = new Color(0f, 0f, 0f, alpha);
        if (top) rt.localRotation = Quaternion.Euler(180f, 0f, 0f);
    }

    // Dégradé opaque -> transparent (vertical : bas opaque ; horizontal : gauche opaque).
    static Texture2D GradientTexture(bool horizontal)
    {
        const int n = 64;
        var tex = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int i = 0; i < n; i++)
        {
            float a = 1f - Mathf.SmoothStep(0f, 1f, i / (float)(n - 1));
            if (horizontal) tex.SetPixel(i, 0, new Color(1f, 1f, 1f, a));
            else tex.SetPixel(0, i, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    // Nettoie la mise en scène (figurants, lumières, caméras, habillages).
    public static void Clear()
    {
        foreach (var rp in Object.FindObjectsOfType<RemotePlayer>())
            if (rp.PlayerId != null && rp.PlayerId.StartsWith("vitrine-")) Object.Destroy(rp.gameObject);
        foreach (var go in Object.FindObjectsOfType<GameObject>())
            if (go.name.StartsWith("Vitrine_")) Object.Destroy(go);
    }
}
