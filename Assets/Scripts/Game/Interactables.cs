using System.Collections.Generic;
using UnityEngine;

// Objet interactif : visé au centre de l'écran et assez proche, son contour s'illumine (côté client uniquement).
public abstract class Interactable : MonoBehaviour
{
    Renderer[] renderers;
    Material[][] originals;
    // 'highlighted' : visé par le joueur ; 'outlined' : contour réellement affiché.
    bool highlighted, outlined;

    // Un objet peut être visé sans afficher de contour (Pogo une fois sorti de sa boîte).
    protected virtual bool OutlineVisible => true;
    protected virtual Material OutlineMaterial => GameArt.Outline;

    public abstract string Prompt { get; }
    public virtual bool CanInteract => true;
    public abstract void Interact();

    public void SetHighlight(bool on)
    {
        highlighted = on;
        UpdateOutline();
    }

    // À rappeler quand OutlineVisible change pendant que l'objet est visé.
    protected void UpdateOutline()
    {
        bool show = highlighted && OutlineVisible;
        if (show == outlined) return;
        outlined = show;
        if (show)
        {
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (!(r is ParticleSystemRenderer) && r.GetComponent<TextMesh>() == null) list.Add(r);
            renderers = list.ToArray();
            originals = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                originals[i] = renderers[i].sharedMaterials;
                var mats = new Material[originals[i].Length + 1];
                originals[i].CopyTo(mats, 0);
                mats[mats.Length - 1] = OutlineMaterial;
                renderers[i].sharedMaterials = mats;
            }
        }
        else if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterials = originals[i];
            renderers = null;
        }
    }

    void OnDisable() => SetHighlight(false);

    // Collider de sélection autour de l'objet.
    protected void FitTrigger(Vector3 minSize)
    {
        Bounds b = GameArt.LocalBounds(transform);
        var col = gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = b.center;
        col.size = Vector3.Max(b.size, minSize);
    }
}

// Objet ramassable (Spawn Aléatoire dans le labyrinthe).
public class ItemPickup : Interactable
{
    public int Id { get; private set; }
    public ItemType Type { get; private set; }

    public static ItemPickup Create(Transform parent, int id, ItemType type, Vector3 pos)
    {
        var go = new GameObject("Objet_" + type + "_" + id);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var visual = new GameObject("Visuel").transform;
        visual.SetParent(go.transform, false);

        if (GameArt.SpawnModel(Items.ModelName(type), visual, 0.45f) == null)
            BuildFallback(visual, type);
        visual.gameObject.AddComponent<Bobbing>();

        var pickup = go.AddComponent<ItemPickup>();
        pickup.Id = id;
        pickup.Type = type;
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = new Vector3(0f, 0.35f, 0f);
        col.size = new Vector3(0.7f, 0.7f, 0.7f);
        return pickup;
    }

    static void BuildFallback(Transform parent, ItemType type)
    {
        ItemDef def = Items.Get(type);
        Material body = GameArt.Lit("Item" + type, def.color, null, 0.8f);
        if (type == ItemType.Canard)
        {
            GameArt.Prim(PrimitiveType.Sphere, parent, new Vector3(0f, 0.15f, 0f), new Vector3(0.36f, 0.28f, 0.42f), body);
            GameArt.Prim(PrimitiveType.Sphere, parent, new Vector3(0f, 0.35f, 0.12f), Vector3.one * 0.22f, body);
            GameArt.Prim(PrimitiveType.Sphere, parent, new Vector3(0f, 0.33f, 0.25f), new Vector3(0.12f, 0.05f, 0.1f), GameArt.Lit("Beak", new Color(1f, 0.45f, 0.1f)));
        }
        else
        {
            GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.4f, 0.15f), body);
        }
    }

    public override string Prompt => Loc.F("Ramasser {0}", Items.Get(Type).name);
    public override void Interact() => GameSession.Instance.RequestPickup(this);
}

