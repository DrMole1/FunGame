using UnityEngine;

// Téléporteur Inversé (GDD 7.9, V0.6.10) : acheté au Terminal d'un Hub, il apparaît au centre de ce Hub, 5 fois plus grand
// qu'un Téléporteur. Il est relié directement à la Balise de son Hub quand elle est chargée (sinon il ne fonctionne pas).
// Les joueurs qui se tiennent dessus canalisent ensemble, puis voyagent jusqu'à la Balise la plus éloignée du flux de ce Hub.
// Après un voyage : teinte terne et 120 secondes de recharge, montrées par une jauge circulaire (style de celle de la
// Balise du Hub).
// Visé pendant sa recharge, il affiche le temps restant (sans contour ni touche).
public class ReverseTeleporter : Interactable
{
    // Le modèle du Téléporteur, 5 fois plus large, est aplati à 25 cm de haut pour qu'on marche dessus. Rayon du disque : mesuré
    // sur le modèle.
    const float SurfaceHeight = Beacon.PadHeight, FlatHeight = 0.25f;
    public float Radius { get; private set; } = 0.45f * GameRules.ReverseTeleporterScale;
    static readonly Color Dull = new Color(0.35f, 0.35f, 0.38f);

    public int Hub { get; private set; }
    public float CooldownEnd { get; private set; } = -1f;
    public bool CoolingDown => Time.time < CooldownEnd;
    public Vector3 Top => transform.position + Vector3.up * (SurfaceHeight + 0.6f);

    Renderer[] bodies;
    MaterialPropertyBlock block;
    GameObject gaugeRoot;
    MeshFilter gaugeFill;
    int gaugeShown = -1;
    LineRenderer beam;
    bool dull;
    Light glow;

