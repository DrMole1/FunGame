using System.Collections.Generic;
using UnityEngine;

// Objet interactif : visé au centre de l'écran et assez proche, son contour s'illumine (côté client uniquement).
public abstract class Interactable : MonoBehaviour
{
    Renderer[] renderers;
    Material[][] originals;
    bool highlighted;

    public abstract string Prompt { get; }
    public virtual bool CanInteract => true;
    public abstract void Interact();

    public void SetHighlight(bool on)
    {
        if (on == highlighted) return;
        highlighted = on;
        if (on)
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
                mats[mats.Length - 1] = GameArt.Outline;
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

        string model = type == ItemType.Canard ? "Duck" : "Croquette";
        if (GameArt.SpawnModel(model, visual, 0.45f) == null)
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

    public override string Prompt => "Ramasser " + Items.Get(Type).name;
    public override void Interact() => GameSession.Instance.RequestPickup(this);
}

// Bouton Rouge au centre du Hub : seul l'hôte peut l'enclencher.
public class RedButton : Interactable
{
    public static RedButton Create(Transform anchor)
    {
        var go = new GameObject("Bouton Rouge");
        go.transform.SetParent(anchor, false);
        if (GameArt.SpawnModel("RedButton", go.transform, 1.1f) == null)
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
        solid.radius = 0.4f;
        return button;
    }

    public override string Prompt => "Appuyer sur le Bouton Rouge";
    public override void Interact() => GameSession.Instance.PressButton();
}

// Terminal du Hub.
public class TerminalStation : Interactable
{
    public static TerminalStation Create(Transform anchor)
    {
        var go = new GameObject("Terminal");
        go.transform.SetParent(anchor, false);
        var model = GameArt.SpawnModel("Terminal", go.transform, 1.4f);
        if (model != null)
        {
            // L'écran du modèle Meshy regarde vers son -Z : on le tourne vers le centre du Hub.
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
        else
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

    public override string Prompt => "Utiliser le Terminal";
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
        var model = GameArt.SpawnModel("Kennel", go.transform, 1.3f);
        if (model == null)
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
        else
        {
            // Le modèle Meshy a l'entrée vers son -Z : on le tourne vers le centre du Hub.
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        var kennel = go.AddComponent<Kennel>();
        kennel.PlayerId = playerId;
        kennel.FitTrigger(new Vector3(1.2f, 1.2f, 1.2f));
        var solid = go.AddComponent<BoxCollider>();
        Bounds b = GameArt.LocalBounds(go.transform);
        solid.center = b.center;
        solid.size = b.size * 0.85f;

        // Panneau au-dessus de la niche (lisible depuis le centre du Hub).
        var board = new GameObject("Panneau").transform;
        board.SetParent(go.transform, false);
        board.localPosition = new Vector3(0f, 2.15f, -0.2f);
        board.localRotation = Quaternion.Euler(0f, 180f, 0f);
        GameArt.Prim(PrimitiveType.Cube, board, new Vector3(0f, 0f, 0.03f), new Vector3(1.9f, 1.05f, 0.04f), GameArt.Unlit("ScreenBg", new Color(0.02f, 0.03f, 0.04f)));
        GameArt.Prim(PrimitiveType.Cube, board, new Vector3(0f, -0.9f, 0.05f), new Vector3(0.06f, 0.8f, 0.06f), GameArt.Lit("HubGold", new Color(0.85f, 0.65f, 0.2f)));
        kennel.sign = GameArt.CreateText(board, "Texte", new Vector3(0f, 0f, -0.01f), 0.022f, Color.white);
        return kennel;
    }

    public void SetInfo(string playerName, int hp, float distance, bool dead)
    {
        PlayerName = playerName;
        TargetDead = dead;
        string status = dead ? "<color=#ff3030>MORT</color>" : "<color=#30ff50>EN VIE</color>";
        sign.text = "<b>" + playerName + "</b>\nPV : " + hp + " / " + GameRules.MaxHp + "\nDistance : " + Mathf.RoundToInt(distance) + " m\n" + status;
    }

    public override bool CanInteract => TargetDead;
    public override string Prompt => "Réanimer " + PlayerName;
    public override void Interact() => GameSession.Instance.RequestRevive(PlayerId);
}
