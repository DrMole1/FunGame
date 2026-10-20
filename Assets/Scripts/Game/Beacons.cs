using System.Collections.Generic;
using UnityEngine;

// Appareils posés au sol, ramassables, reliés au réseau d'énergie des Balises (GDD 7.1 à 7.4).
public enum DeviceKind : byte { Beacon = 0, Enhanced = 1, Teleporter = 2, Station = 3 }

// Balise (GDD 7.1) : petite borne posée au sol, ramassable. La Balise du Hub, plus grande, ne se ramasse pas :
// elle est chargée 10 minutes au début du jeu (jauge circulaire autour d'elle) et se recharge avec un Chargeur.
// Balise Améliorée (7.3) : cristal et aura rouges, coordonnées affichées au-dessus d'elle ; ni Abjuration ni apparition
// d'entité à moins de 5 m. Téléporteur (7.2) et Station Curative (7.4) : chargés s'ils voient une Balise chargée.
public class Beacon : Interactable
{
    public const float HubHeight = 1.5f, Height = 0.85f, PadHeight = 0.16f, StationHeight = 1.15f;
    static readonly Color ChargedTint = Color.white, DischargedTint = new Color(0.42f, 0.42f, 0.46f);

    public int Id { get; private set; }   // -1 : Balise du Hub
    public DeviceKind Kind { get; private set; }
    public bool IsHub => Id < 0;
    // Balises (simples ou améliorées) : elles relaient l'énergie ; Téléporteur et Station ne font que la recevoir.
    public bool Relays => Kind == DeviceKind.Beacon || Kind == DeviceKind.Enhanced;
    public bool Charged { get; private set; }
    public ItemType Item => Kind == DeviceKind.Enhanced ? ItemType.BaliseAmelioree : Kind == DeviceKind.Teleporter ? ItemType.Teleporteur
        : Kind == DeviceKind.Station ? ItemType.StationCurative : ItemType.Balise;
    float VisualHeight => IsHub ? HubHeight : Kind == DeviceKind.Teleporter ? PadHeight : Kind == DeviceKind.Station ? StationHeight : Height;
    // Point d'attache des rayons d'énergie : le cristal (le centre du disque, le haut du tube).
    public Vector3 Top => transform.position + Vector3.up * (Kind == DeviceKind.Teleporter ? 0.12f : VisualHeight * 0.86f);

    Renderer[] bodies;
    GameObject halo;
    Light glow;
    MaterialPropertyBlock block;
    TextMesh coordinates;
    ParticleSystem gas;

