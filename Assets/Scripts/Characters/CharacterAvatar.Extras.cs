using System.Collections.Generic;
using UnityEngine;

// V0.7.11 : ce que le personnage porte ou fait de plus, sur tous les skins (positions proportionnelles à la taille de sa tête et de
// ses bras, mesurées au chargement du skin) :
// - 3.6 Visage : quand un autre joueur est à moins de 7 m, la tête se tourne progressivement vers lui (le plus proche), de 45° au
//   plus à droite ou à gauche. V0.7.12 (Note 08) : le volume des têtes est multiplié par 2,7, de façon permanente.
// - 7.11 Mégaphone sélectionné : le bras droit monte, le Mégaphone devant la bouche.
// - 7.14 Collier à Laisse : autour du cou du joueur soumis.
// - 7.15 Lunettes PEACE dans l'inventaire : portées sur les yeux. V0.7.12 (Note 09) : posées entre les yeux du skin (os des yeux s'il
//   en a), elles suivent l'os de la tête (aboiement, attente, mort) ; à la mort, elles tombent au sol.
// - 7.16 Sac à Os dans l'inventaire : porté dans le dos.
public partial class CharacterAvatar
{
    public const float LookRange = 7f, LookMaxAngle = 45f, LookSpeed = 120f;
    static readonly Quaternion MegaphoneTurn = Quaternion.Euler(0f, -90f, 0f);
    const float GlassesDrop = 0.06f;
    // Volume des têtes (facteur d'échelle de l'os de la tête, appliqué en racine cubique du volume).
    public const float HeadVolume = 2.7f;

    bool wearGlasses, wearBag, holdMegaphone, wearCollar;
    GameObject glasses, bag, megaphone, collar;
    float lookYaw, megaphoneWeight;
    PlayerPresence self;
    float headSize = 0.35f;
    bool headMeasured, glassesCalibrated;
    // Position et orientation des Lunettes dans le repère de l'os de la tête.
    Vector3 glassesLocalPos;
    Quaternion glassesLocalRot = Quaternion.identity;
    float glassesWidth = -1f;

    public void SetEquipment(bool glassesOn, bool bagOn, bool megaphoneOn)
    {
        wearGlasses = glassesOn;
        wearBag = bagOn;
        holdMegaphone = megaphoneOn;
    }

    public void SetCollar(bool on) => wearCollar = on;

    // Point du cou (attache de la chaîne du Collier à Laisse).
    public Vector3 CollarPoint => neck != null ? neck.position : head != null ? head.position : transform.position + Vector3.up * 0.4f;
    // Pavillon du Mégaphone tenu (sinon, devant la bouche) : départ de l'onde de choc.
    public Vector3 MegaphoneMouth => megaphone != null && megaphone.activeInHierarchy
        ? megaphone.transform.position + model.transform.forward * (headSize * 0.5f)
        : head != null && model != null ? head.position + model.transform.forward * (headSize * 0.6f) : transform.position + Vector3.up * 0.6f;
    // Main droite (le maître tient la chaîne).
    public Vector3 HandPoint => hand != null ? hand.position : transform.position;

    void ResetExtras()
    {
        foreach (var go in new[] { glasses, bag, megaphone, collar }) if (go != null) Destroy(go);
        glasses = bag = megaphone = collar = null;
        headMeasured = false;
        glassesCalibrated = false;
        glassesWidth = -1f;
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
        // À la mort, les Lunettes portées tombent au sol (l'objet lui-même est lâché près du corps).
        if (dead && glasses != null && glasses.activeSelf) DropGlasses();
        if (!visible)
        {
            foreach (var go in new[] { glasses, bag, megaphone, collar }) if (go != null && go.activeSelf) go.SetActive(false);
            return;
        }
        CacheBones();
        if (!headMeasured) MeasureHead();
        if (head == null) return;
        head.localScale = Vector3.one * Mathf.Pow(HeadVolume, 1f / 3f);
        Transform m = model.transform;
        float k = head.lossyScale.y / Mathf.Max(0.0001f, m.lossyScale.y);
        float size = headSize * k;
        if (!glassesCalibrated) CalibrateGlasses(m, size);
        LookAtNearest();
        MegaphonePose();
        Place(ref glasses, wearGlasses, "Lunettes", head, glassesWidth > 0f ? glassesWidth * k : size * 0.75f, () => head.TransformPoint(glassesLocalPos), () => head.rotation * glassesLocalRot, BuildGlasses);
        Transform back = chest != null ? chest : spine;
        if (back != null)
            Place(ref bag, wearBag, "SacOs", back, armLength * 0.8f, () =>
                back.position - m.forward * (armLength * 0.32f) - m.up * (armLength * 0.1f), () => Quaternion.LookRotation(-m.forward, m.up), BuildBag);
        if (hand != null)
            // V0.7.12 (Note 11) : le pavillon du modèle est le long de son axe X : un quart de tour le tourne vers l'avant, l'embout
            // contre la bouche.
            Place(ref megaphone, holdMegaphone && megaphoneWeight > 0.5f, "Megaphone", hand, size * 0.9f, () =>
                hand.position + m.forward * (size * 0.45f), () => Quaternion.LookRotation(m.forward, m.up) * MegaphoneTurn, BuildMegaphone);
        Transform throat = neck != null ? neck : head;
        Place(ref collar, wearCollar, "Collier", throat, size * 0.75f, () => throat.position + m.up * (size * 0.05f),
            () => Quaternion.LookRotation(m.forward, m.up), BuildCollar);
    }

