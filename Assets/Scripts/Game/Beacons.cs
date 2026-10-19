using System.Collections.Generic;
using UnityEngine;

// Balise (GDD 7.1) : petite borne posée au sol, ramassable. La Balise du Hub, plus grande, ne se ramasse pas :
// elle est chargée 10 minutes au début du jeu (jauge circulaire autour d'elle) et se recharge avec un Chargeur.
public class Beacon : Interactable
{
    public const float HubHeight = 1.5f, Height = 0.85f;
    static readonly Color ChargedTint = Color.white, DischargedTint = new Color(0.42f, 0.42f, 0.46f);

    public int Id { get; private set; }   // -1 : Balise du Hub
    public bool IsHub => Id < 0;
    public bool Charged { get; private set; }
    // Point d'attache des rayons d'énergie : le cristal.
    public Vector3 Top => transform.position + Vector3.up * (IsHub ? HubHeight : Height) * 0.86f;

    Renderer[] bodies;
    GameObject halo;
    Light glow;
    MaterialPropertyBlock block;

    public static Beacon Create(Transform parent, int id, Vector3 pos, float yaw)
    {
        bool hub = id < 0;
        float height = hub ? HubHeight : Height;
        var go = new GameObject(hub ? "Balise du Hub" : "Balise " + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var visual = new GameObject("Visuel").transform;
        visual.SetParent(go.transform, false);
        if (GameArt.SpawnModel("Balise", visual, height) == null)
        {
            Material stone = GameArt.Lit("BeaconStone", new Color(0.45f, 0.43f, 0.38f), null, 0.15f);
            GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, height * 0.35f, 0f), new Vector3(0.22f, height * 0.35f, 0.22f) * (height / Height), stone);
            GameArt.Prim(PrimitiveType.Sphere, visual, new Vector3(0f, height * 0.84f, 0f), new Vector3(0.3f, 0.26f, 0.3f) * (height / Height),
                GameArt.Emissive("BeaconCrystal", new Color(0.2f, 0.45f, 1f), new Color(0.1f, 0.3f, 0.9f)));
        }

        var beacon = go.AddComponent<Beacon>();
        beacon.Id = id;
        beacon.bodies = visual.GetComponentsInChildren<Renderer>();
        beacon.block = new MaterialPropertyBlock();
        // Halo bleu autour du cristal quand la Balise est chargée.
        beacon.halo = GameArt.Prim(PrimitiveType.Sphere, go.transform, new Vector3(0f, height * 0.86f, 0f), Vector3.one * height * 0.26f, GameArt.GhostAura);
        Object.Destroy(beacon.halo.GetComponent<Collider>());
        if (hub)
        {
            beacon.glow = new GameObject("Lueur").AddComponent<Light>();
            beacon.glow.transform.SetParent(go.transform, false);
            beacon.glow.transform.localPosition = new Vector3(0f, height * 0.9f, 0f);
            beacon.glow.type = LightType.Point;
            beacon.glow.color = new Color(0.35f, 0.6f, 1f);
            beacon.glow.range = 4f;
            beacon.glow.intensity = 1.4f;
            beacon.glow.shadows = LightShadows.None;
            // Socle solide : on ne traverse pas la grande Balise.
            var solid = go.AddComponent<CapsuleCollider>();
            solid.center = new Vector3(0f, height * 0.5f, 0f);
            solid.height = height;
            solid.radius = 0.22f;
        }
        beacon.FitTrigger(new Vector3(0.6f, height, 0.6f));
        beacon.SetCharged(false);
        return beacon;
    }

    public void SetCharged(bool on)
    {
        Charged = on;
        halo.SetActive(on);
        if (glow != null) glow.enabled = on;
        block.SetColor("_Color", on ? ChargedTint : DischargedTint);
        foreach (var r in bodies) if (r != null) r.SetPropertyBlock(block);
    }

    void Update()
    {
        if (!Charged) return;
        // Le halo respire doucement.
        float s = (IsHub ? HubHeight : Height) * (0.26f + 0.03f * Mathf.Sin(Time.time * 2.4f + Id));
        halo.transform.localScale = Vector3.one * s;
    }

    public override string Prompt
    {
        get
        {
            if (!IsHub) return "Ramasser la Balise";
            var session = GameSession.Instance;
            if (session != null && session.SelectedItem == ItemType.Chargeur) return "Recharger la Balise du Hub";
            return Charged ? "Balise du Hub (chargée)" : "Balise du Hub (déchargée) : utilisez un Chargeur";
        }
    }

    // La Balise du Hub lit l'objet sélectionné (un Chargeur) au moment du clic.
    public override bool KeepsSelection => IsHub;
    public override void Interact() => GameSession.Instance.InteractBeacon(this);
}