// Bouton Rouge au centre du Hub : seul l'hôte peut l'enclencher.
// Modèle Meshy (Resources/Art/RedButton) : podium de cirque rayé rouge et crème, gros dôme rouge sur le dessus.
// Enclenché, il s'enfonce dans le sol du chapiteau (dans un nuage de poussière) jusqu'à disparaître.
public class RedButton : Interactable
{
    const float SinkDepth = 1.3f, SinkDuration = 1.6f;
    bool raised = true;
    float sink;   // 0 : levé, 1 : sous le sol

    public override bool CanInteract => raised;

    // 'animate' : il descend lentement (sinon, il est aussitôt à sa place, par exemple pour un joueur qui arrive).
    public void SetRaised(bool on, bool animate)
    {
        if (on == raised) return;
        raised = on;
        foreach (var c in GetComponents<Collider>()) c.enabled = on;
        if (on || !animate || !gameObject.activeSelf)
        {
            sink = on ? 0f : 1f;
            Place();
            gameObject.SetActive(on);
            return;
        }
        gameObject.SetActive(true);
        Fx.Burst(transform.position + Vector3.up * 0.05f, Fx.Dirt, 18, 1.6f, 0.07f, 1f, 1.2f);
    }

    void Update()
    {
        if (raised || sink >= 1f) return;
        sink = Mathf.MoveTowards(sink, 1f, Time.deltaTime / SinkDuration);
        Place();
        if (sink >= 1f) gameObject.SetActive(false);
    }

    // Démarrage lent, comme une plateforme mécanique, puis accélération.
    void Place() => transform.localPosition = Vector3.down * (SinkDepth * sink * sink);

    public static RedButton Create(Transform anchor)
    {
        var go = new GameObject("Bouton Rouge");
        go.transform.SetParent(anchor, false);
        bool model = GameArt.SpawnModel("RedButton", go.transform, 1.1f) != null;
        if (!model)
        {
            Material hazard = GameArt.Lit("Hazard", Color.white, GameArt.Stripes(new Color(0.95f, 0.75f, 0.1f), new Color(0.08f, 0.08f, 0.08f)), 0.3f);
            GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.05f, 0.9f), hazard);
            GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.55f, 0.45f, 0.55f), GameArt.Metal);
            GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0.97f, 0f), new Vector3(0.7f, 0.03f, 0.7f), hazard);
            GameArt.Prim(PrimitiveType.Sphere, go.transform, new Vector3(0f, 1.02f, 0f), new Vector3(0.45f, 0.22f, 0.45f), GameArt.Emissive("ButtonRed", new Color(0.9f, 0.05f, 0.05f), new Color(0.6f, 0f, 0f)));
        }
        var button = go.AddComponent<RedButton>();
        button.FitTrigger(new Vector3(0.8f, 1f, 0.8f));
        // Socle solide pour ne pas traverser le bouton.
        var solid = go.AddComponent<CapsuleCollider>();
        solid.center = new Vector3(0f, 0.6f, 0f);
        solid.height = 1.2f;
        solid.radius = model ? 0.5f : 0.4f;
        return button;
    }

    public override string Prompt => Loc.T("Appuyer sur le Bouton Rouge");
    public override void Interact() => GameSession.Instance.PressButton();
}