    public static Beacon Create(Transform parent, int id, Vector3 pos, float yaw, DeviceKind kind = DeviceKind.Beacon)
    {
        bool hub = id < 0;
        var go = new GameObject(hub ? "Balise du Hub" : kind + " " + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var beacon = go.AddComponent<Beacon>();
        beacon.Id = id;
        beacon.Kind = kind;
        float height = beacon.VisualHeight;
        var visual = new GameObject("Visuel").transform;
        visual.SetParent(go.transform, false);
        switch (kind)
        {
            case DeviceKind.Teleporter:
                if (GameArt.SpawnModel("Teleporteur", visual, height) == null)
                {
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.04f, 0f), new Vector3(0.9f, 0.04f, 0.9f), GameArt.Lit("PadBronze", new Color(0.55f, 0.42f, 0.25f), null, 0.6f));
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.09f, 0f), new Vector3(0.6f, 0.03f, 0.6f), GameArt.Emissive("PadCore", new Color(1f, 0.65f, 0.1f), new Color(0.8f, 0.45f, 0.05f)));
                }
                break;
            case DeviceKind.Station:
                if (GameArt.SpawnModel("StationCurative", visual, height) == null)
                {
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.12f, 0f), new Vector3(0.45f, 0.12f, 0.45f), GameArt.Metal);
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.6f, 0f), new Vector3(0.32f, 0.38f, 0.32f), GameArt.Lit("StationGlass", new Color(0.7f, 0.9f, 0.75f), null, 0.9f));
                }
                beacon.gas = BuildGas(go.transform, height);
                break;
            default:
                if (GameArt.SpawnModel("Balise", visual, height) == null)
                {
                    Material stone = GameArt.Lit("BeaconStone", new Color(0.45f, 0.43f, 0.38f), null, 0.15f);
                    GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, height * 0.35f, 0f), new Vector3(0.22f, height * 0.35f, 0.22f) * (height / Height), stone);
                    GameArt.Prim(PrimitiveType.Sphere, visual, new Vector3(0f, height * 0.84f, 0f), new Vector3(0.3f, 0.26f, 0.3f) * (height / Height),
                        kind == DeviceKind.Enhanced ? GameArt.Emissive("BeaconCrystalRed", new Color(1f, 0.2f, 0.15f), new Color(0.9f, 0.1f, 0.05f))
                                                    : GameArt.Emissive("BeaconCrystal", new Color(0.2f, 0.45f, 1f), new Color(0.1f, 0.3f, 0.9f)));
                }
                else if (kind == DeviceKind.Enhanced)
                {
                    // Même modèle, cristal rouge (texture virée au rouge par "Installer les entités V0.4").
                    var red = Resources.Load<Material>("Art/BaliseRouge");
                    if (red != null) foreach (var r in visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial = red;
                }
                break;
        }

        beacon.bodies = visual.GetComponentsInChildren<Renderer>();
        beacon.block = new MaterialPropertyBlock();
        // Halo autour du cristal quand l'appareil est chargé (rouge pour la Balise Améliorée). Aucun sur le Téléporteur.
        if (kind != DeviceKind.Teleporter)
        {
            Material aura = kind == DeviceKind.Enhanced ? GameArt.GhostAuraTinted("Red", new Color(1f, 0.2f, 0.15f))
                : kind == DeviceKind.Station ? GameArt.GhostAuraTinted("Green", new Color(0.3f, 1f, 0.4f)) : GameArt.GhostAura;
            float haloSize = kind == DeviceKind.Station ? 0.55f : height * 0.26f;
            beacon.halo = GameArt.Prim(PrimitiveType.Sphere, go.transform, beacon.Top - pos, Vector3.one * haloSize, aura);
            Object.Destroy(beacon.halo.GetComponent<Collider>());
        }
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
        if (kind == DeviceKind.Enhanced)
        {
            // Distance jusqu'au Hub, dans l'air au-dessus d'elle (renseignée par BeaconNetwork.Add).
            beacon.coordinates = GameArt.CreateText(go.transform, "DistanceHub", new Vector3(0f, height + 0.45f, 0f), 0.02f, new Color(1f, 0.45f, 0.4f));
        }
        beacon.FitTrigger(kind == DeviceKind.Teleporter ? new Vector3(0.9f, 0.4f, 0.9f) : new Vector3(0.6f, height, 0.6f));
        beacon.SetCharged(false);
        return beacon;
    }

    // Gaz vert pétillant dans le tube de la Station Curative.
    static ParticleSystem BuildGas(Transform parent, float height)
    {
        var go = new GameObject("Gaz");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, height * 0.25f, 0f);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 1.3f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 1f, 0.35f, 0.9f), new Color(0.75f, 1f, 0.5f, 0.9f));
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = ps.emission;
        emission.rateOverTime = 28f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.12f;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        color.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GameArt.Spark;
        return ps;
    }

    public void SetCharged(bool on)
    {
        Charged = on;
        if (halo != null) halo.SetActive(on);
        if (glow != null) glow.enabled = on;
        if (gas != null) { if (on && !gas.isPlaying) gas.Play(); else if (!on && gas.isPlaying) gas.Stop(); }
        block.SetColor("_Color", on ? ChargedTint : DischargedTint);
        foreach (var r in bodies) if (r != null) r.SetPropertyBlock(block);
    }

    // Balise Améliorée : distance au sol jusqu'à la Balise du Hub, arrondie au mètre (ex. "29m"), suivie de sa profondeur
    // ou de sa hauteur par rapport au niveau du Hub quand elle dépasse 2 m (ex. "29m ↓3m", "29m ↑4m").
    const float LevelShownBeyond = 2f;

    public void ShowHubDistance(Vector3 hubPos)
    {
        if (coordinates == null) return;
        Vector3 d = transform.position - hubPos;
        string text = Mathf.RoundToInt(new Vector2(d.x, d.z).magnitude) + "m";
        float level = transform.position.y - hubPos.y;
        if (Mathf.Abs(level) > LevelShownBeyond) text += (level < 0f ? " ↓" : " ↑") + Mathf.RoundToInt(Mathf.Abs(level)) + "m";
        coordinates.text = text;
    }

    void Update()
    {
        if (coordinates != null && Camera.main != null)
            coordinates.transform.rotation = Quaternion.LookRotation(coordinates.transform.position - Camera.main.transform.position);
        if (!Charged || Kind == DeviceKind.Teleporter) return;
        // Le halo respire doucement.
        float s = (Kind == DeviceKind.Station ? 0.55f : VisualHeight * 0.26f) * (1f + 0.12f * Mathf.Sin(Time.time * 2.4f + Id));
        halo.transform.localScale = Vector3.one * s;
    }

    public override string Prompt
    {
        get
        {
            return IsHub ? "Recharger la Balise du Hub" : "Ramasser " + Items.Get(Item).name;
        }
    }

    // La Balise du Hub n'est interactive (contour, message, touche E) que Chargeur sélectionné.
    public override bool CanInteract => !IsHub || (GameSession.Instance != null && GameSession.Instance.SelectedItem == ItemType.Chargeur);
    public override void Interact() => GameSession.Instance.InteractBeacon(this);
}

