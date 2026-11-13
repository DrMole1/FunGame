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
    protected virtual bool HasOutline(Renderer r) => true;

    public abstract string Prompt { get; }
    public virtual bool CanInteract => true;
    // Simple information (rien à actionner) : le texte s'affiche sans touche, et un clic ne fait rien de spécial.
    public virtual bool InfoOnly => false;
    public abstract void Interact();

    public void SetHighlight(bool on)
    {
        highlighted = on;
        UpdateOutline();
    }

    // À rappeler quand les renderers qui portent le contour changent pendant que l'objet est visé.
    protected void RefreshOutline()
    {
        bool was = highlighted;
        SetHighlight(false);
        SetHighlight(was);
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
                if (!(r is ParticleSystemRenderer) && r.GetComponent<TextMesh>() == null && HasOutline(r)) list.Add(r);
            renderers = list.ToArray();
            originals = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                originals[i] = renderers[i].sharedMaterials;
                var mats = new Material[originals[i].Length + 1];
                originals[i].CopyTo(mats, 0);
                mats[mats.Length - 1] = GameArt.WhiteOutline;
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

        if (GameArt.SpawnModel(Items.ModelName(type), visual, ModelHeight(type)) == null)
            BuildFallback(visual, type);
        // V0.6.1 : un même modèle teinté pour les quatre tickets et les trois boules à facettes ; V0.6.3 : canards colorés.
        Items.ApplyLook(visual, type);
        // La Clé est couchée à plat sur le sol, immobile, centrée sur le point de chute. Son orientation dépend de l'id :
        // la même pour tous les joueurs.
        if (type == ItemType.Cle)
        {
            visual.localRotation = Quaternion.Euler(0f, (id * 137) % 360, 0f) * Quaternion.Euler(90f, 0f, 0f);
            visual.localPosition = Vector3.up * 0.02f + visual.localRotation * new Vector3(0f, -0.24f, 0f);
        }
        else visual.gameObject.AddComponent<Bobbing>();
        if (Items.Dances(type)) DiscoDancer.Attach(visual);
        // Lot gagné à une borne d'arcade ou Canard Coloré : il scintille de petites particules (un objet précieux).
        if (Items.IsPrize(type) || Items.IsColoredDuck(type)) Fx.Sparkle(go.transform, Items.Get(type).color);

        var pickup = go.AddComponent<ItemPickup>();
        pickup.Id = id;
        pickup.Type = type;
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = new Vector3(0f, 0.35f, 0f);
        col.size = new Vector3(0.7f, 0.7f, 0.7f);
        return pickup;
    }

    // Hauteur du modèle à terre (le ticket est posé à plat, le Jeton WOOF debout comme une pièce).
    static float ModelHeight(ItemType type) =>
        Items.IsTicket(type) ? 0.05f : type == ItemType.JetonWoof ? 0.24f : Items.IsDiscoBall(type) ? 0.68f : Items.IsColoredDuck(type) ? Beacon.ColoredDuckHeight : 0.45f;

    static void BuildFallback(Transform parent, ItemType type)
    {
        ItemDef def = Items.Get(type);
        if (type == ItemType.Cle)
        {
            BuildKey(parent);
            return;
        }
        // V0.6.14 : la Carte Magnétique 1, debout comme les autres objets à terre.
        if (type == ItemType.CarteMagnetique1)
        {
            var card = new GameObject("Carte").transform;
            card.SetParent(parent, false);
            card.localPosition = new Vector3(0f, 0.22f, 0f);
            SecretCorpse.BuildCard(card, 0.32f);
            return;
        }
        Material body = GameArt.Lit("Item" + type, def.color, null, 0.8f);
        if (Items.IsDuck(type))
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

    // Clé (d'après son icône) : vieille clé de fer rouillé, debout. Anneau ovale en haut, collerette, tige ronde, panneton
    // crénelé en bas. Environ 45 cm, comme les autres objets à terre.
    static Mesh keyRing;
    static Texture2D rust;

    // Fer rouillé de la Clé (aussi celui des serrures des boîtiers de verre).
    public static Material KeyIron
    {
        get
        {
            Material iron = GameArt.Lit("CleRouille", Color.white, RustTexture(), 0.18f);
            iron.SetFloat("_Metallic", 0.45f);
            return iron;
        }
    }

    static void BuildKey(Transform parent)
    {
        Material iron = KeyIron;
        if (keyRing == null) keyRing = Torus(0.068f, 0.02f, 28, 10);

        var ring = new GameObject("Anneau", typeof(MeshFilter), typeof(MeshRenderer));
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = new Vector3(0f, 0.355f, 0f);
        ring.transform.localScale = new Vector3(1f, 1.2f, 1f);
        ring.GetComponent<MeshFilter>().sharedMesh = keyRing;
        ring.GetComponent<MeshRenderer>().sharedMaterial = iron;

        // Collerette sous l'anneau, tige, bout arrondi.
        GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.262f, 0f), new Vector3(0.052f, 0.014f, 0.052f), iron);
        GameArt.Prim(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.15f, 0f), new Vector3(0.034f, 0.11f, 0.034f), iron);
        GameArt.Prim(PrimitiveType.Sphere, parent, new Vector3(0f, 0.04f, 0f), Vector3.one * 0.036f, iron);
        // Panneton : une plaque et ses dents.
        GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0.042f, 0.085f, 0f), new Vector3(0.052f, 0.075f, 0.016f), iron);
        GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0.078f, 0.06f, 0f), new Vector3(0.024f, 0.025f, 0.016f), iron);
        GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0.078f, 0.108f, 0f), new Vector3(0.024f, 0.025f, 0.016f), iron);
        GameArt.Prim(PrimitiveType.Cube, parent, new Vector3(0.055f, 0.038f, 0f), new Vector3(0.022f, 0.02f, 0.016f), iron);
    }

    // Tore dans le plan XY (l'anneau se voit de face, de profil quand l'objet tourne).
    public static Mesh Torus(float radius, float tube, int segments, int sides)
    {
        var verts = new Vector3[(segments + 1) * (sides + 1)];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            for (int j = 0; j <= sides; j++)
            {
                float b = j * Mathf.PI * 2f / sides;
                Vector3 n = dir * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                int k = i * (sides + 1) + j;
                verts[k] = dir * radius + n * tube;
                normals[k] = n;
                uvs[k] = new Vector2((float)i / segments * 3f, (float)j / sides);
            }
        }
        var tris = new int[segments * sides * 6];
        int t = 0;
        for (int i = 0; i < segments; i++)
            for (int j = 0; j < sides; j++)
            {
                int a0 = i * (sides + 1) + j, a1 = a0 + sides + 1;
                tris[t++] = a0; tris[t++] = a1; tris[t++] = a0 + 1;
                tris[t++] = a1; tris[t++] = a1 + 1; tris[t++] = a0 + 1;
            }
        var mesh = new Mesh { name = "AnneauCle", vertices = verts, normals = normals, uv = uvs, triangles = tris };
        mesh.RecalculateBounds();
        return mesh;
    }

    // Fer sombre taché de rouille orangée.
    static Texture2D RustTexture()
    {
        if (rust != null) return rust;
        const int size = 64;
        rust = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, name = "Rouille" };
        var px = new Color[size * size];
        Color dark = new Color(0.16f, 0.14f, 0.13f), mid = new Color(0.3f, 0.2f, 0.15f), orange = new Color(0.55f, 0.28f, 0.12f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.11f + 7.3f, y * 0.11f + 2.1f) * 0.65f + Mathf.PerlinNoise(x * 0.37f, y * 0.37f + 9f) * 0.35f;
                px[y * size + x] = n < 0.55f ? Color.Lerp(dark, mid, n / 0.55f) : Color.Lerp(mid, orange, Mathf.Clamp01((n - 0.55f) / 0.25f));
            }
        rust.SetPixels(px);
        rust.Apply(true);
        return rust;
    }

    public override string Prompt => Loc.F("Ramasser {0}", Items.Get(Type).name);
    public override void Interact() => GameSession.Instance.RequestPickup(this);
}

