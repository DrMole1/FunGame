using System.Collections.Generic;
using UnityEngine;

// Drapeaux des langues (Royaume-Uni, France, Espagne, Russie), dessinés par le code au format 3:2.
public static class Flags
{
    const int W = 96, H = 64;
    static readonly Dictionary<Lang, Sprite> cache = new Dictionary<Lang, Sprite>();

    public static Sprite Get(Lang lang)
    {
        if (cache.TryGetValue(lang, out var s) && s != null) return s;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                px[y * W + x] = Pixel(lang, x + 0.5f, y + 0.5f);
        tex.SetPixels32(px);
        tex.Apply();
        return cache[lang] = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
    }

    static readonly Color32 White = new Color32(255, 255, 255, 255);

    static Color32 Pixel(Lang lang, float x, float y)
    {
        float u = x / W, v = y / H; // v = 0 en bas
        switch (lang)
        {
            case Lang.Fr:
                return u < 1f / 3f ? new Color32(0, 35, 149, 255) : u < 2f / 3f ? White : new Color32(237, 41, 57, 255);
            case Lang.Es:
                return v < 0.25f || v >= 0.75f ? new Color32(170, 21, 27, 255) : new Color32(241, 191, 0, 255);
            case Lang.Ru:
                return v >= 2f / 3f ? White : v >= 1f / 3f ? new Color32(0, 57, 166, 255) : new Color32(213, 43, 30, 255);
            default:
                return UnionJack(x, y);
        }
    }

    // Union Jack simplifié : diagonales blanches et rouges, croix blanche puis croix rouge sur fond bleu.
    static Color32 UnionJack(float x, float y)
    {
        var blue = new Color32(1, 33, 105, 255);
        var red = new Color32(200, 16, 46, 255);
        float cx = x - W * 0.5f, cy = y - H * 0.5f;
        if (Mathf.Abs(cx) < H * 0.1f || Mathf.Abs(cy) < H * 0.1f) return red;
        if (Mathf.Abs(cx) < H * 0.167f || Mathf.Abs(cy) < H * 0.167f) return White;
        float len = Mathf.Sqrt(W * W + H * H);
        float d1 = (y * W - x * H) / len;           // diagonale bas-gauche → haut-droite
        float d2 = ((H - y) * W - x * H) / len;     // diagonale haut-gauche → bas-droite
        // Bande blanche le long de chaque diagonale, rouge d'un seul côté (contre-changé, en tourniquet).
        float d = cx * cy > 0f ? d1 : d2;
        if (Mathf.Abs(d) >= H * 0.1f) return blue;
        bool redSide = cy > 0f ? d > 0f : d < 0f;
        return redSide && Mathf.Abs(d) < H * 0.067f ? red : White;
    }
}
