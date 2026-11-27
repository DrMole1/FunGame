using System.Collections.Generic;
using UnityEngine;

// V0.7.11 (11.3) : herses des remparts du Château. Fermées, elles bloquent le passage (entre l'intérieur du rempart et chaque côté
// de la zone) ; le levier du rempart les ouvre pour de bon : elles remontent lentement dans le mur.
public class CastleGate : MonoBehaviour
{
    const float OpenDuration = 3.5f, OpenGap = 0.3f;
    Transform grid;
    BoxCollider blocker;
    float height, openAmount;
    bool open;

    public bool IsOpen => open;
    public Collider Blocker => blocker;

    public static CastleGate Create(Transform parent, Vector3 pos, Quaternion rot, float width, float height)
    {
        var go = new GameObject("Herse");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var gate = go.AddComponent<CastleGate>();
        gate.height = height;
        gate.grid = new GameObject("Grille").transform;
        gate.grid.SetParent(go.transform, false);
        var model = GameArt.SpawnModel("Herse", gate.grid, height);
        if (model != null)
        {
            Bounds b = GameArt.LocalBounds(model.transform);
            model.transform.localScale = new Vector3(width / Mathf.Max(0.01f, b.size.x), 1f, 0.35f / Mathf.Max(0.01f, b.size.z));
        }
        else
        {
            // Grille de barreaux en damier noir et blanc (repli).
            Material bar = GameArt.CheckerWall;
            for (float x = -width * 0.5f + 0.2f; x <= width * 0.5f - 0.2f; x += 0.45f)
                GameArt.Prim(PrimitiveType.Cube, gate.grid, new Vector3(x, height * 0.5f, 0f), new Vector3(0.12f, height, 0.12f), bar);
            for (float y = 0.6f; y < height; y += 0.8f)
                GameArt.Prim(PrimitiveType.Cube, gate.grid, new Vector3(0f, y, 0f), new Vector3(width, 0.1f, 0.1f), bar);
        }
        foreach (var c in gate.grid.GetComponentsInChildren<Collider>()) Destroy(c);
        gate.blocker = go.AddComponent<BoxCollider>();
        gate.blocker.center = new Vector3(0f, height * 0.5f, 0f);
        gate.blocker.size = new Vector3(width + 0.2f, height, 0.5f);
        return gate;
    }

    public void SetOpen(bool value, bool instant)
    {
        if (value && !open && !instant) GameAudio.Play3D("zone_door", transform.position + Vector3.up * 2f, 0.9f, 50f, 0f, 0.8f);
        if (value != open) BeaconNetwork.SightChanged();
        open = value;
        blocker.enabled = !open;
        if (instant) Apply(open ? 1f : 0f);
    }

    // V0.7.13 (Note 22) : plus de bruit de porte ; une fois déverrouillée, la porte reste grande ouverte.
    void Update()
    {
        float goal = open ? 1f : 0f;
        if (Mathf.Approximately(openAmount, goal)) return;
        Apply(Mathf.MoveTowards(openAmount, goal, Time.deltaTime / OpenDuration));
    }

    void Apply(float amount)
    {
        openAmount = amount;
        // V0.7.15 (Note 04) : ouverte, la herse reste 1 m plus bas qu'avant (on la voit en haut du passage, on passe dessous).
        grid.localPosition = Vector3.up * Mathf.SmoothStep(0f, 1f, amount) * (height - OpenGap - 1f);
    }
}

// Levier d'un rempart (objet interactif) : actionné une fois, il ouvre les deux herses de son rempart ; il ne l'est plus ensuite.
// Ses deux câbles, noirs et peu visibles, deviennent blancs et lumineux. Note 10 des Notes Diverses : pas de message.
public class CastleLever : Interactable
{
    public int Id { get; private set; }
    public bool Pulled { get; private set; }
    readonly List<Renderer> cables = new List<Renderer>();
    Transform handle;

    public static CastleLever Create(Transform parent, int id, Vector3 pos, float yaw)
    {
        var go = new GameObject("Levier_" + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var lever = go.AddComponent<CastleLever>();
        lever.Id = id;
        Material iron = GameArt.Lit("LevierFer", new Color(0.18f, 0.18f, 0.19f), null, 0.5f);
        Material stone = GameArt.CheckerWall;
        GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.4f), stone);
        lever.handle = new GameObject("Manche").transform;
        lever.handle.SetParent(go.transform, false);
        lever.handle.localPosition = new Vector3(0f, 0.9f, 0f);
        lever.handle.localRotation = Quaternion.Euler(-35f, 0f, 0f);
        GameArt.Prim(PrimitiveType.Cylinder, lever.handle, new Vector3(0f, 0.4f, 0f), new Vector3(0.07f, 0.4f, 0.07f), iron);
        GameArt.Prim(PrimitiveType.Sphere, lever.handle, new Vector3(0f, 0.82f, 0f), Vector3.one * 0.16f, iron);
        foreach (var c in go.GetComponentsInChildren<Collider>()) if (!c.transform.IsChildOf(lever.handle)) { }
        lever.FitTrigger(new Vector3(0.8f, 1.8f, 0.8f));
        return lever;
    }

    public void AddCable(Renderer cable) => cables.Add(cable);

    public void SetPulled(bool on, bool animate)
    {
        if (on && !Pulled && animate) GameAudio.Play3D("lever", transform.position + Vector3.up, 1f, 30f, 0.05f);
        Pulled = on;
        handle.localRotation = Quaternion.Euler(on ? 35f : -35f, 0f, 0f);
        foreach (var c in cables) if (c != null) c.sharedMaterial = Glyph.CableMaterial(on);
    }

    public override string Prompt => "";
    public override bool CanInteract => !Pulled;
    public override void Interact() => GameSession.Instance.PullLever(this);
}