// Ensemble des Balises d'une partie : charge de la Balise du Hub, propagation de la charge (vision directe,
// aucun mur entre deux Balises) et rayons d'énergie bleus, qui circulent des Balises éloignées vers le Hub.
public class BeaconNetwork : MonoBehaviour
{
    const float RecomputeInterval = 0.4f;
    const int GaugeSegments = 120;
    const float GaugeInner = 0.55f, GaugeOuter = 0.68f;
    const float BackInner = 0.52f, BackOuter = 0.71f;

    Beacon hub;
    Transform world;
    readonly Dictionary<int, Beacon> placed = new Dictionary<int, Beacon>();
    // Pour chaque Balise chargée, la Balise vers laquelle son énergie s'écoule (plus proche du Hub).
    readonly Dictionary<Beacon, Beacon> parentOf = new Dictionary<Beacon, Beacon>();
    readonly Dictionary<Beacon, LineRenderer> beams = new Dictionary<Beacon, LineRenderer>();
    readonly RaycastHit[] hits = new RaycastHit[16];
    MaterialPropertyBlock beamBlock;
    float recomputeTimer;
    bool dirty = true;

    // Charge restante de la Balise du Hub (secondes).
    public float Charge { get; set; } = GameRules.BeaconCharge;
    public bool HubCharged => Charge > 0f;
    public Beacon Hub => hub;
    public IEnumerable<int> Ids => placed.Keys;

    MeshFilter gauge;
    int gaugeShown = -1;

    public static BeaconNetwork Create(Transform world, Vector3 hubPos, float hubYaw)
    {
        var net = new GameObject("Balises").AddComponent<BeaconNetwork>();
        net.transform.SetParent(world, false);
        net.world = world;
        net.beamBlock = new MaterialPropertyBlock();
        net.hub = Beacon.Create(net.transform, -1, hubPos, hubYaw);
        net.BuildGauge(hubPos);
        return net;
    }

    public Beacon Add(int id, Vector3 pos)
    {
        if (placed.TryGetValue(id, out var existing) && existing != null) return existing;
        var b = Beacon.Create(transform, id, pos, Random.Range(0f, 360f));
        placed[id] = b;
        dirty = true;
        return b;
    }

    public Beacon Get(int id) => placed.TryGetValue(id, out var b) ? b : null;

    public void Remove(int id)
    {
        if (!placed.TryGetValue(id, out var b)) return;
        placed.Remove(id);
        if (b != null)
        {
            if (beams.TryGetValue(b, out var beam)) { if (beam != null) Destroy(beam.gameObject); beams.Remove(b); }
            Destroy(b.gameObject);
        }
        dirty = true;
    }

    public void Clear()
    {
        foreach (int id in new List<int>(placed.Keys)) Remove(id);
        dirty = true;
    }

    void Update()
    {
        recomputeTimer -= Time.deltaTime;
        if (dirty || recomputeTimer <= 0f) Recompute();
        UpdateGauge();
        UpdateBeams();
    }