// Ensemble des Balises d'une partie : charge de la Balise du Hub, propagation de la charge (vision directe,
// aucun mur entre deux Balises) et rayons d'énergie bleus, qui circulent des Balises éloignées vers le Hub.
// Téléporteurs et Stations Curatives se chargent en voyant une Balise chargée, sans relayer l'énergie.
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

    public Beacon Add(int id, Vector3 pos, DeviceKind kind = DeviceKind.Beacon)
    {
        if (placed.TryGetValue(id, out var existing) && existing != null) return existing;
        var b = Beacon.Create(transform, id, pos, Random.Range(0f, 360f), kind);
        b.ShowHubDistance(hub.transform.position);
        placed[id] = b;
        dirty = true;
        return b;
    }

    public Beacon Get(int id) => placed.TryGetValue(id, out var b) ? b : null;
    public IEnumerable<Beacon> Placed => placed.Values;

    // Liens d'énergie d'un appareil chargé jusqu'à la Balise du Hub (points d'attache), ou null s'il n'est pas relié.
    public List<Vector3> PathToHub(Beacon from)
    {
        var points = new List<Vector3>();
        for (Beacon b = from; b != null; b = parentOf.TryGetValue(b, out var next) ? next : null)
        {
            points.Add(b.Top);
            if (b == hub) return points;
            if (points.Count > 256) break;
        }
        return null;
    }

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
        var leaves = new List<Beacon>();
        foreach (var b in placed.Values) if (b != null) (b.Relays ? pending : leaves).Add(b);
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
        // Téléporteurs et Stations : reliés à la Balise chargée la plus proche qu'ils voient.
        foreach (var leaf in leaves)
        {
            Beacon best = null;
            float bestDist = float.MaxValue;
            foreach (var from in charged)
            {
                float d = (from.Top - leaf.Top).sqrMagnitude;
                if (d < bestDist && InSight(from, leaf)) { best = from; bestDist = d; }
            }
            if (best != null) parentOf[leaf] = best;
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
            if (c.GetComponentInParent<Interactable>() != null || c.GetComponentInParent<Entity>() != null) continue;
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
