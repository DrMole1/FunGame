using UnityEngine;

// V0.7.11 : ce que le personnage porte ou fait de plus, sur tous les skins (positions proportionnelles à la taille de sa tête et de
// ses bras, mesurées au chargement du skin) :
// - 3.6 Visage : quand un autre joueur est à moins de 7 m, la tête se tourne progressivement vers lui (le plus proche), de 45° au
//   plus à droite ou à gauche. "/head N" multiplie le volume des têtes de tous les personnages (1 à 3).
// - 7.11 Mégaphone sélectionné : le bras droit monte, le Mégaphone devant la bouche.
// - 7.14 Collier à Laisse : autour du cou du joueur soumis.
// - 7.15 Lunettes PEACE dans l'inventaire : portées sur les yeux.
// - 7.16 Sac à Os dans l'inventaire : porté dans le dos.
public partial class CharacterAvatar
{
    public const float LookRange = 7f, LookMaxAngle = 45f, LookSpeed = 120f;
    // "/head" : volume des têtes (facteur d'échelle de l'os de la tête, appliqué en racine cubique du volume).
    public static float HeadVolume = 1f;

    bool wearGlasses, wearBag, holdMegaphone, wearCollar;
    GameObject glasses, bag, megaphone, collar;
    float lookYaw, megaphoneWeight;
    PlayerPresence self;
    float headSize = 0.35f;
    bool headMeasured;

    public void SetEquipment(bool glassesOn, bool bagOn, bool megaphoneOn)
    {
        wearGlasses = glassesOn;
        wearBag = bagOn;
        holdMegaphone = megaphoneOn;
    }

    public void SetCollar(bool on) => wearCollar = on;

    // Point du cou (attache de la chaîne du Collier à Laisse).
    public Vector3 CollarPoint => neck != null ? neck.position : head != null ? head.position : transform.position + Vector3.up * 0.4f;
    // Main droite (le maître tient la chaîne).
    public Vector3 HandPoint => hand != null ? hand.position : transform.position;

    void ResetExtras()
    {
        foreach (var go in new[] { glasses, bag, megaphone, collar }) if (go != null) Destroy(go);
        glasses = bag = megaphone = collar = null;
        headMeasured = false;
        lookYaw = 0f;
    }

    // Taille de la tête : du pied de l'os de la tête au sommet du modèle.
    void MeasureHead()
    {
        headMeasured = true;
        if (head == null || model == null) return;
        Bounds b = new Bounds(head.position, Vector3.zero);
        bool any = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (any) headSize = Mathf.Clamp(b.max.y - head.position.y, 0.15f, 1.2f);
    }

    void UpdateExtras()
    {
        bool visible = !dead && !hidden && model != null && model.activeInHierarchy && animator != null && animator.isHuman;
        if (!visible)
        {
            foreach (var go in new[] { glasses, bag, megaphone, collar }) if (go != null && go.activeSelf) go.SetActive(false);
            return;
        }
        CacheBones();
        if (!headMeasured) MeasureHead();
        if (head == null) return;
        if (!Mathf.Approximately(HeadVolume, 1f) || head.localScale != Vector3.one)
            head.localScale = Vector3.one * Mathf.Pow(Mathf.Clamp(HeadVolume, 1f, 3f), 1f / 3f);
        LookAtNearest();
        MegaphonePose();
        Transform m = model.transform;
        float k = head.lossyScale.y / Mathf.Max(0.0001f, m.lossyScale.y);
        float size = headSize * k;
        Place(ref glasses, wearGlasses, "Lunettes", head, size * 0.75f, () =>
            head.position + m.up * (size * 0.42f) + m.forward * (size * 0.48f), () => Quaternion.LookRotation(m.forward, m.up), BuildGlasses);
        Transform back = chest != null ? chest : spine;
        if (back != null)
            Place(ref bag, wearBag, "SacOs", back, armLength * 0.8f, () =>
                back.position - m.forward * (armLength * 0.32f) - m.up * (armLength * 0.1f), () => Quaternion.LookRotation(-m.forward, m.up), BuildBag);
        if (hand != null)
            Place(ref megaphone, holdMegaphone && megaphoneWeight > 0.5f, "Megaphone", hand, size * 0.9f, () =>
                hand.position + m.forward * (size * 0.25f), () => Quaternion.LookRotation(m.forward, m.up), BuildMegaphone);
        Transform throat = neck != null ? neck : head;
        Place(ref collar, wearCollar, "Collier", throat, size * 0.75f, () => throat.position + m.up * (size * 0.05f),
            () => Quaternion.LookRotation(m.forward, m.up), BuildCollar);
    }