    // Repère des Lunettes, mesuré une fois sur le skin au repos (tête droite) : à hauteur des yeux (os des yeux s'il en a), posées sur
    // la surface du visage (le point le plus en avant du maillage à cette hauteur, autour des yeux), larges comme la tête à cette hauteur.
    void CalibrateGlasses(Transform m, float size)
    {
        glassesCalibrated = true;
        Transform le = animator.GetBoneTransform(HumanBodyBones.LeftEye), re = animator.GetBoneTransform(HumanBodyBones.RightEye);
        bool eyes = le != null && re != null && Vector3.Distance(le.position, re.position) > 0.01f;
        Vector3 eye = eyes ? (le.position + re.position) * 0.5f : head.position + m.up * (size * 0.42f);
        float span = eyes ? Vector3.Distance(le.position, re.position) : size * 0.4f;
        float front = eyes ? span * 0.9f : size * 0.48f, halfWidth = size * 0.5f;
        bool measured = false;
        float maxFront = float.MinValue, maxSide = 0f;
        var band = new List<Vector2>();   // (côté, avant) des points du maillage à hauteur des yeux
        var baked = new Mesh();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.BakeMesh(baked, true);
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 local in baked.vertices)
            {
                Vector3 d = toWorld.MultiplyPoint3x4(local) - eye;
                float up = Vector3.Dot(d, m.up), side = Vector3.Dot(d, m.right), fwd = Vector3.Dot(d, m.forward);
                if (Mathf.Abs(up) > size * 0.1f || fwd < -size) continue;
                if (Mathf.Abs(side) < span * 0.75f && fwd > maxFront) { maxFront = fwd; measured = true; }
                band.Add(new Vector2(Mathf.Abs(side), fwd));
            }
        }
        Destroy(baked);
        // Largeur du visage : seulement l'avant de la tête (ni les oreilles, ni la fourrure de l'arrière).
        foreach (var b in band) if (b.y > maxFront - size * 0.22f && b.x > maxSide) maxSide = b.x;
        if (measured) front = Mathf.Clamp(maxFront + size * 0.03f, 0f, size * 1.2f);
        if (maxSide > 0.01f) halfWidth = maxSide;
        glassesWidth = Mathf.Clamp(halfWidth * 2.2f, size * 0.6f, size * 1.2f);
        // (Un peu sous les os des yeux : les verres couvrent les yeux de tous les skins.)
        Vector3 pos = eye + m.forward * front - m.up * (size * GlassesDrop);
        glassesLocalPos = head.InverseTransformPoint(pos);
        glassesLocalRot = Quaternion.Inverse(head.rotation) * Quaternion.LookRotation(m.forward, m.up);
        // (La largeur est mesurée avec l'échelle actuelle de la tête ; les Lunettes suivent ensuite l'os.)
        glassesWidth /= Mathf.Max(0.0001f, head.lossyScale.y / Mathf.Max(0.0001f, m.lossyScale.y));
    }

    // Copie des Lunettes, détachée et lâchée (physique) ; elle disparaît après quelques secondes.
    void DropGlasses()
    {
        var fall = Instantiate(glasses, glasses.transform.position, glasses.transform.rotation);
        fall.name = "LunettesTombees";
        glasses.SetActive(false);
        foreach (var t in fall.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        var box = fall.AddComponent<BoxCollider>();
        Bounds b = GameArt.LocalBounds(fall.transform);
        box.center = b.center;
        box.size = Vector3.Max(b.size, Vector3.one * 0.03f);
        var body = fall.AddComponent<Rigidbody>();
        body.mass = 0.1f;
        body.velocity = Random.insideUnitSphere * 0.6f + Vector3.up * 0.8f;
        body.angularVelocity = Random.insideUnitSphere * 6f;
        Destroy(fall, 5f);
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

    static void BuildMegaphone(Transform holder, float size)
    {
        // (Le repli est construit le long de Z : on annule le quart de tour appliqué au modèle.)
        var t = new GameObject("Repli").transform;
        t.SetParent(holder, false);
        t.localRotation = Quaternion.Inverse(MegaphoneTurn);
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