// Bouton Skip (V0.4.6), sous le Rendu de Quota : apparaît quand le quota est atteint et passe à la manche suivante,
// comme la commande /skip round.
public class SkipButton : Interactable
{
    public static SkipButton Create(Transform anchor)
    {
        var go = new GameObject("Bouton Skip");
        go.transform.SetParent(anchor, false);
        // Plaque murale tournée vers le centre du Hub (+Z). Modèle Meshy (Resources/Art/BoutonSkip) : cadre doré à ampoules
        // de fête foraine, fond rayé rouge et crème, gros bouton vert en bas ; "SKIP" s'affiche au-dessus du bouton.
        var plate = GameArt.SpawnModel("BoutonSkip", go.transform, SkipPlateHeight);
        if (plate != null)
        {
            Bounds b = GameArt.LocalBounds(go.transform);
            plate.transform.localPosition = new Vector3(0f, -SkipPlateHeight * 0.5f, b.extents.z + 0.01f);
            GameArt.CreateText(go.transform, "Texte", new Vector3(0f, SkipPlateHeight * 0.2f, b.size.z + 0.03f), 0.045f,
                Color.white, TextAnchor.MiddleCenter, TextAlignment.Center).text = "<b>SKIP</b>";
        }
        else
        {
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, 0.05f), new Vector3(1.3f, 0.6f, 0.08f), GameArt.Lit("ScreenFrame", new Color(0.1f, 0.1f, 0.1f), null, 0.6f));
            var cap = GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, 0f, 0.1f), new Vector3(1.05f, 0.08f, 0.38f),
                GameArt.Emissive("SkipGreen", new Color(0.1f, 0.7f, 0.25f), new Color(0.05f, 0.5f, 0.15f)));
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GameArt.CreateText(go.transform, "Texte", new Vector3(0f, 0f, 0.2f), 0.05f, Color.white, TextAnchor.MiddleCenter, TextAlignment.Center).text = "SKIP";
        }
        foreach (var t in go.GetComponentsInChildren<TextMesh>()) t.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var button = go.AddComponent<SkipButton>();
        button.FitTrigger(new Vector3(1.3f, 0.6f, 0.4f));
        return button;
    }

    const float SkipPlateHeight = 0.65f;

    public override string Prompt => Loc.T("Passer à la manche suivante");
    public override void Interact()
    {
        string refusal = GameSession.Instance.RequestSkipRound();
        if (refusal != null) GameSession.Instance.Toast(refusal);
    }
}

// Terminal du Hub.
public class TerminalStation : Interactable
{
    public static TerminalStation Create(Transform anchor)
    {
        var go = new GameObject("Terminal");
        go.transform.SetParent(anchor, false);
        // Le clavier et l'écran du modèle Meshy sont vers son +Z, comme l'ancre qui regarde le centre du Hub.
        if (GameArt.SpawnModel("Terminal", go.transform, 1.4f) == null)
        {
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.4f, 0f), new Vector3(1.4f, 0.8f, 0.7f), GameArt.Metal);
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 1.1f, 0.05f), new Vector3(0.7f, 0.6f, 0.5f), GameArt.Lit("Beige", new Color(0.8f, 0.75f, 0.6f)));
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 1.1f, -0.21f), new Vector3(0.55f, 0.42f, 0.02f), GameArt.Emissive("CrtGreen", new Color(0.05f, 0.3f, 0.08f), new Color(0.1f, 0.6f, 0.15f)));
        }
        var station = go.AddComponent<TerminalStation>();
        station.FitTrigger(new Vector3(1f, 1.2f, 0.8f));
        var solid = go.AddComponent<BoxCollider>();
        Bounds b = GameArt.LocalBounds(go.transform);
        solid.center = b.center;
        solid.size = b.size * 0.9f;
        return station;
    }

    public override string Prompt => Loc.T("Utiliser le Terminal");
    public override void Interact() => GameSession.Instance.RequestTerminal();
}

// Chenil d'un joueur : affiche son état et permet de le réanimer quand il est mort.
public class Kennel : Interactable
{
    public string PlayerId { get; private set; }
    public string PlayerName;
    public bool TargetDead;
    TextMesh sign;

