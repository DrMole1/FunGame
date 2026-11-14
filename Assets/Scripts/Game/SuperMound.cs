using System.Collections.Generic;
using UnityEngine;

// Super Monticule (V0.7.2, chapitre 10.2) : un tas de terre 40 % plus gros qu'un monticule, sur lequel on peut monter. Des
// traces de pattes de chien y sont incrustées (autant que d'icônes sur un glyphe neutre : chapitre 16.1, Calcul des Icônes),
// d'une faible lueur blanche. Un joueur qui maintient le clic dessus allume une trace (son bras droit se tend vers l'avant) ;
// toutes allumées en même temps, les joueurs creusent ensemble et déterrent un objet (Trampoline, Boule de Disco,
// Télévision ou peluche). Modèles Meshy : Resources/Art/SuperMonticule1 à SuperMonticule4 (une à quatre traces).
public class SuperMound : Interactable
{
    public int Id { get; private set; }
    public ItemType Loot { get; private set; }
    public bool Digging { get; private set; }
    public int PawCount => paws.Count;

    // Disposition de secours des traces sur le dessus (fractions de la demi-largeur), comme sur le schéma du GDD ; avec les
    // modèles Meshy, les traces lumineuses se posent dans les empreintes creusées du modèle (DetectPrints).
    static readonly Vector2[][] Layouts =
    {
        new[] { Vector2.zero },
        new[] { new Vector2(-0.05f, 0.42f), new Vector2(0.05f, -0.38f) },
        new[] { new Vector2(0f, 0.45f), new Vector2(-0.42f, -0.28f), new Vector2(0.42f, -0.28f) },
        new[] { new Vector2(0f, 0.48f), new Vector2(-0.48f, 0f), new Vector2(0.48f, 0f), new Vector2(0f, -0.48f) },
    };
    const float PawSize = 0.3f, SinglePawSize = 0.5f;
    // Faible lueur blanche (éteinte) ; bien plus lumineuse quand un joueur maintient le clic.
    static readonly Color PawDim = new Color(0.8f, 0.8f, 0.8f, 0.3f), PawLit = new Color(1.6f, 1.6f, 1.6f, 1f);
    // Empreintes repérées dans chaque modèle (repère du tas, sans sa rotation) : centre et taille.
    static readonly Dictionary<int, List<(Vector2 at, float size)>> prints = new Dictionary<int, List<(Vector2, float)>>();
    static Material pawDim, pawLit;

    readonly List<Renderer> paws = new List<Renderer>();
    // Pose de repos de chaque trace (repère du modèle), autour de laquelle elle flotte.
    readonly List<(Vector3 pos, Quaternion rot, Vector3 scale)> pawRest = new List<(Vector3, Quaternion, Vector3)>();
    // V0.7.3 (Note 05) : traces fantomatiques. Elles ondulent doucement (léger glissement, rotation et respiration) et leur
    // lueur vacille ; seulement près de la caméra.
    const float GhostRange = 30f, GhostDrift = 0.018f, GhostTurn = 7f, GhostBreath = 0.07f;
    static int ghostFrame = -1;
    readonly List<Collider> boxes = new List<Collider>();
    Transform model;
    Light glow;
    int pawsLit;
    float debrisTimer;