    public static ReverseTeleporter Create(Transform parent, int hub, Vector3 ground)
    {
        var go = new GameObject("Téléporteur Inversé " + hub);
        go.transform.SetParent(parent, false);
        go.transform.position = ground;
        var pad = go.AddComponent<ReverseTeleporter>();
        pad.Hub = hub;

        var visual = new GameObject("Visuel").transform;
        visual.SetParent(go.transform, false);
        float height = Beacon.PadHeight * GameRules.ReverseTeleporterScale;
        if (GameArt.SpawnModel("Teleporteur", visual, height) == null)
        {
            GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.04f, 0f) * GameRules.ReverseTeleporterScale, new Vector3(0.9f, 0.04f, 0.9f) * GameRules.ReverseTeleporterScale, GameArt.Lit("PadBronze", new Color(0.55f, 0.42f, 0.25f), null, 0.6f));
            GameArt.Prim(PrimitiveType.Cylinder, visual, new Vector3(0f, 0.09f, 0f) * GameRules.ReverseTeleporterScale, new Vector3(0.6f, 0.03f, 0.6f) * GameRules.ReverseTeleporterScale, GameArt.Emissive("PadCore", new Color(1f, 0.65f, 0.1f), new Color(0.8f, 0.45f, 0.05f)));
        }
        // 5 fois plus large, mais aplati : on marche dessus comme sur un Téléporteur (aucun collider).
        visual.localScale = new Vector3(1f, FlatHeight / height, 1f);
        pad.bodies = visual.GetComponentsInChildren<Renderer>();
        if (pad.bodies.Length > 0)
        {
            Bounds b = pad.bodies[0].bounds;
            foreach (var r in pad.bodies) b.Encapsulate(r.bounds);
            pad.Radius = Mathf.Max(0.5f, Mathf.Max(b.extents.x, b.extents.z));
        }
        pad.block = new MaterialPropertyBlock();

        pad.glow = new GameObject("Lueur").AddComponent<Light>();
        pad.glow.transform.SetParent(go.transform, false);
        pad.glow.transform.localPosition = Vector3.up * 1.2f;
        pad.glow.type = LightType.Point;
        pad.glow.color = new Color(1f, 0.7f, 0.25f);
        pad.glow.range = pad.Radius * 2.5f;
        pad.glow.intensity = 1.2f;
        pad.glow.shadows = LightShadows.None;

        // Zone de visée, à la taille du disque (seulement visée pendant la recharge).
        pad.FitTrigger(new Vector3(pad.Radius * 2f, 0.6f, pad.Radius * 2f));
        pad.BuildGauge();
        return pad;
    }

    void BuildGauge()
    {
        gaugeRoot = new GameObject("Jauge");
        gaugeRoot.transform.SetParent(transform, false);
        var back = new GameObject("Jauge (fond)");
        back.transform.SetParent(gaugeRoot.transform, false);
        back.transform.localPosition = Vector3.up * 0.015f;
        back.AddComponent<MeshFilter>().sharedMesh = BeaconNetwork.Ring(1f, Radius + 0.15f, Radius + 0.5f);
        back.AddComponent<MeshRenderer>().sharedMaterial = BeaconNetwork.BrushMaterial("InverseJaugeFond", new Color(0.09f, 0.09f, 0.1f), 21, 3000);
        var fill = new GameObject("Jauge (charge)");
        fill.transform.SetParent(gaugeRoot.transform, false);
        fill.transform.localPosition = Vector3.up * 0.025f;
        gaugeFill = fill.AddComponent<MeshFilter>();
        fill.AddComponent<MeshRenderer>().sharedMaterial = BeaconNetwork.BrushMaterial("InverseJauge" + Hub, Beacon.HubColor(Hub), 22, 3001);
        gaugeRoot.SetActive(false);
    }

    // 'remaining' : secondes de recharge restantes (un voyage vient de partir : la recharge complète).
    public void StartCooldown(float remaining) => CooldownEnd = Time.time + Mathf.Clamp(remaining, 0f, GameRules.ReverseTeleporterCooldown);

    public float CooldownLeft => Mathf.Max(0f, CooldownEnd - Time.time);

    public override bool CanInteract => CoolingDown;
    public override bool InfoOnly => true;
    protected override bool OutlineVisible => false;
    public override string Prompt => Loc.F("Téléporteur Inversé en recharge : {0} s", Mathf.CeilToInt(CooldownLeft));
    public override void Interact() { }

    // Pieds du personnage sur le disque.
    public bool Holds(Vector3 feet)
    {
        Vector3 d = feet - transform.position;
        return Mathf.Abs(d.y) < 1.2f && new Vector2(d.x, d.z).magnitude < Radius + 0.2f;
    }

    // Relié à la Balise de son Hub (active et chargée) : il fonctionne s'il n'est pas en recharge.
    public static bool Linked(BeaconNetwork network, Beacon hubBeacon) =>
        network != null && network.HubCharged && hubBeacon != null && hubBeacon.gameObject.activeInHierarchy;

    public bool Ready(BeaconNetwork network) => !CoolingDown && Linked(network, network != null ? network.GetHub(Hub) : null);

    // À chaque image : lien d'énergie vers la Balise du Hub, teinte, lueur et jauge de recharge.
    public void Refresh(BeaconNetwork network)
    {
        Beacon hubBeacon = network != null ? network.GetHub(Hub) : null;
        bool linked = Linked(network, hubBeacon);
        if (linked)
        {
            if (beam == null) beam = network.NewBeam();
            if (!beam.gameObject.activeSelf) beam.gameObject.SetActive(true);
            network.ShowBeam(beam, hubBeacon, Top, hubBeacon.Top);
        }
        else if (beam != null && beam.gameObject.activeSelf) beam.gameObject.SetActive(false);

        bool cooling = CoolingDown;
        bool wantDull = cooling || !linked;
        if (wantDull != dull)
        {
            dull = wantDull;
            block.SetColor("_Color", dull ? Dull : Color.white);
            foreach (var r in bodies) if (r != null) r.SetPropertyBlock(block);
            glow.enabled = !dull;
        }

        if (gaugeRoot.activeSelf != cooling) gaugeRoot.SetActive(cooling);
        if (!cooling) { gaugeShown = -1; return; }
        // La jauge se remplit à mesure que la recharge avance.
        const int Segments = 120;
        int shown = Mathf.CeilToInt(Mathf.Clamp01(1f - CooldownLeft / GameRules.ReverseTeleporterCooldown) * Segments);
        if (shown == gaugeShown) return;
        gaugeShown = shown;
        if (gaugeFill.sharedMesh != null) Destroy(gaugeFill.sharedMesh);
        gaugeFill.sharedMesh = shown > 0 ? BeaconNetwork.Ring((float)shown / Segments, Radius + 0.2f, Radius + 0.45f) : null;
    }

    void OnDestroy()
    {
        if (beam != null) Destroy(beam.gameObject);
        if (gaugeFill != null && gaugeFill.sharedMesh != null) Destroy(gaugeFill.sharedMesh);
    }
}