    public static Kennel Create(Transform parent, Vector3 pos, Quaternion rot, string playerId)
    {
        var go = new GameObject("Chenil");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        // L'ouverture du modèle Meshy est vers son +Z, comme le chenil qui regarde le centre du Hub.
        if (GameArt.SpawnModel("Kennel", go.transform, 1.3f) == null)
        {
            Material wood = GameArt.Lit("KennelWood", new Color(0.85f, 0.78f, 0.62f));
            Material roof = GameArt.Lit("KennelRoof", new Color(0.65f, 0.1f, 0.1f));
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.2f, 0.9f, 1.1f), wood);
            var r1 = GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(-0.32f, 1.05f, 0f), new Vector3(0.8f, 0.08f, 1.2f), roof);
            r1.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            var r2 = GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0.32f, 1.05f, 0f), new Vector3(0.8f, 0.08f, 1.2f), roof);
            r2.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.35f, 0.551f), new Vector3(0.5f, 0.6f, 0.01f), GameArt.Lit("KennelHole", new Color(0.05f, 0.04f, 0.03f)));
        }

        var kennel = go.AddComponent<Kennel>();
        kennel.PlayerId = playerId;
        kennel.FitTrigger(new Vector3(1.2f, 1.2f, 1.2f));
        var solid = go.AddComponent<BoxCollider>();
        Bounds b = GameArt.LocalBounds(go.transform);
        solid.center = b.center;
        solid.size = b.size * 0.85f;

        // Panneau au-dessus de la niche (lisible depuis le centre du Hub). Modèle Meshy (Resources/Art/PanneauChenil) :
        // enseigne de cirque à cadre doré et fond bleu nuit, tournée vers +Z, dont le petit pied rayé repose sur le toit.
        var board = new GameObject("Panneau").transform;
        board.SetParent(go.transform, false);
        if (GameArt.SpawnModel("PanneauChenil", board, SignHeight) != null)
        {
            board.localPosition = new Vector3(0f, SignBase, -0.2f);
            kennel.sign = GameArt.CreateText(board, "Texte", new Vector3(0f, SignHeight * 0.46f, SignFace), 0.028f, Color.white);
            kennel.sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
        else
        {
            board.localPosition = new Vector3(0f, 2.15f, -0.2f);
            board.localRotation = Quaternion.Euler(0f, 180f, 0f);
            GameArt.Prim(PrimitiveType.Cube, board, new Vector3(0f, 0f, 0.03f), new Vector3(1.9f, 1.05f, 0.04f), GameArt.Unlit("ScreenBg", new Color(0.02f, 0.03f, 0.04f)));
            GameArt.Prim(PrimitiveType.Cube, board, new Vector3(0f, -0.9f, 0.05f), new Vector3(0.06f, 0.8f, 0.06f), GameArt.Lit("HubGold", new Color(0.85f, 0.65f, 0.2f)));
            kennel.sign = GameArt.CreateText(board, "Texte", new Vector3(0f, 0f, -0.01f), 0.028f, Color.white);
        }
        return kennel;
    }

    // Enseigne : 1,6 m de haut (1,9 m de large), posée sur le toit de la niche (1,3 m) ; face avant du fond à -0,11 m.
    const float SignHeight = 1.6f, SignBase = 1.3f, SignFace = -0.09f;

    public void SetInfo(string playerName, int hp, float distance, bool dead)
    {
        PlayerName = playerName;
        TargetDead = dead;
        string status = dead ? "<color=#ff3030>" + Loc.T("MORT") + "</color>" : "<color=#30ff50>" + Loc.T("EN VIE") + "</color>";
        sign.text = "<b>" + playerName + "</b>\n" + Loc.F("PV : {0} / {1}", hp, GameRules.MaxHp) + "\n" + Loc.F("Distance : {0} m", Mathf.RoundToInt(distance)) + "\n" + status;
    }

    public override bool CanInteract => TargetDead;
    public override string Prompt => Loc.F("Réanimer {0}", PlayerName);
    public override void Interact() => GameSession.Instance.RequestRevive(PlayerId);
}

// Orbe globuleux devant la porte de la Zone 2 : terne au départ, il s'active à la fin d'une manche
// (lumière bleue, aura fantomatique, serrure visible) et se brise si un joueur y utilise une Clé.
public class ZoneOrb : Interactable
{
    public int Index { get; private set; }
    bool active, broken;
    GameObject model, aura, keyhole;
    Light glow;
    float pulse;

