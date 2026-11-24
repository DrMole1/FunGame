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
    const float GlassesDrop = 0.06f, GlassesMaxWidth = 0.85f;
    // V0.7.13 (Note 09) : hauteur des yeux propre à chaque skin (aucun n'a d'os des yeux), en part de la taille de la tête.
    // V0.7.14 : réglages des Lunettes par skin : hauteur (part de la taille de la tête), taille, recul après l'ajustement au
    // visage (mètres, positif : plus loin des yeux) et part centrale ignorée par l'ajustement (0 : rien n'entre dans le skin).
    // 0 Snoops, 1 Grumble, 2 Princess, 3 Jerk, 4 Karen, 5 Peepoo, 6 Gladys, 7 Mirkov.
    static readonly float[] GlassesLiftBySkin = { 0f, -0.04f, 0f, 0.12f, -0.08f, -0.14f, -0.05f, -0.05f };
    static readonly float[] GlassesScaleBySkin = { 1.25f, 1.1f, 1f, 1f, 1f, 1f, 1f, 0.85f };
    static readonly float[] GlassesOffsetBySkin = { 0f, 0f, 0f, -0.02f, 0.015f, 0f, 0.03f, 0.015f };
    static float GlassesLift(int skin) => skin >= 0 && skin < GlassesLiftBySkin.Length ? GlassesLiftBySkin[skin] : 0f;
    static float GlassesScale(int skin) => skin >= 0 && skin < GlassesScaleBySkin.Length ? GlassesScaleBySkin[skin] : 1f;
    static float GlassesOffset(int skin) => skin >= 0 && skin < GlassesOffsetBySkin.Length ? GlassesOffsetBySkin[skin] : 0f;
    static float GlassesBridgeOf(int skin) => skin == 0 ? 0f : GlassesBridge;
    // V0.7.13 (Note 07) : le Mégaphone a la même taille pour tous les skins (plus petit qu'avant), tenu devant le museau, sans le
    // toucher ('MegaphoneGap' entre le bout du museau et l'embout).
    const float MegaphoneSize = 0.3f, MegaphoneGap = 0.08f;
    // Bout du museau (à hauteur de la bouche), dans le repère de l'os de la tête ; mesuré avec les Lunettes.
    Vector3 snoutLocal;
    bool snoutMeasured;
    // V0.7.14 : le Mégaphone est reculé vers la bouche au plus près, sans toucher le visage (tous les skins sauf Jerk).
    // Relief du visage, mesuré une fois par skin bras baissé : pour chaque case de 1 cm (côté, hauteur) autour de la place
    // habituelle du Mégaphone, le point le plus en avant du maillage. 'megaphoneShift' : déplacement du Mégaphone le long du
    // regard depuis cette place (négatif : reculé vers la bouche).
    const float FaceCell = 0.01f, MegaphoneClearance = 0.01f;
    // V0.7.14 : recul supplémentaire vers la bouche, au-delà de la mesure (Mégaphone un peu plus près).
    const float MegaphoneExtraBack = 0.02f;
    // Snoops, Grumble, Princess, Peepoo et Gladys (0, 1, 2, 5, 6) : 8 cm de recul supplémentaire au total (2 + 6).
    static float MegaphoneSkinBack(int skin) => skin == 0 || skin == 1 || skin == 2 || skin == 5 || skin == 6 ? 0.06f : 0f;
    const int FaceCellReach = 1;   // cases voisines prises en compte (maillages peu denses)
    readonly Dictionary<long, float> faceFront = new Dictionary<long, float>();
    bool faceMeasured, megaphoneFitted;
    float megaphoneShift;
    static long FaceKey(int side, int up) => ((long)side << 32) ^ (uint)up;
    // V0.7.13 (Note 11) : Sac à Os plaqué contre le dos mesuré du skin (point le plus en arrière du torse, dans le repère de l'os du
    // torse), plus gros pour Peepoo et Princess.
    Vector3 backLocal;
    float torsoHalf;
    bool backMeasured, backTried;
    static float BagScale(int skin) => skin == 2 || skin == 5 ? 1.45f : 1f;   // Princess, Peepoo
    // V0.7.14 : Sac à Os ajusté sur le skin (sauf Princess, inchangée) : le haut des bretelles sur les épaules, le panneau du sac
    // (côté dos) collé au dos. Position du sac par rapport à l'os du torse, dans le repère du personnage (hauteur, avant).
    const int PrincessSkin = 2;
    bool bagFitted, bagOk;   // ajustement tenté (une fois), réussi
    // Place du sac dans le repère de l'os du torse (fixée une fois le sac posé) : il suit les gestes et l'animation.
    bool bagAnchored;
    Vector3 bagAnchorLocal;
    Quaternion bagAnchorRot = Quaternion.identity;
    float bagUp, bagFwd;
    // Hauteur du dessus des épaules au-dessus de l'articulation de l'épaule (part de la longueur du bras), et enfoncement du
    // panneau dans le dos (pour qu'il colle sans jour).
    const float ShoulderTop = 0.12f, BagSink = 0.005f;
    // Peepoo : sa collerette dépasse de 5 cm du dos en haut du sac et le tenait à l'écart ; le sac passe dessus (contre le dos).
    static float BagExtraSink(int skin) => skin == 5 ? 0.05f : 0f;
    // Volume des têtes (facteur d'échelle de l'os de la tête, appliqué en racine cubique du volume).
    public const float HeadVolume = 2.7f;

    // Centre de la tête (hauteur des yeux), pour cadrer le skin au zoom du lobby. Null tant que la tête n'est pas trouvée.
    // Point fixe par rapport au personnage, mesuré sur le skin au repos : il ne suit ni les mouvements de tête ni l'animation
    // idle (la caméra du lobby ne bouge pas avec eux).
    Vector3 restHeadLocal;
    public Vector3? HeadCenter
    {
        get
        {
            if (head == null || model == null) return null;
            if (glassesCalibrated) return transform.TransformPoint(restHeadLocal);
            float k = head.lossyScale.y / Mathf.Max(0.0001f, model.transform.lossyScale.y);
            return head.position + model.transform.up * (headSize * k * 0.42f);
        }
    }

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
        ? megaphone.transform.position + model.transform.forward * (MegaphoneSize * 0.5f)
        : head != null && model != null ? head.position + model.transform.forward * (headSize * 0.6f) : transform.position + Vector3.up * 0.6f;
    // Main droite (le maître tient la chaîne).
    public Vector3 HandPoint => hand != null ? hand.position : transform.position;

    void ResetExtras()
    {
        foreach (var go in new[] { glasses, bag, megaphone, collar }) if (go != null) Destroy(go);
        glasses = bag = megaphone = collar = null;
        headMeasured = false;
        glassesCalibrated = false;
        snoutMeasured = false;
        faceMeasured = megaphoneFitted = false;
        bagFitted = bagOk = bagAnchored = false;
        glassesFitted = false;
        backMeasured = backTried = false;
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
        // Relief du visage pour le Mégaphone : une fois par skin, bras baissé (V0.7.14).
        if (!faceMeasured && glassesCalibrated && megaphoneWeight <= 0f && CharacterIndex != JerkSkin) MeasureFace(m, size);
        MegaphonePose();
        Place(ref glasses, wearGlasses, "Lunettes", head, (glassesWidth > 0f ? glassesWidth * k : size * 0.75f) * GlassesScale(CharacterIndex),() => head.TransformPoint(glassesLocalPos), () => head.rotation * glassesLocalRot, BuildGlasses);
        if (!glassesFitted && glasses != null && glasses.activeSelf && glassesCalibrated && CharacterIndex != PrincessSkin) FitGlasses();
        Transform back = chest != null ? chest : spine;
        if (back != null)
        {
            if (!backTried) MeasureBack(m, back);
            float bagSize = Mathf.Max(armLength * 0.8f, torsoHalf * 1.5f) * BagScale(CharacterIndex);
            Place(ref bag, wearBag, "SacOs", back, bagSize, () =>
            {
                // V0.7.14 : une fois posé, le sac est accroché à l'os du torse : il suit les gestes et l'animation.
                if (bagAnchored) return back.TransformPoint(bagAnchorLocal);
                float halfDepth = bag != null ? GameArt.LocalBounds(bag.transform).extents.z : bagSize * 0.2f;
                Vector3 surface = backMeasured ? back.TransformPoint(backLocal) : back.position - m.forward * (armLength * 0.22f);
                return surface - m.forward * (halfDepth + 0.02f) - m.up * (armLength * 0.08f);
            }, () => bagAnchored ? back.rotation * bagAnchorRot : Quaternion.LookRotation(-m.forward, m.up), BuildBag);
            if (!bagFitted && bag != null && bag.activeSelf && CharacterIndex != PrincessSkin) FitBag(m, back);
            if (!bagAnchored && bag != null && bag.activeSelf && (bagFitted || CharacterIndex == PrincessSkin))
            {
                // V0.7.14 : sac ajusté (tous les skins sauf Princess) : bretelles sur les épaules, sac collé au dos.
                Vector3 at = bagOk && CharacterIndex != PrincessSkin ? back.position + m.up * bagUp + m.forward * bagFwd : bag.transform.position;
                bagAnchorLocal = back.InverseTransformPoint(at);
                bagAnchorRot = Quaternion.Inverse(back.rotation) * bag.transform.rotation;
                bag.transform.position = at;
                bagAnchored = true;
            }
        }
        if (hand != null)
            // V0.7.12 (Note 11) : le pavillon du modèle est le long de son axe X : un quart de tour le tourne vers l'avant, l'embout
            // vers la bouche. V0.7.13 : devant le bout du museau, à distance.
            // V0.7.14 : orienté comme la tête (il suit l'aboiement, les gestes et l'animation).
            Place(ref megaphone, holdMegaphone && megaphoneWeight > 0.5f, "Megaphone", hand, MegaphoneSize, () => MegaphoneCenter(m, size),
                () => FaceRotation(m) * MegaphoneTurn, BuildMegaphone);
        if (!megaphoneFitted && faceMeasured && megaphone != null && megaphone.activeSelf) FitMegaphone(m);
        Transform throat = neck != null ? neck : head;
        Place(ref collar, wearCollar, "Collier", throat, size * 0.75f, () => throat.position + m.up * (size * 0.05f),
            () => Quaternion.LookRotation(m.forward, m.up), BuildCollar);
    }

    // Repère des Lunettes, mesuré une fois sur le skin au repos (tête droite) : à hauteur des yeux (os des yeux s'il en a), posées sur
    // la surface du visage (le point le plus en avant du maillage à cette hauteur, autour des yeux), larges comme la tête à cette hauteur.
    void CalibrateGlasses(Transform m, float size)
    {
        glassesCalibrated = true;
        faceMeasured = megaphoneFitted = glassesFitted = false;
        Transform le = animator.GetBoneTransform(HumanBodyBones.LeftEye), re = animator.GetBoneTransform(HumanBodyBones.RightEye);
        bool eyes = le != null && re != null && Vector3.Distance(le.position, re.position) > 0.01f;
        Vector3 eye = eyes ? (le.position + re.position) * 0.5f : head.position + m.up * (size * 0.42f);
        float span = eyes ? Vector3.Distance(le.position, re.position) : size * 0.4f;
        float front = eyes ? span * 0.9f : size * 0.48f, halfWidth = size * 0.5f;
        bool measured = false;
        float maxFront = float.MinValue, maxSide = 0f, snoutFront = float.MinValue, snoutUp = 0f;
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
                // Museau : le point le plus en avant sous les yeux, au milieu du visage.
                if (up < size * 0.05f && up > -size * 0.6f && Mathf.Abs(side) < size * 0.3f && fwd > snoutFront) { snoutFront = fwd; snoutUp = up; }
                if (Mathf.Abs(up) > size * 0.1f || fwd < -size) continue;
                // V0.7.13 (Note 09) : devant les yeux (de part et d'autre du museau), pas au bout du museau.
                bool eyeColumn = Mathf.Abs(side) > size * 0.12f && Mathf.Abs(side) < size * 0.32f;
                if (eyeColumn && fwd > maxFront) { maxFront = fwd; measured = true; }
                band.Add(new Vector2(Mathf.Abs(side), fwd));
            }
        }
        Destroy(baked);
        // Largeur du visage : seulement l'avant de la tête (ni les oreilles, ni la fourrure de l'arrière).
        foreach (var b in band) if (b.y > maxFront - size * 0.15f && b.x > maxSide) maxSide = b.x;
        if (measured) front = Mathf.Clamp(maxFront + size * 0.03f, 0f, size * 1.2f);
        if (maxSide > 0.01f) halfWidth = maxSide;
        glassesWidth = Mathf.Clamp(halfWidth * 2.1f, size * 0.55f, size * GlassesMaxWidth);
        // (Un peu sous les os des yeux : les verres couvrent les yeux de tous les skins.)
        Vector3 pos = eye + m.forward * front - m.up * (size * (GlassesDrop - GlassesLift(CharacterIndex)));
        if (snoutFront > float.MinValue)
        {
            snoutLocal = head.InverseTransformPoint(eye + m.forward * Mathf.Max(snoutFront, front) + m.up * Mathf.Min(snoutUp, -size * 0.1f));
            snoutMeasured = true;
        }
        glassesLocalPos = head.InverseTransformPoint(pos);
        restHeadLocal = transform.InverseTransformPoint(pos);
        glassesLocalRot = Quaternion.Inverse(head.rotation) * Quaternion.LookRotation(m.forward, m.up);
        // (La largeur est mesurée avec l'échelle actuelle de la tête ; les Lunettes suivent ensuite l'os.)
        glassesWidth /= Mathf.Max(0.0001f, head.lossyScale.y / Mathf.Max(0.0001f, m.lossyScale.y));
    }

    // Dos du skin (au repos) : le point le plus en arrière du maillage à hauteur du torse, et la demi-largeur du torse.
    void MeasureBack(Transform m, Transform back)
    {
        backMeasured = backTried = true;
        float minBack = float.MaxValue, half = 0f;
        var baked = new Mesh();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.BakeMesh(baked, true);
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 local in baked.vertices)
            {
                Vector3 d = toWorld.MultiplyPoint3x4(local) - back.position;
                float up = Vector3.Dot(d, m.up), side = Vector3.Dot(d, m.right), fwd = Vector3.Dot(d, m.forward);
                if (Mathf.Abs(up) > armLength * 0.35f || Mathf.Abs(side) > armLength * 0.6f) continue;
                if (Mathf.Abs(side) < armLength * 0.2f && fwd < minBack) minBack = fwd;
                if (Mathf.Abs(fwd) < armLength * 0.3f && Mathf.Abs(side) > half) half = Mathf.Abs(side);
            }
        }
        Destroy(baked);
        torsoHalf = half > 0.01f ? half : armLength * 0.3f;
        if (minBack < float.MaxValue && minBack < 0f) backLocal = back.InverseTransformPoint(back.position + m.forward * minBack);
        else backMeasured = false;
    }

    // V0.7.14 : ajustement du Sac à Os, une fois par skin, d'après son maillage (posé à sa place habituelle) et celui du skin.
    // Le haut des bretelles (boucles côté dos) est mis à hauteur du dessus des épaules, puis le panneau du sac (côté dos, au
    // milieu) est plaqué contre le point le plus en arrière du dos sur sa hauteur. Sans maillage lisible : place habituelle.
    void FitBag(Transform m, Transform back)
    {
        bagFitted = true;
        Vector3 pivot = bag.transform.position;
        var pts = new List<Vector3>();
        foreach (var mf in bag.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
            foreach (Vector3 v in mf.sharedMesh.vertices)
            {
                Vector3 d = mf.transform.TransformPoint(v) - pivot;
                pts.Add(new Vector3(Vector3.Dot(d, m.right), Vector3.Dot(d, m.up), Vector3.Dot(d, m.forward)));
            }
        }
        if (pts.Count == 0) return;
        float width = 0f, minUp = float.MaxValue, maxUp = float.MinValue;
        foreach (var p in pts) { width = Mathf.Max(width, Mathf.Abs(p.x)); minUp = Mathf.Min(minUp, p.y); maxUp = Mathf.Max(maxUp, p.y); }
        // Panneau côté dos : milieu du sac (ni les bretelles sur les côtés, ni la poignée du haut, ni le fond).
        float panelFwd = float.MinValue, panelLow = float.MaxValue, panelHigh = float.MinValue;
        float h = maxUp - minUp;
        foreach (var p in pts)
            if (Mathf.Abs(p.x) < width * 0.2f && p.y > minUp + h * 0.2f && p.y < maxUp - h * 0.2f)
            {
                panelFwd = Mathf.Max(panelFwd, p.z);
                panelLow = Mathf.Min(panelLow, p.y);
                panelHigh = Mathf.Max(panelHigh, p.y);
            }
        if (panelFwd == float.MinValue) return;
        // Haut du corps du sac (sans les boucles des bretelles du modèle ni la poignée du milieu).
        float bodyTop = float.MinValue;
        foreach (var p in pts) if (p.z <= panelFwd + h * 0.03f && Mathf.Abs(p.x) > width * 0.3f) bodyTop = Mathf.Max(bodyTop, p.y);
        if (bodyTop == float.MinValue) bodyTop = maxUp;
        // Épaules (articulations) : le haut du sac est à leur hauteur.
        Transform rs = upperArm, ls = leftUpperArm;
        Vector3 shoulders = rs != null && ls != null ? (rs.position + ls.position) * 0.5f
            : rs != null ? rs.position : back.position + m.up * (armLength * 0.3f);
        float shoulderU = Vector3.Dot(shoulders - back.position, m.up);
        float shoulderHalf = rs != null && ls != null ? Mathf.Abs(Vector3.Dot(rs.position - ls.position, m.right)) * 0.5f : torsoHalf;
        float up = shoulderU - bodyTop;
        // Maillage du skin au repos, dans le repère du personnage (côté, hauteur, avant) depuis l'os du torse.
        var skin = new List<Vector3>();
        var baked = new Mesh();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // Sans l'échelle du renderer, replacé avec sa position et sa rotation (juste aussi pour un modèle à l'échelle 0,01).
            smr.BakeMesh(baked);
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 local in baked.vertices)
            {
                Vector3 d = toWorld.MultiplyPoint3x4(local) - back.position;
                skin.Add(new Vector3(Vector3.Dot(d, m.right), Vector3.Dot(d, m.up), Vector3.Dot(d, m.forward)));
            }
        }
        Destroy(baked);
        // Dos du skin sur la hauteur du panneau, au milieu.
        float reach = Mathf.Min(width * 0.5f, torsoHalf * 0.5f), backFwd = float.MaxValue;
        foreach (var v in skin)
            if (Mathf.Abs(v.x) <= reach && v.y >= up + panelLow && v.y <= up + panelHigh && v.z <= 0f) backFwd = Mathf.Min(backFwd, v.z);
        if (backFwd == float.MaxValue) return;
        bagUp = up;
        bagFwd = backFwd + BagSink + BagExtraSink(CharacterIndex) - panelFwd;
        bagOk = true;
        // Bretelles : celles du modèle (boucles derrière le sac) sont retirées, deux bretelles suivent le skin par-dessus les
        // épaules jusqu'au-devant du torse.
        HideModelStraps(m, h);
        Vector3 pivotNew = back.position + m.up * bagUp + m.forward * bagFwd;
        foreach (float sgn in new[] { -1f, 1f })
        {
            float xs = sgn * Mathf.Min(shoulderHalf * StrapSpread, width * 0.9f);
            var path = StrapPath(skin, xs, new Vector2(bagUp + bodyTop - h * 0.06f, bagFwd + panelFwd), shoulderU);
            if (path != null) BuildStrap(m, path, xs, back.position, pivotNew, width * StrapWidth);
        }
    }

    // Bretelles du Sac à Os (V0.7.14) : position sur l'épaule (part de la demi-largeur des épaules), largeur (part de la
    // demi-largeur du sac), épaisseur, et écart au-dessus du skin.
    const float StrapSpread = 0.6f, StrapWidth = 0.32f, StrapThick = 0.012f, StrapLift = 0.006f;
    static readonly Dictionary<Mesh, Mesh> straplessBags = new Dictionary<Mesh, Mesh>();

    // Retire du maillage du sac les boucles de bretelles (tout ce qui dépasse du panneau vers le dos) : copie partagée.
    void HideModelStraps(Transform m, float bagHeight)
    {
        foreach (var mf in bag.GetComponentsInChildren<MeshFilter>())
        {
            Mesh src = mf.sharedMesh;
            if (src == null || !src.isReadable) continue;
            if (!straplessBags.TryGetValue(src, out Mesh clean))
            {
                Vector3 toBody = mf.transform.InverseTransformDirection(m.forward).normalized;
                Vector3[] v = src.vertices;
                Bounds b = src.bounds;
                float panel = float.MinValue;
                for (int i = 0; i < v.Length; i++)
                    if (Mathf.Abs(v[i].x - b.center.x) < b.extents.x * 0.2f && Mathf.Abs(v[i].y - b.center.y) < b.extents.y * 0.6f)
                        panel = Mathf.Max(panel, Vector3.Dot(v[i], toBody));
                float limit = panel + b.size.magnitude * 0.02f;
                clean = Instantiate(src);
                clean.name = src.name + "_SansBretelles";
                for (int sub = 0; sub < src.subMeshCount; sub++)
                {
                    int[] tri = src.GetTriangles(sub);
                    var kept = new List<int>(tri.Length);
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                        if (Vector3.Dot(v[tri[i]], toBody) <= limit && Vector3.Dot(v[tri[i + 1]], toBody) <= limit && Vector3.Dot(v[tri[i + 2]], toBody) <= limit)
                        { kept.Add(tri[i]); kept.Add(tri[i + 1]); kept.Add(tri[i + 2]); }
                    clean.SetTriangles(kept, sub);
                }
                straplessBags[src] = clean;
            }
            mf.sharedMesh = clean;
        }
    }

    // Trajet d'une bretelle dans le plan (hauteur, avant) à la position latérale xs : depuis le haut du panneau du sac ('start'),
    // autour du profil du skin (balayage angulaire depuis un centre dans l'épaule), par-dessus l'épaule et jusqu'au-devant du
    // torse, un peu sous l'aisselle. Points (côté, hauteur, avant) depuis l'os du torse.
    List<Vector3> StrapPath(List<Vector3> skin, float xs, Vector2 start, float shoulderU)
    {
        const float SliceHalf = 0.02f;
        // Corps seulement : rien au-dessus de la base du cou (la tête, très grosse, déborde sur les épaules).
        float neckU = neck != null ? Vector3.Dot(neck.position - (chest != null ? chest : spine).position, model.transform.up)
            : shoulderU + armLength * 0.25f;
        var slice = new List<Vector2>();
        foreach (var v in skin) if (Mathf.Abs(v.x - xs) < SliceHalf && v.y < neckU) slice.Add(new Vector2(v.y, v.z));
        if (slice.Count < 10) return null;
        // Centre : à mi-épaisseur du torse, un peu sous l'épaule.
        float cu = shoulderU - armLength * 0.1f, fMin = float.MaxValue, fMax = float.MinValue;
        foreach (var s in slice) if (Mathf.Abs(s.x - cu) < armLength * 0.08f) { fMin = Mathf.Min(fMin, s.y); fMax = Mathf.Max(fMax, s.y); }
        if (fMin == float.MaxValue) return null;
        var o = new Vector2(cu, (fMin + fMax) * 0.5f);
        // Rayon du profil par tranche d'angle (0° : devant, 90° : dessus, 180° : derrière).
        const int Step = 6;
        float startAngle = Mathf.Atan2(start.x - o.x, start.y - o.y) * Mathf.Rad2Deg;
        if (startAngle < 0f) startAngle += 360f;
        startAngle = Mathf.Clamp(startAngle, 120f, 230f);
        const float EndAngle = -55f;
        var angles = new List<float>();
        for (float a = startAngle; a >= EndAngle; a -= Step) angles.Add(a);
        var radius = new float[angles.Count];
        for (int i = 0; i < angles.Count; i++)
        {
            radius[i] = -1f;
            foreach (var s in slice)
            {
                Vector2 d = s - o;
                float a = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(a, angles[i])) <= Step * 0.75f) radius[i] = Mathf.Max(radius[i], d.magnitude);
            }
        }
        // Trous (maillage peu dense) : valeur voisine.
        for (int i = 0; i < radius.Length; i++)
            if (radius[i] < 0f)
                for (int k = 1; k < radius.Length && radius[i] < 0f; k++)
                {
                    if (i - k >= 0 && radius[i - k] >= 0f) radius[i] = radius[i - k];
                    else if (i + k < radius.Length && radius[i + k] >= 0f) radius[i] = radius[i + k];
                }
        // Lissage (la bretelle ne suit pas chaque creux du maillage : elle passe sur les bosses).
        var bumps = new float[radius.Length];
        for (int i = 0; i < radius.Length; i++)
        {
            float best = radius[i];
            for (int k = -1; k <= 1; k++) if (i + k >= 0 && i + k < radius.Length) best = Mathf.Max(best, radius[i + k]);
            bumps[i] = best;
        }
        // Puis moyenne glissante (pas de zigzag), sans jamais repasser sous le profil.
        var smooth = new float[radius.Length];
        for (int i = 0; i < radius.Length; i++)
        {
            float sum = 0f; int n = 0;
            for (int k = -2; k <= 2; k++) if (i + k >= 0 && i + k < radius.Length && bumps[i + k] >= 0f) { sum += bumps[i + k]; n++; }
            smooth[i] = n > 0 ? Mathf.Max(sum / n, radius[i]) : -1f;
        }
        var path = new List<Vector3> { new Vector3(xs, start.x, start.y) };
        for (int i = 0; i < angles.Count; i++)
        {
            if (smooth[i] < 0f) continue;
            float a = angles[i] * Mathf.Deg2Rad, r = smooth[i] + StrapLift + StrapThick * 0.5f;
            path.Add(new Vector3(xs, o.x + Mathf.Sin(a) * r, o.y + Mathf.Cos(a) * r));
        }
        return path.Count >= 3 ? path : null;
    }

    // Bande (largeur 'width', épaisseur StrapThick) le long du trajet, enfant du sac (elle le suit).
    void BuildStrap(Transform m, List<Vector3> path, float xs, Vector3 origin, Vector3 bagPivot, float width)
    {
        var go = new GameObject("Bretelle");
        // Au pivot du sac (à sa nouvelle place, qu'il rejoint à l'image suivante) : sommets dans son repère.
        go.transform.SetParent(bag.transform, false);
        Quaternion toLocal = Quaternion.Inverse(bag.transform.rotation);
        Vector3 scale = bag.transform.lossyScale;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        int n = path.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = origin + m.right * path[i].x + m.up * path[i].y + m.forward * path[i].z;
            Vector3 tangent = (path[Mathf.Min(i + 1, n - 1)] - path[Mathf.Max(i - 1, 0)]);
            Vector3 t = (m.right * tangent.x + m.up * tangent.y + m.forward * tangent.z).normalized;
            Vector3 outward = Vector3.Cross(m.right, t).normalized;   // perpendiculaire au trajet, hors du corps
            for (int c = 0; c < 4; c++)
            {
                float sx = (c == 0 || c == 3) ? -0.5f : 0.5f, so = c < 2 ? 0.5f : -0.5f;
                Vector3 l = toLocal * (p + m.right * (sx * width) + outward * (so * StrapThick) - bagPivot);
                verts.Add(new Vector3(l.x / scale.x, l.y / scale.y, l.z / scale.z));
            }
        }
        for (int i = 0; i + 1 < n; i++)
            for (int c = 0; c < 4; c++)
            {
                int a = i * 4 + c, b = i * 4 + (c + 1) % 4, a2 = a + 4, b2 = b + 4;
                tris.AddRange(new[] { a, b2, a2, a, b, b2 });   // faces tournées vers l'extérieur de la bande
            }
        var mesh = new Mesh { name = "Bretelle" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = GameArt.Lit("SacOsBretelle", new Color(0.24f, 0.13f, 0.07f), null, 0.25f);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetLayerRecursive(go, gameObject.layer);
    }

    // V0.7.14 : Lunettes au plus près des yeux sans que les verres y entrent (tous les skins sauf Princess) : une fois posées, elles
    // sont avancées ou reculées le long du regard jusqu'à ce que chaque point des verres soit à 'GlassesClearance' devant le
    // relief du visage juste derrière lui (le pont, sur le museau, n'est pas compté).
    // 'GlassesBridge' : part centrale des Lunettes (pont et intérieur des verres, devant la base du museau) qui n'est pas comptée.
    const float GlassesClearance = 0.004f, GlassesCell = 0.006f, GlassesBridge = 0.5f;
    bool glassesFitted;

    void FitGlasses()
    {
        glassesFitted = true;
        Transform g = glasses.transform;
        Vector3 o = g.position, right = g.right, upDir = g.up, fwd = g.forward;
        var lens = new List<Vector3>();
        float halfW = 0f, halfH = 0f;
        foreach (var mf in glasses.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
            foreach (Vector3 v in mf.sharedMesh.vertices)
            {
                Vector3 d = mf.transform.TransformPoint(v) - o;
                var p = new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, upDir), Vector3.Dot(d, fwd));
                lens.Add(p);
                halfW = Mathf.Max(halfW, Mathf.Abs(p.x));
                halfH = Mathf.Max(halfH, Mathf.Abs(p.y));
            }
        }
        if (lens.Count == 0) return;
        // Relief du visage derrière les Lunettes : point le plus en avant par case (côté, hauteur).
        var face = new Dictionary<long, float>();
        var baked = new Mesh();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // Sans l'échelle du renderer, replacé avec sa position et sa rotation (juste aussi pour un modèle à l'échelle 0,01).
            smr.BakeMesh(baked);
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 local in baked.vertices)
            {
                Vector3 d = toWorld.MultiplyPoint3x4(local) - o;
                float s = Vector3.Dot(d, right), u = Vector3.Dot(d, upDir), f = Vector3.Dot(d, fwd);
                if (Mathf.Abs(s) > halfW + GlassesCell * 2f || Mathf.Abs(u) > halfH + GlassesCell * 2f || Mathf.Abs(f) > halfW * 2f) continue;
                long key = FaceKey(Mathf.FloorToInt(s / GlassesCell), Mathf.FloorToInt(u / GlassesCell));
                if (!face.TryGetValue(key, out float best) || f > best) face[key] = f;
            }
        }
        Destroy(baked);
        float shift = float.MinValue;
        foreach (var p in lens)
        {
            if (Mathf.Abs(p.x) < halfW * GlassesBridgeOf(CharacterIndex)) continue;
            int cs = Mathf.FloorToInt(p.x / GlassesCell), cu = Mathf.FloorToInt(p.y / GlassesCell);
            float behind = float.MinValue;
            for (int a = -1; a <= 1; a++)
                for (int c = -1; c <= 1; c++)
                    if (face.TryGetValue(FaceKey(cs + a, cu + c), out float f) && f > behind) behind = f;
            if (behind == float.MinValue) continue;
            shift = Mathf.Max(shift, behind + GlassesClearance - p.z);
        }
        if (shift == float.MinValue) return;
        shift += GlassesOffset(CharacterIndex);
        glassesLocalPos = head.InverseTransformPoint(o + fwd * shift);
        g.position = o + fwd * shift;
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

    // Milieu du Mégaphone tenu : devant le bout du museau (mesuré), l'embout à 'MegaphoneGap' de lui.
    Vector3 MegaphoneCenter(Transform m, float size)
    {
        Vector3 snout = snoutMeasured ? head.TransformPoint(snoutLocal) : head.position + m.up * (size * 0.2f) + m.forward * (size * 0.6f);
        float reach = MegaphoneGap + MegaphoneSize * 0.5f;
        if (megaphoneFitted && CharacterIndex != JerkSkin) reach += megaphoneShift - MegaphoneExtraBack - MegaphoneSkinBack(CharacterIndex);
        return snout + FaceRotation(m) * Vector3.forward * reach;
    }

    // Orientation du visage : celle du personnage au repos, puis tournée avec l'os de la tête (gestes, animation, regard).
    Quaternion FaceRotation(Transform m) => glassesCalibrated && head != null ? head.rotation * glassesLocalRot : Quaternion.LookRotation(m.forward, m.up);

    const int JerkSkin = 3;

    // V0.7.14 : de combien reculer le Mégaphone (le long du regard) pour qu'il soit au plus près du visage sans le toucher (à
    // 'MegaphoneClearance' près) : chaque sommet du Mégaphone est comparé au relief du visage juste derrière lui. Sans maillage
    // lisible, sa boîte englobante sert de repli.
    void FitMegaphone(Transform m)
    {
        megaphoneFitted = true;
        megaphoneShift = 0f;
        if (!faceMeasured || megaphone == null) return;
        Vector3 center = megaphone.transform.position;
        var points = new List<Vector3>();
        foreach (var mf in megaphone.GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;
            if (mesh.isReadable)
                foreach (Vector3 v in mesh.vertices) points.Add(mf.transform.TransformPoint(v));
            else
            {
                Bounds b = mesh.bounds;
                for (int x = 0; x <= 6; x++)
                    for (int y = 0; y <= 6; y++)
                        for (int z = 0; z <= 6; z++)
                            points.Add(mf.transform.TransformPoint(new Vector3(Mathf.Lerp(b.min.x, b.max.x, x / 6f),
                                Mathf.Lerp(b.min.y, b.max.y, y / 6f), Mathf.Lerp(b.min.z, b.max.z, z / 6f))));
            }
        }
        float shift = float.MinValue;
        foreach (Vector3 p in points)
        {
            Vector3 d = p - center;
            float side = Vector3.Dot(d, m.right), up = Vector3.Dot(d, m.up), fwd = Vector3.Dot(d, m.forward);
            int cs = Mathf.FloorToInt(side / FaceCell), cu = Mathf.FloorToInt(up / FaceCell);
            float face = float.MinValue;
            for (int a = -FaceCellReach; a <= FaceCellReach; a++)
                for (int c = -FaceCellReach; c <= FaceCellReach; c++)
                    if (faceFront.TryGetValue(FaceKey(cs + a, cu + c), out float f) && f > face) face = f;
            if (face == float.MinValue) continue;
            // Ce point du Mégaphone doit rester devant le visage (le recul est négatif).
            shift = Mathf.Max(shift, face - fwd + MegaphoneClearance);
        }
        if (shift > float.MinValue) megaphoneShift = shift;
    }

    // Relief du visage autour de la place habituelle du Mégaphone (repère du personnage), bras baissé. Rien de mesuré (modèle
    // pas encore prêt) : nouvel essai à l'image suivante.
    void MeasureFace(Transform m, float size)
    {
        faceFront.Clear();
        megaphoneFitted = false;
        Vector3 center = MegaphoneCenter(m, size);
        float reachOut = MegaphoneSize;
        var baked = new Mesh();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // Sans l'échelle du renderer, puis replacé avec sa position et sa rotation seulement : juste aussi pour les modèles
            // dont l'échelle est portée par un parent (0,01 dans le FBX), contrairement à BakeMesh(…, true).
            smr.BakeMesh(baked);
            Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 local in baked.vertices)
            {
                Vector3 d = toWorld.MultiplyPoint3x4(local) - center;
                float side = Vector3.Dot(d, m.right), up = Vector3.Dot(d, m.up), fwd = Vector3.Dot(d, m.forward);
                if (Mathf.Abs(side) > reachOut || Mathf.Abs(up) > reachOut || fwd < -reachOut * 2f) continue;
                long key = FaceKey(Mathf.FloorToInt(side / FaceCell), Mathf.FloorToInt(up / FaceCell));
                if (!faceFront.TryGetValue(key, out float f) || fwd > f) faceFront[key] = fwd;
            }
        }
        Destroy(baked);
        faceMeasured = faceFront.Count > 0;
    }

    // 7.11 : le bras droit monte, la main sous le Mégaphone, devant la bouche.
    void MegaphonePose()
    {
        megaphoneWeight = Mathf.MoveTowards(megaphoneWeight, holdMegaphone ? 1f : 0f, Time.deltaTime * 5f);
        if (megaphoneWeight <= 0f || upperArm == null || lowerArm == null || hand == null) return;
        Transform m = model.transform;
        float k = head.lossyScale.y / Mathf.Max(0.0001f, m.lossyScale.y);
        Vector3 mouth = MegaphoneCenter(m, headSize * k) - m.up * (MegaphoneSize * 0.2f);
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