// Bouton Rouge au centre du Hub : dans le Hub 1, seul l'hôte peut l'enclencher (il démarre la partie). V0.6 : dans les
// Hubs suivants, il ouvre seulement la porte vers la zone suivante, sans démarrer de manche.
// Modèle Meshy (Resources/Art/RedButton) : podium de cirque rayé rouge et crème, gros dôme rouge sur le dessus.
// Enclenché, il s'enfonce dans le sol du chapiteau (dans un nuage de poussière) jusqu'à disparaître.
public class RedButton : Interactable
{
    const float SinkDepth = 1.3f, SinkDuration = 1.6f;
    public int Hub { get; private set; } = 1;
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

    public static RedButton Create(Transform anchor, int hub = 1)
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
        button.Hub = hub;
        button.FitTrigger(new Vector3(0.8f, 1f, 0.8f));
        // Socle solide pour ne pas traverser le bouton.
        var solid = go.AddComponent<CapsuleCollider>();
        solid.center = new Vector3(0f, 0.6f, 0f);
        solid.height = 1.2f;
        solid.radius = model ? 0.5f : 0.4f;
        return button;
    }

    public override string Prompt => Loc.T("Appuyer sur le Bouton Rouge");
    public override void Interact() => GameSession.Instance.PressButton(Hub);
}