    public static ZoneOrb Create(Transform parent, int index, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject("Orbe_" + (index + 1));
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var orb = go.AddComponent<ZoneOrb>();
        orb.Index = index;

        // La serrure du modèle Meshy est vers son +Z, comme l'orbe qui tourne le dos à la porte.
        orb.model = GameArt.SpawnModel("Orb", go.transform, 1.35f);
        if (orb.model == null)
        {
            orb.model = new GameObject("Orbe");
            orb.model.transform.SetParent(go.transform, false);
            Material stone = GameArt.Lit("OrbStone", new Color(0.25f, 0.25f, 0.26f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
            Material tar = GameArt.Lit("OrbTar", new Color(0.06f, 0.06f, 0.07f), null, 0.7f);
            GameArt.Prim(PrimitiveType.Cylinder, orb.model.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.35f, 0.3f, 0.35f), stone);
            var rnd = new System.Random(index + 7);
            for (int k = 0; k < 9; k++)
            {
                Vector3 d = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f).normalized * 0.2f;
                GameArt.Prim(PrimitiveType.Sphere, orb.model.transform, new Vector3(0f, 0.95f, 0f) + d, Vector3.one * (0.3f + (float)rnd.NextDouble() * 0.12f), tar);
            }
        }
        Bounds b = GameArt.LocalBounds(go.transform);
        Vector3 orbCenter = new Vector3(0f, b.max.y - b.size.x * 0.45f, 0f);

        orb.aura = GameArt.Prim(PrimitiveType.Sphere, go.transform, orbCenter, Vector3.one * b.size.x * 1.25f, GameArt.GhostAura);
        orb.aura.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orb.aura.SetActive(false);

        // Serrure lumineuse sur l'avant, visible une fois l'orbe activé.
        orb.keyhole = new GameObject("Serrure");
        orb.keyhole.transform.SetParent(go.transform, false);
        orb.keyhole.transform.localPosition = orbCenter + Vector3.forward * (b.size.z * 0.5f + 0.02f);
        Material lockMat = GameArt.Emissive("OrbLock", new Color(0.1f, 0.2f, 0.35f), new Color(0.4f, 0.85f, 1.6f));
        GameArt.Prim(PrimitiveType.Cylinder, orb.keyhole.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.09f, 0.01f, 0.09f), lockMat).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameArt.Prim(PrimitiveType.Cube, orb.keyhole.transform, new Vector3(0f, -0.04f, 0f), new Vector3(0.045f, 0.1f, 0.02f), lockMat);
        orb.keyhole.SetActive(false);

        orb.glow = new GameObject("Lueur").AddComponent<Light>();
        orb.glow.transform.SetParent(go.transform, false);
        orb.glow.transform.localPosition = orbCenter + Vector3.forward * 0.6f;
        orb.glow.type = LightType.Point;
        orb.glow.color = new Color(0.35f, 0.65f, 1f);
        orb.glow.range = 7f;
        orb.glow.intensity = 1.6f;
        orb.glow.shadows = LightShadows.None;
        orb.glow.enabled = false;

        orb.FitTrigger(new Vector3(0.9f, 1.4f, 0.9f));
        var solid = go.AddComponent<CapsuleCollider>();
        solid.center = new Vector3(0f, b.center.y, 0f);
        solid.height = b.size.y;
        solid.radius = Mathf.Min(b.size.x, b.size.z) * 0.4f;
        return orb;
    }

    public bool Active => active && !broken;

    public void SetState(bool isActive, bool isBroken, bool animate)
    {
        if (isBroken && !broken && animate) Shatter();
        if (isActive && !active && !isBroken && animate) GameAudio.Play3D("orb_on", transform.position + Vector3.up, 0.9f, 30f, 0f);
        active = isActive;
        broken = isBroken;
        model.SetActive(!broken);
        bool lit = active && !broken;
        aura.SetActive(lit);
        keyhole.SetActive(lit);
        glow.enabled = lit;
    }

    void Update()
    {
        if (!glow.enabled) return;
        pulse += Time.deltaTime;
        glow.intensity = 1.6f + Mathf.Sin(pulse * 2.3f) * 0.4f + Mathf.Sin(pulse * 7.1f) * 0.1f;
    }

    // Éclats qui retombent en s'éteignant.
    void Shatter()
    {
        GameAudio.Play3D("orb_break", transform.position + Vector3.up, 1f, 35f, 0.05f);
        Material shard = GameArt.Emissive("OrbShard", new Color(0.1f, 0.15f, 0.25f), new Color(0.3f, 0.6f, 1.2f));
        Vector3 center = aura.transform.position;
        for (int k = 0; k < 14; k++)
        {
            var piece = GameArt.Prim(PrimitiveType.Cube, null, center + Random.insideUnitSphere * 0.25f, Vector3.one * Random.Range(0.06f, 0.16f), shard, true);
            piece.transform.rotation = Random.rotation;
            var rb = piece.AddComponent<Rigidbody>();
            rb.velocity = Random.insideUnitSphere * 3f + Vector3.up * 2.5f;
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            piece.layer = 2;
            Destroy(piece, 4f);
        }
    }

    public override bool CanInteract => Active;
    // Pas de message à l'écran : viser l'orbe et cliquer avec la Clé suffit.
    public override string Prompt => "";
    public override void Interact() => GameSession.Instance.UseKeyOnOrb(Index);
}