    // Propagation de la charge : depuis la Balise du Hub (si elle est chargée), de proche en proche
    // vers toute Balise en vision directe d'une Balise déjà chargée.
    void Recompute()
    {
        dirty = false;
        recomputeTimer = RecomputeInterval;
        parentOf.Clear();
        var charged = new List<Beacon>();
        if (HubCharged) charged.Add(hub);
        var pending = new List<Beacon>();
        foreach (var b in placed.Values) if (b != null) pending.Add(b);
        for (int head = 0; head < charged.Count; head++)
        {
            Beacon from = charged[head];
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (!InSight(from, pending[i])) continue;
                parentOf[pending[i]] = from;
                charged.Add(pending[i]);
                pending.RemoveAt(i);
            }
        }
        hub.SetCharged(HubCharged);
        foreach (var b in placed.Values)
            if (b != null && b.Charged != parentOf.ContainsKey(b)) b.SetCharged(parentOf.ContainsKey(b));
    }

    // Aucun élément du décor entre les deux cristaux (les objets interactifs, joueurs et entités ne bloquent pas).
    bool InSight(Beacon a, Beacon b)
    {
        Vector3 from = a.Top, to = b.Top, d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        int n = Physics.RaycastNonAlloc(from, d / len, hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (!c.transform.IsChildOf(world)) continue;
            if (c.GetComponentInParent<Interactable>() != null || c.GetComponentInParent<Dame>() != null) continue;
            return false;
        }
        return true;
    }

    void UpdateBeams()
    {
        foreach (var kv in parentOf)
        {
            if (kv.Key == null || kv.Value == null) continue;
            if (!beams.TryGetValue(kv.Key, out var line) || line == null) beams[kv.Key] = line = NewBeam();
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            // De la Balise éloignée vers celle qui la relie au Hub : l'énergie circule dans ce sens.
            Vector3 a = kv.Key.Top, b = kv.Value.Top;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            beamBlock.SetFloat("_Length", Vector3.Distance(a, b));
            line.SetPropertyBlock(beamBlock);
        }
        foreach (var kv in beams)
            if (kv.Value != null && (kv.Key == null || !parentOf.ContainsKey(kv.Key)) && kv.Value.gameObject.activeSelf)
                kv.Value.gameObject.SetActive(false);
    }

    LineRenderer NewBeam()
    {
        var go = new GameObject("Rayon");
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = 0.09f;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 2;
        line.sharedMaterial = GameArt.EnergyBeam;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    // ---------- Jauge circulaire autour de la Balise du Hub (pas de compteur numérique) ----------

    void BuildGauge(Vector3 hubPos)
    {
        // Même style que les boutons du menu : coups de pinceau (fond sombre, un peu plus large, puis trait bleu).
        var back = new GameObject("Jauge (fond)");
        back.transform.SetParent(transform, false);
        back.transform.position = hubPos + Vector3.up * 0.015f;
        back.AddComponent<MeshFilter>().sharedMesh = Ring(1f, BackInner, BackOuter);
        back.AddComponent<MeshRenderer>().sharedMaterial = BrushMaterial("BeaconGaugeBack", new Color(0.09f, 0.09f, 0.1f), 21, 3000);

        var fill = new GameObject("Jauge");
        fill.transform.SetParent(transform, false);
        fill.transform.position = hubPos + Vector3.up * 0.025f;
        gauge = fill.AddComponent<MeshFilter>();
        fill.AddComponent<MeshRenderer>().sharedMaterial = BrushMaterial("BeaconGauge", new Color(0.3f, 0.65f, 1f), 22, 3001);
    }

    static Material BrushMaterial(string name, Color color, int seed, int queue)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { name = name, color = color };
        m.mainTexture = UiKit.BrushTexture(2048, 96, seed);
        m.renderQueue = queue;
        return m;
    }

    void UpdateGauge()
    {
        int shown = Mathf.CeilToInt(Mathf.Clamp01(Charge / GameRules.BeaconCharge) * GaugeSegments);
        if (shown == gaugeShown) return;
        gaugeShown = shown;
        if (gauge.sharedMesh != null) Destroy(gauge.sharedMesh);
        gauge.sharedMesh = shown > 0 ? Ring((float)shown / GaugeSegments, GaugeInner, GaugeOuter) : null;
        if (shown == 0 || HubCharged != hub.Charged) dirty = true;
    }

    // Anneau plat (vu du dessus), rempli dans le sens horaire à partir du nord sur la fraction donnée.
    // Le coup de pinceau s'étire sur la partie remplie : il garde ses deux extrémités (poils du pinceau).
    static Mesh Ring(float fraction, float inner, float outer)
    {
        int segs = Mathf.Max(1, Mathf.RoundToInt(GaugeSegments * fraction));
        var verts = new Vector3[(segs + 1) * 2];
        var uvs = new Vector2[verts.Length];
        var colors = new Color[verts.Length];
        var tris = new int[segs * 6];
        for (int i = 0; i <= segs; i++)
        {
            float a = (float)i / GaugeSegments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            verts[i * 2] = dir * inner;
            verts[i * 2 + 1] = dir * outer;
            uvs[i * 2] = new Vector2((float)i / segs, 0f);
            uvs[i * 2 + 1] = new Vector2((float)i / segs, 1f);
            colors[i * 2] = colors[i * 2 + 1] = Color.white;
            if (i == segs) break;
            int v = i * 2;
            tris[i * 6] = v; tris[i * 6 + 1] = v + 1; tris[i * 6 + 2] = v + 3;
            tris[i * 6 + 3] = v; tris[i * 6 + 4] = v + 3; tris[i * 6 + 5] = v + 2;
        }
        var mesh = new Mesh { vertices = verts, uv = uvs, colors = colors, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