// V0.7.11 (11.4) : porte d'appartement des Blockhaus. V0.7.12 : il n'y en a plus qu'aux appartements fermés, sous cadenas : elle bloque le passage ; la Clé sélectionnée la rend interactive, et l'ouvrir enlève le cadenas et ouvre la
// porte (la Clé disparaît).
public class ApartmentDoor : Interactable
{
    public int Id { get; private set; }
    public bool Locked { get; private set; }
    Transform leaf;
    BoxCollider blocker;
    GameObject padlock;
    float angle, target;
    const float Ajar = 18f, Pushed = 95f;

    public static ApartmentDoor Create(Transform parent, int id, Vector3 pos, Quaternion rot, float width, float height, bool locked)
    {
        var go = new GameObject("PorteAppartement_" + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var door = go.AddComponent<ApartmentDoor>();
        door.Id = id;
        door.Locked = locked;
        // Battant pivotant sur son côté gauche.
        door.leaf = new GameObject("Battant").transform;
        door.leaf.SetParent(go.transform, false);
        door.leaf.localPosition = new Vector3(-width * 0.5f, 0f, 0f);
        // V0.7.12 (Note 21) : vieille porte usée en damier noir et blanc (modèle Meshy), étirée aux dimensions du passage.
        var model = GameArt.SpawnModel("PorteAppart", door.leaf, height);
        if (model != null)
        {
            // (Bornes dans le repère du modèle : si sa largeur est le long de Z, il est tourné d'un quart de tour.)
            Bounds b = GameArt.LocalBounds(model.transform);
            bool turned = b.size.z > b.size.x;
            float across = Mathf.Max(0.01f, turned ? b.size.z : b.size.x), thick = Mathf.Max(0.01f, turned ? b.size.x : b.size.z);
            if (turned) model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            model.transform.localScale = turned ? new Vector3(0.08f / thick, 1f, width / across) : new Vector3(width / across, 1f, 0.08f / thick);
            model.transform.localPosition = new Vector3(width * 0.5f, 0f, 0f);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        else
        {
            Material wood = GameArt.Lit("PorteBlockhaus", new Color(0.32f, 0.3f, 0.26f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.3f);
            var slab = GameArt.Prim(PrimitiveType.Cube, door.leaf, new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(width, height, 0.06f), wood);
            foreach (var c in slab.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        if (locked)
        {
            // V0.7.21 (Note 14) : un vrai cadenas de fer rouillé (celui des boîtiers de verre des glyphes), accroché à la poignée,
            // visible des deux côtés de la porte.
            door.padlock = new GameObject("Cadenas");
            door.padlock.transform.SetParent(door.leaf, false);
            foreach (float side in new[] { 1f, -1f })
            {
                var lockPart = Glyph.BuildPadlock(door.padlock.transform, new Vector3(width * 0.88f, height * 0.45f, side * 0.075f));
                if (side < 0f) lockPart.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            foreach (var c in door.padlock.GetComponentsInChildren<Collider>()) Destroy(c);
            door.blocker = go.AddComponent<BoxCollider>();
            door.blocker.center = new Vector3(0f, height * 0.5f, 0f);
            door.blocker.size = new Vector3(width, height, 0.3f);
        }
        door.angle = door.target = locked ? 0f : Ajar;
        door.leaf.localRotation = Quaternion.Euler(0f, door.angle, 0f);
        door.FitTrigger(new Vector3(width, height, 0.6f));
        return door;
    }

    public void Unlock(bool animate)
    {
        if (!Locked) return;
        Locked = false;
        if (blocker != null) blocker.enabled = false;
        // V0.7.21 (Note 14) : le cadenas saute de la poignée et retombe au sol.
        if (padlock != null)
        {
            if (animate) JumpPadlock();
            padlock.SetActive(false);
        }
        if (animate) GameAudio.Play3D("key", transform.position + Vector3.up, 0.8f, 15f, 0.05f);
        target = Pushed;
        RefreshOutline();
    }

    void JumpPadlock()
    {
        foreach (Transform part in padlock.transform)
        {
            var flying = Instantiate(part.gameObject, part.position, part.rotation);
            flying.transform.localScale = part.lossyScale;
            flying.layer = 2;
            var box = flying.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.07f, 0f);
            box.size = new Vector3(0.12f, 0.12f, 0.05f);
            var rb = flying.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.velocity = part.forward * Random.Range(0.8f, 1.6f) + Vector3.up * Random.Range(2.5f, 3.5f);
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            Destroy(flying, 4f);
        }
    }

    void Update()
    {
        angle = Mathf.MoveTowards(angle, target, (target > angle ? 240f : 60f) * Time.deltaTime);
        leaf.localRotation = Quaternion.Euler(0f, angle, 0f);
    }

    public override string Prompt => "";
    public override bool CanInteract => Locked && GameSession.Instance != null && (GameSession.Instance.SelectedItem == ItemType.Cle || GameSession.Instance.Godmode);
    public override void Interact() => GameSession.Instance.UnlockDoor(this);
}