    // Objet attaché : créé à la demande (modèle Meshy de Resources/Art, sinon des primitives), placé chaque image.
    void Place(ref GameObject item, bool on, string modelName, Transform bone, float size, System.Func<Vector3> position,
        System.Func<Quaternion> rotation, System.Action<Transform, float> fallback)
    {
        if (!on)
        {
            if (item != null && item.activeSelf) item.SetActive(false);
            return;
        }
        if (item == null)
        {
            item = new GameObject(modelName);
            item.transform.SetParent(transform, false);
            if (GameArt.SpawnModelFit(modelName, item.transform, size) == null) fallback(item.transform, size);
            foreach (var c in item.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in item.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetLayerRecursive(item, gameObject.layer);
        }
        if (!item.activeSelf) item.SetActive(true);
        item.transform.SetPositionAndRotation(position(), rotation());
    }

    // 3.6 : la tête se tourne vers le joueur le plus proche à moins de 7 m (45° au plus).
    void LookAtNearest()
    {
        if (self == null) self = GetComponentInParent<PlayerPresence>();
        Transform m = model.transform;
        Vector3 from = head.position;
        float best = LookRange * LookRange, target = 0f;
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || p == self || !p.isActiveAndEnabled || !p.Alive) continue;
            Vector3 d = p.Position + Vector3.up * 0.5f - from;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq >= best || sq < 0.01f) continue;
            best = sq;
            target = Mathf.Clamp(Vector3.SignedAngle(new Vector3(m.forward.x, 0f, m.forward.z), d, Vector3.up), -LookMaxAngle, LookMaxAngle);
        }
        lookYaw = Mathf.MoveTowards(lookYaw, target, LookSpeed * Time.deltaTime);
        if (Mathf.Abs(lookYaw) > 0.01f)
        {
            if (neck != null) Tilt(neck, m.up, lookYaw * 0.35f);
            Tilt(head, m.up, lookYaw * (neck != null ? 0.65f : 1f));
        }
    }

    // 7.11 : le bras droit monte, la main devant la bouche.
    void MegaphonePose()
    {
        megaphoneWeight = Mathf.MoveTowards(megaphoneWeight, holdMegaphone ? 1f : 0f, Time.deltaTime * 5f);
        if (megaphoneWeight <= 0f || upperArm == null || lowerArm == null || hand == null) return;
        Transform m = model.transform;
        Vector3 mouth = head.position + m.up * (headSize * 0.2f) + m.forward * (headSize * 0.35f) + m.right * (headSize * 0.05f);
        Vector3 pole = upperArm.position - m.up * armLength + m.right * armLength * 0.6f;
        SolveArm(upperArm, lowerArm, hand, mouth, pole, megaphoneWeight);
    }

    // ---------- Replis en primitives (si le modèle Meshy manque) ----------

    static void BuildGlasses(Transform t, float size)
    {
        Material frame = GameArt.Lit("LunettesMonture", new Color(1f, 0.45f, 0.75f), null, 0.6f);
        Material lens = GameArt.Lit("LunettesVerre", new Color(1f, 0.55f, 0.8f), null, 0.9f);
        foreach (float sx in new[] { -1f, 1f })
        {
            var l = GameArt.Prim(PrimitiveType.Cylinder, t, new Vector3(sx * size * 0.22f, 0f, 0f), new Vector3(size * 0.32f, 0.005f, size * 0.32f), lens);
            l.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        GameArt.Prim(PrimitiveType.Cube, t, Vector3.zero, new Vector3(size * 0.12f, size * 0.03f, size * 0.03f), frame);
    }

    static void BuildBag(Transform t, float size)
    {
        GameArt.Prim(PrimitiveType.Cube, t, Vector3.zero, new Vector3(size * 0.6f, size * 0.8f, size * 0.3f), GameArt.Lit("SacOsToile", new Color(0.5f, 0.38f, 0.24f), null, 0.2f));
        GameArt.Prim(PrimitiveType.Cube, t, new Vector3(0f, 0f, size * 0.16f), new Vector3(size * 0.4f, size * 0.12f, size * 0.02f), GameArt.Lit("SacOsOs", new Color(0.95f, 0.93f, 0.85f), null, 0.2f));
    }

    static void BuildMegaphone(Transform t, float size)
    {
        var cone = GameArt.Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0f, size * 0.3f), new Vector3(size * 0.35f, size * 0.3f, size * 0.35f), GameArt.Lit("MegaphoneBlanc", new Color(0.9f, 0.9f, 0.88f), null, 0.5f));
        cone.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameArt.Prim(PrimitiveType.Cube, t, new Vector3(0f, -size * 0.15f, size * 0.1f), new Vector3(size * 0.08f, size * 0.25f, size * 0.08f), GameArt.Lit("MegaphonePoignee", new Color(0.1f, 0.1f, 0.1f), null, 0.3f));
    }

    static void BuildCollar(Transform t, float size)
    {
        var ring = GameArt.MeshObject("Anneau", t, ItemPickup.Torus(size * 0.32f, size * 0.05f, 24, 8), GameArt.Lit("CollierCuir", new Color(0.3f, 0.18f, 0.1f), null, 0.3f));
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI * 2f / 8f;
            var spike = GameArt.Prim(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size * 0.37f, Vector3.one * size * 0.06f, GameArt.Metal);
            spike.transform.localScale = new Vector3(size * 0.04f, size * 0.04f, size * 0.08f);
        }
    }
}