// Post-It accroché à un mur par un joueur ; n'importe quel joueur peut le retirer.
public class PostItNote : Interactable
{
    public int Id { get; private set; }
    float placedAt;

    public static PostItNote Create(Transform parent, int id, Vector3 pos, Vector3 normal, string text)
    {
        var go = new GameObject("PostIt_" + id);
        go.transform.SetParent(parent, false);
        // Le texte se lit selon l'axe +Z : l'avant pointe vers le mur.
        go.transform.SetPositionAndRotation(pos + normal * 0.012f,
            Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0f, 0f, (id * 37 % 13) - 6f));
        GameArt.Prim(PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.32f, 0.32f, 0.01f), GameArt.Lit("PostIt", new Color(1f, 0.9f, 0.3f), null, 0.15f));
        GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.13f, -0.006f), new Vector3(0.32f, 0.06f, 0.002f), GameArt.Lit("PostItGlue", new Color(0.95f, 0.82f, 0.22f), null, 0.15f));
        var label = GameArt.CreateText(go.transform, "Message", new Vector3(0f, -0.01f, -0.008f), 0.0045f, new Color(0.12f, 0.1f, 0.25f));
        FitText(label, text, new Vector2(0.26f, 0.24f));
        var note = go.AddComponent<PostItNote>();
        note.Id = id;
        note.placedAt = Time.time;
        note.FitTrigger(new Vector3(0.36f, 0.36f, 0.12f));
        return note;
    }

    // Écrit le message aussi grand que possible dans la zone 'area' (en mètres) : on essaie chaque largeur de ligne,
    // on mesure le texte obtenu et on garde la mise en page qui autorise les plus gros caractères.
    static void FitText(TextMesh label, string text, Vector2 area)
    {
        const float probeSize = 0.01f;
        label.characterSize = probeSize;
        var renderer = label.GetComponent<MeshRenderer>();
        string best = text;
        float bestScale = 0f;
        for (int width = 1; width <= Items.PostItMaxChars; width++)
        {
            string wrapped = Wrap(text, width);
            label.text = wrapped;
            Vector3 size = renderer.localBounds.size;
            if (size.x < 1e-5f || size.y < 1e-5f) continue;
            float scale = Mathf.Min(area.x / size.x, area.y / size.y);
            if (scale > bestScale) { bestScale = scale; best = wrapped; }
        }
        label.text = best;
        label.characterSize = bestScale > 0f ? probeSize * bestScale : 0.0045f;
    }

    // Découpe le message en lignes d'au plus 'width' caractères, sans couper les mots si possible.
    static string Wrap(string text, int width)
    {
        var sb = new System.Text.StringBuilder();
        int line = 0;
        foreach (string word in text.Split(' '))
        {
            if (word.Length == 0) continue;
            if (line > 0 && line + 1 + word.Length > width) { sb.Append('\n'); line = 0; }
            else if (line > 0) { sb.Append(' '); line++; }
            sb.Append(word);
            line += word.Length;
        }
        return sb.ToString();
    }

    // Pas de retrait pendant les 2 secondes qui suivent la pose.
    public override bool CanInteract => Time.time - placedAt >= Items.PostItLockSeconds;
    public override string Prompt => Loc.T("Retirer le Post-It");
    public override void Interact() => GameSession.Instance.RemovePostIt(Id);
}