// Bouton Skip (V0.4.6), sous le Rendu de Quota : apparaît quand le quota est atteint et passe à la manche suivante,
// avec 10 croquettes par tranche de 10 s restantes. V0.6 : des confettis explosent joyeusement à côté de lui.
public class SkipButton : Interactable
{
    public int Hub { get; private set; } = 1;

    public static SkipButton Create(Transform anchor, int hub = 1)
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
            // Mot "SKIP" en 3D (Meshy, police soviétique du titre), en relief sur la plaque, au-dessus du bouton.
            var word = GameArt.SpawnModel("Decor/TexteSkip", go.transform, SkipWordHeight);
            if (word != null)
            {
                float depth = GameArt.LocalBounds(word.transform).size.z;
                word.transform.localPosition = new Vector3(0f, SkipPlateHeight * 0.2f - SkipWordHeight * 0.5f, b.size.z + depth * 0.5f);
                // Livré lisible depuis son -Z : demi-tour pour se lire depuis le centre du Hub.
                word.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var white = GameArt.Emissive("SkipBlanc", new Color(0.95f, 0.95f, 0.92f), new Color(0.4f, 0.4f, 0.38f));
                foreach (var r in word.GetComponentsInChildren<Renderer>()) r.sharedMaterial = white;
            }
            else
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
        button.Hub = hub;
        button.FitTrigger(new Vector3(1.3f, 0.6f, 0.4f));
        return button;
    }

    const float SkipPlateHeight = 0.65f, SkipWordHeight = 0.16f;

    public override string Prompt => Loc.T("Passer à la manche suivante");
    public override void Interact()
    {
        string refusal = GameSession.Instance.RequestSkipRound(Hub);
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

// Chenil d'un joueur (un dans chaque Hub) : affiche son état et permet de le réanimer quand il est mort. V0.6 (note 05 du
// chapitre 4) : son propriétaire peut rentrer dedans (objet interactif pour lui seul).
public class Kennel : Interactable
{
    public string PlayerId { get; private set; }
    public int Hub { get; private set; } = 1;
    public string PlayerName;
    public bool TargetDead;
    TextMesh sign;

    public static Kennel Create(Transform parent, Vector3 pos, Quaternion rot, string playerId, int hub = 1)
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
        kennel.Hub = hub;
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

    bool Own => NetworkGame.Instance != null && PlayerId == NetworkGame.Instance.MyId;

    // Intérieur de la niche, d'où le propriétaire regarde dehors.
    public Vector3 Eye => transform.TransformPoint(new Vector3(0f, 0.5f, 0.38f));

    public override bool CanInteract => TargetDead || Own;
    public override string Prompt => Own ? Loc.T("Entrer dans votre Chenil") : Loc.F("Réanimer {0}", PlayerName);
    public override void Interact()
    {
        if (Own) GameSession.Instance.EnterKennel(this);
        else GameSession.Instance.RequestRevive(PlayerId, Hub);
    }
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