    public static SuperMound Create(Transform parent, int id, ItemType loot, Vector3 pos, float yaw, int pawCount)
    {
        var go = new GameObject("SuperMonticule_" + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var mound = go.AddComponent<SuperMound>();
        mound.Id = id;
        mound.Loot = loot;
        mound.BuildModel(Mathf.Clamp(pawCount, 1, 4));
        mound.FitTrigger(new Vector3(WorldBuilder.SuperMoundWidth, 0.7f, WorldBuilder.SuperMoundWidth));
        // Toujours actif : les traces fantomatiques bougent (Update ne fait rien loin de la caméra).
        mound.enabled = true;
        return mound;
    }

    // Nouveau nombre de joueurs : le modèle change (une à quatre traces).
    public void SetPawCount(int count)
    {
        count = Mathf.Clamp(count, 1, 4);
        if (count == paws.Count || Digging) return;
        BuildModel(count);
    }

    void BuildModel(int count)
    {
        bool wasHighlighted = model != null;
        if (model != null)
        {
            // Désactivé tout de suite : ses collisions ne gênent plus la pose des nouvelles traces.
            model.gameObject.SetActive(false);
            Destroy(model.gameObject);
        }
        foreach (var box in boxes) if (box != null) Destroy(box);
        boxes.Clear();
        paws.Clear();
        pawRest.Clear();
        model = new GameObject("Modele").transform;
        model.SetParent(transform, false);
        float width = WorldBuilder.SuperMoundWidth;
        GameObject art = GameArt.SpawnModel("SuperMonticule" + count, model, 0.5f);
        if (art == null) art = GameArt.SpawnModel("Monticule", model, 0.4f * GameRules.SuperMoundScale);
        if (art == null)
        {
            Material dirt = GameArt.Lit("Monticule", new Color(0.26f, 0.17f, 0.1f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
            art = GameArt.Prim(PrimitiveType.Sphere, model, new Vector3(0f, 0.07f, 0f), new Vector3(width, 0.55f, width * 0.85f), dirt);
            foreach (var c in art.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        // Mis à la largeur voulue (40 % de plus qu'un monticule).
        Bounds b = GameArt.LocalBounds(model);
        float fit = width / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
        model.localScale = Vector3.one * fit;
        b = GameArt.LocalBounds(model);
        // On peut monter dessus : collisions du maillage lui-même.
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
        if (model.GetComponentInChildren<MeshCollider>() == null)
        {
            // Sans maillage lisible : deux boîtes empilées épousent à peu près le dôme.
            float h = GameArt.LocalBounds(transform).size.y;
            var low = gameObject.AddComponent<BoxCollider>();
            low.center = new Vector3(0f, h * 0.3f, 0f);
            low.size = new Vector3(width * 0.85f, h * 0.6f, width * 0.85f);
            var high = gameObject.AddComponent<BoxCollider>();
            high.center = new Vector3(0f, h * 0.75f, 0f);
            high.size = new Vector3(width * 0.5f, h * 0.5f, width * 0.5f);
            boxes.Add(low);
            boxes.Add(high);
        }
        Physics.SyncTransforms();

        // Traces : dans les empreintes du modèle, posées sur sa surface (lancer de rayon sur ses propres collisions).
        float half = width * 0.5f;
        if (!prints.TryGetValue(count, out var found))
        {
            found = DetectPrints(count, half, art != null && art.name.StartsWith("SuperMonticule"));
            prints[count] = found;
        }
        foreach (var (at, size) in found)
        {
            Vector3 local = new Vector3(at.x, 0f, at.y);
            Vector3 top = transform.TransformPoint(local + Vector3.up * 2f);
            Vector3 normal = transform.up;
            Vector3 point = transform.TransformPoint(local + Vector3.up * b.max.y * fit);
            if (OwnHit(top, out RaycastHit hit))
            {
                point = hit.point;
                normal = Vector3.Slerp(transform.up, hit.normal, 0.5f);
                // Au niveau des bords de l'empreinte : la trace lumineuse n'est pas cachée au fond du creux.
                float rim = 0f;
                int rims = 0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    Vector3 around = local + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size * 0.5f;
                    if (!OwnHit(transform.TransformPoint(around + Vector3.up * 2f), out RaycastHit edge)) continue;
                    rim += edge.point.y;
                    rims++;
                }
                if (rims > 0) point.y = Mathf.Max(point.y, rim / rims);
            }
            var quad = GameArt.Prim(PrimitiveType.Quad, model, Vector3.zero, Vector3.one, PawMaterial(false));
            foreach (var c in quad.GetComponentsInChildren<Collider>()) Destroy(c);
            quad.name = "Trace";
            quad.transform.SetParent(transform, false);
            quad.transform.position = point + normal * 0.02f;
            // Le quad regarde vers -Z : on le couche face au ciel, les doigts vers l'avant du tas.
            quad.transform.rotation = Quaternion.LookRotation(-normal, transform.forward);
            quad.transform.localScale = Vector3.one * size;
            quad.transform.SetParent(model, true);
            var r = quad.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            paws.Add(r);
            pawRest.Add((quad.transform.localPosition, quad.transform.localRotation, quad.transform.localScale));
        }
        if (glow == null)
        {
            glow = new GameObject("LueurTraces").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.type = LightType.Point;
            glow.color = Color.white;
            glow.range = 2.2f;
            glow.shadows = LightShadows.None;
        }
        glow.transform.localPosition = Vector3.up * (GameArt.LocalBounds(model).max.y * fit + 0.5f);
        RefreshPaws();
        if (wasHighlighted) RefreshOutline();
    }

    bool OwnHit(Vector3 from, out RaycastHit result)
    {
        result = default;
        float best = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(transform) || hit.distance >= best) continue;
            best = hit.distance;
            result = hit;
        }
        return best < float.MaxValue;
    }

    // Empreintes creusées dans le modèle Meshy : relevé du dessus (grille de hauteurs), creux par rapport à la surface lissée,
    // regroupés en 'count' empreintes (k-moyennes). Une seule fois par modèle ; à défaut, la disposition de secours.
    List<(Vector2 at, float size)> DetectPrints(int count, float half, bool meshy)
    {
        var fallback = new List<(Vector2, float)>();
        foreach (Vector2 at in Layouts[count - 1]) fallback.Add((at * half, count == 1 ? SinglePawSize : PawSize));
        if (!meshy) return fallback;
        const int n = 48;
        float cell = half * 2f / n;
        var h = new float[n, n];
        var valid = new bool[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                var local = new Vector3(-half + (i + 0.5f) * cell, 2f, -half + (j + 0.5f) * cell);
                if (!OwnHit(transform.TransformPoint(local), out RaycastHit hit)) continue;
                h[i, j] = transform.InverseTransformPoint(hit.point).y;
                valid[i, j] = true;
            }
        // Surface lissée (moyenne sur ~0,35 m) : les empreintes sont nettement plus basses qu'elle.
        int radius = Mathf.Max(2, Mathf.RoundToInt(0.18f / cell));
        var points = new List<(Vector2 p, float w)>();
        float deepest = 0f;
        var depth = new float[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (!valid[i, j]) continue;
                // Pas trop près du bord du tas (pente naturelle, pas une empreinte).
                float ri = (i + 0.5f) / n * 2f - 1f, rj = (j + 0.5f) / n * 2f - 1f;
                if (ri * ri + rj * rj > 0.55f) continue;
                float sum = 0f;
                int cnt = 0;
                for (int di = -radius; di <= radius; di++)
                    for (int dj = -radius; dj <= radius; dj++)
                    {
                        int a = i + di, b = j + dj;
                        if (a < 0 || b < 0 || a >= n || b >= n || !valid[a, b]) continue;
                        sum += h[a, b];
                        cnt++;
                    }
                depth[i, j] = sum / cnt - h[i, j];
                deepest = Mathf.Max(deepest, depth[i, j]);
            }
        if (deepest < 0.01f) return fallback;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (valid[i, j] && depth[i, j] > deepest * 0.3f)
                    points.Add((new Vector2(-half + (i + 0.5f) * cell, -half + (j + 0.5f) * cell), depth[i, j]));
        if (points.Count < count * 3) return fallback;
        // k-moyennes, départ par les points les plus éloignés les uns des autres.
        var centers = new List<Vector2> { points[0].p };
        float firstBest = -1f;
        foreach (var q in points) if (q.w > firstBest) { firstBest = q.w; centers[0] = q.p; }
        while (centers.Count < count)
        {
            Vector2 far = centers[0];
            float farDist = -1f;
            foreach (var q in points)
            {
                float d = float.MaxValue;
                foreach (var c in centers) d = Mathf.Min(d, (q.p - c).sqrMagnitude);
                if (d > farDist) { farDist = d; far = q.p; }
            }
            centers.Add(far);
        }
        var sums = new Vector2[count];
        var weights = new float[count];
        var spread = new float[count];
        for (int iter = 0; iter < 12; iter++)
        {
            for (int k = 0; k < count; k++) { sums[k] = Vector2.zero; weights[k] = 0f; spread[k] = 0f; }
            foreach (var q in points)
            {
                int k = Nearest(centers, q.p);
                sums[k] += q.p * q.w;
                weights[k] += q.w;
            }
            for (int k = 0; k < count; k++) if (weights[k] > 0f) centers[k] = sums[k] / weights[k];
        }
        var cells = new int[count];
        foreach (var q in points) cells[Nearest(centers, q.p)]++;
        var result = new List<(Vector2, float)>();
        for (int k = 0; k < count; k++)
        {
            // Taille : un disque de même aire que l'empreinte, un peu agrandi (doigts compris).
            float size = Mathf.Clamp(2f * Mathf.Sqrt(cells[k] * cell * cell / Mathf.PI) * 1.4f, 0.34f, count == 1 ? 0.7f : 0.5f);
            result.Add((centers[k], size));
        }
        return result;
    }

    static int Nearest(List<Vector2> centers, Vector2 p)
    {
        int best = 0;
        for (int k = 1; k < centers.Count; k++) if ((p - centers[k]).sqrMagnitude < (p - centers[best]).sqrMagnitude) best = k;
        return best;
    }

    static Material PawMaterial(bool lit)
    {
        if (pawDim == null)
        {
            pawDim = new Material(Shader.Find("Sprites/Default")) { name = "TraceEteinte", mainTexture = Glyph.PawTexture(), color = PawDim };
            pawLit = new Material(Shader.Find("Sprites/Default")) { name = "TraceAllumee", mainTexture = Glyph.PawTexture(), color = PawLit };
        }
        return lit ? pawLit : pawDim;
    }

    // Traces allumées (une par joueur qui maintient le clic).
    public void SetPawsLit(int lit)
    {
        pawsLit = Mathf.Max(0, lit);
        RefreshPaws();
    }

    void RefreshPaws()
    {
        int lit = Digging ? paws.Count : Mathf.Min(pawsLit, paws.Count);
        for (int k = 0; k < paws.Count; k++) if (paws[k] != null) paws[k].sharedMaterial = PawMaterial(k < lit);
        if (glow == null) return;
        glow.enabled = true;
        glow.intensity = 0.15f + 0.35f * lit;
    }

    // Les traces ne prennent pas le contour blanc du tas visé.
    protected override bool HasOutline(Renderer r) => !paws.Contains(r);

    public override string Prompt => Digging ? "" : Loc.T("Creuser");
    public override bool CanInteract => !Digging;
    public override void Interact() => GameSession.Instance.TouchSuperMound(this);

    // Toutes les traces allumées : les joueurs creusent ensemble, de la terre gicle pendant 1,2 seconde.
    public void StartDigging()
    {
        if (Digging) return;
        Digging = true;
        enabled = true;
        debrisTimer = 0f;
        RefreshPaws();
        GameAudio.Play3D("dig", transform.position, 1f, 22f, 0.1f);
    }

    void Update()
    {
        if (!Digging)
        {
            AnimateGhosts();
            return;
        }
        debrisTimer -= Time.deltaTime;
        if (debrisTimer > 0f) return;
        debrisTimer = 0.22f;
        Fx.Burst(transform.position + Vector3.up * 0.35f, Fx.Dirt, 14, 3.6f, 0.1f, 0.9f);
    }

    void AnimateGhosts()
    {
        Camera cam = Camera.main;
        if (cam == null || paws.Count == 0 || (cam.transform.position - transform.position).sqrMagnitude > GhostRange * GhostRange) return;
        float t = Time.time;
        // La lueur des traces éteintes vacille (matériau commun : une fois par image).
        if (ghostFrame != Time.frameCount && pawDim != null)
        {
            ghostFrame = Time.frameCount;
            float flicker = 0.75f + 0.25f * Mathf.Sin(t * 2.3f) * Mathf.Sin(t * 0.9f + 1.3f);
            pawDim.color = new Color(PawDim.r, PawDim.g, PawDim.b, PawDim.a * flicker);
        }
        float seed = Id * 1.618f;
        for (int k = 0; k < paws.Count && k < pawRest.Count; k++)
        {
            if (paws[k] == null) continue;
            float p = seed + k * 2.39f;
            var (pos, rot, scale) = pawRest[k];
            var tr = paws[k].transform;
            // Glissement dans le plan de la trace, un peu de rotation, et une respiration de la taille.
            Vector3 drift = rot * new Vector3(Mathf.Sin(t * 0.83f + p), Mathf.Sin(t * 0.61f + p * 1.7f), 0f) * GhostDrift / Mathf.Max(0.001f, model.localScale.x);
            tr.localPosition = pos + drift;
            tr.localRotation = rot * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.7f + p) * GhostTurn);
            tr.localScale = scale * (1f + GhostBreath * Mathf.Sin(t * 1.1f + p * 0.5f));
        }
    }

    // Déterré : une dernière gerbe de terre, et le Super Monticule disparaît.
    public void Open()
    {
        Fx.Burst(transform.position + Vector3.up * 0.3f, Fx.Dirt, 36, 4.6f, 0.12f);
        Destroy(gameObject);
    }
}
