using System.Collections.Generic;
using UnityEngine;

// Fermeture éclair de Mirkov (GDD 3.1, V0.6.13) : fermée, incrustée dans son dos, du bas du dos jusqu'aux omoplates. Elle est
// collée à la peau sur toute sa longueur, extrémités comprises : elle passe par des sommets du maillage pris sur la ligne
// médiane du dos, dont la position est recalculée à chaque image comme le fait le skinning (mêmes os, mêmes poids). Elle se
// plie donc exactement comme le dos. (À défaut de maillage lisible : à distance fixe derrière les os du dos.)
// Objet interactif pour les autres joueurs seulement : un clic joue le son Zip, puis une explosion tue tous les joueurs à
// 5 mètres ou moins (membres qui explosent et mare de sang, comme au quota non atteint) — voir GameSession.PullZipper.
// (Construite en primitives : une bande de dents métalliques et un curseur avec sa tirette.)
public class MirkovZipper : Interactable
{
    public const int Skin = 7;   // Perso 8 : Mirkov
    const float ToothSize = 0.018f, Width = 0.035f;

    Transform spine, chest, upperChest, model;
    Transform lower, upper, slider;
    float back = 0.14f;
    BoxCollider trigger;

    // Points de la fermeture collés à la peau (du bas vers le haut) : sommet du maillage, position et normale au repos.
    const int Points = 10;
    const float SkinLift = 0.004f;
    SkinnedMeshRenderer skin;
    Transform[] skinBones;
    Matrix4x4[] bindposes;
    BoneWeight[] pointWeights;
    Vector3[] restPos, restNormal;
    Transform[] chain;
    readonly Vector3[] pos = new Vector3[Points], normal = new Vector3[Points];
    readonly Vector3[] smoothPos = new Vector3[Points], smoothNormal = new Vector3[Points];
    Renderer[] parts;
    RemotePlayer owner;

    public static MirkovZipper Attach(Transform avatarRoot, GameObject model, Animator animator)
    {
        if (animator == null || !animator.isHuman) return null;
        var go = new GameObject("FermetureEclair");
        go.transform.SetParent(avatarRoot, false);
        var zip = go.AddComponent<MirkovZipper>();
        zip.model = model.transform;
        zip.spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        zip.chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? zip.spine;
        zip.upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Neck);
        if (zip.spine == null || zip.chest == null || zip.upperChest == null)
        {
            Destroy(go);
            return null;
        }
        zip.back = BackDepth(model, zip.spine, zip.upperChest);
        zip.BindToSkin();
        zip.Build();
        zip.owner = avatarRoot.GetComponentInParent<RemotePlayer>();
        return zip;
    }

    // Profondeur du dos derrière la colonne : les sommets du maillage les plus en arrière, au milieu du dos.
    static float BackDepth(GameObject model, Transform low, Transform high)
    {
        float best = 0f;
        Vector3 fwd = model.transform.forward, right = model.transform.right;
        float yMin = low.position.y, yMax = high.position.y;
        Vector3 axis = (low.position + high.position) * 0.5f;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 v in baked.vertices)
            {
                Vector3 p = m.MultiplyPoint3x4(v);
                if (p.y < yMin || p.y > yMax || Mathf.Abs(Vector3.Dot(p - axis, right)) > 0.05f) continue;
                best = Mathf.Max(best, Vector3.Dot(axis - p, fwd));
            }
            Destroy(baked);
        }
        return best > 0.03f ? Mathf.Min(best, 0.4f) : 0.14f;
    }

    // Choisit, sur la pose de repos (le personnage vient d'être créé), un sommet du milieu du dos à chaque hauteur de la
    // fermeture : le plus en arrière parmi ceux qui sont sur la ligne médiane.
    void BindToSkin()
    {
        Vector3 fwd = model.forward, right = model.right;
        Vector3 a = OnBack(spine, fwd), b = OnBack(chest, fwd), c = OnBack(upperChest, fwd);
        Vector3 start = a + (a - b).normalized * 0.06f, end = Vector3.Lerp(b, c, 0.8f);
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Mesh mesh = smr.sharedMesh;
            if (!smr.enabled || mesh == null || !mesh.isReadable || smr.bones.Length == 0) continue;
            BoneWeight[] weights = mesh.boneWeights;
            Vector3[] rest = mesh.vertices, restN = mesh.normals;
            if (weights.Length != rest.Length || restN.Length != rest.Length) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            Vector3[] verts = baked.vertices;
            Destroy(baked);
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            var world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) world[i] = m.MultiplyPoint3x4(verts[i]);
            var chosen = new int[Points];
            bool ok = true;
            for (int k = 0; k < Points && ok; k++)
            {
                Vector3 target = Vector3.Lerp(start, end, k / (Points - 1f));
                Vector3 axis = target + fwd * back;   // la colonne, à cette hauteur
                // La surface du dos à cette hauteur (le plus en arrière), puis, parmi les sommets de cette surface, le plus
                // proche de la ligne médiane : sinon la fermeture zigzague d'un côté à l'autre de la colonne.
                int best = -1;
                for (float tol = 0.02f; best < 0 && tol <= 0.08f; tol *= 2f)
                {
                    float maxDepth = 0f;
                    for (int i = 0; i < world.Length; i++)
                    {
                        Vector3 d = world[i] - axis;
                        if (Mathf.Abs(Vector3.Dot(d, right)) > 0.04f || Mathf.Abs(d.y) > tol) continue;
                        maxDepth = Mathf.Max(maxDepth, -Vector3.Dot(d, fwd));
                    }
                    float bestSide = float.MaxValue;
                    for (int i = 0; i < world.Length && maxDepth > 0f; i++)
                    {
                        Vector3 d = world[i] - axis;
                        float side = Mathf.Abs(Vector3.Dot(d, right));
                        if (side > 0.04f || Mathf.Abs(d.y) > tol || -Vector3.Dot(d, fwd) < maxDepth - 0.015f) continue;
                        if (side < bestSide) { bestSide = side; best = i; }
                    }
                }
                ok = best >= 0;
                chosen[k] = best;
            }
            if (!ok) continue;
            skin = smr;
            skinBones = smr.bones;
            bindposes = mesh.bindposes;
            pointWeights = new BoneWeight[Points];
            restPos = new Vector3[Points];
            restNormal = new Vector3[Points];
            for (int k = 0; k < Points; k++)
            {
                pointWeights[k] = weights[chosen[k]];
                restPos[k] = rest[chosen[k]];
                restNormal[k] = restN[chosen[k]];
            }
            return;
        }
    }

    // Position et normale d'un point de la fermeture, comme le skinning les calcule (jusqu'à quatre os pondérés).
    bool SkinPoint(int k, out Vector3 p, out Vector3 n)
    {
        BoneWeight w = pointWeights[k];
        p = n = Vector3.zero;
        float total = 0f;
        Accumulate(w.boneIndex0, w.weight0, k, ref p, ref n, ref total);
        Accumulate(w.boneIndex1, w.weight1, k, ref p, ref n, ref total);
        Accumulate(w.boneIndex2, w.weight2, k, ref p, ref n, ref total);
        Accumulate(w.boneIndex3, w.weight3, k, ref p, ref n, ref total);
        if (total < 1e-4f) return false;
        p /= total;
        n = n.normalized;
        return true;
    }

    void Accumulate(int bone, float weight, int k, ref Vector3 p, ref Vector3 n, ref float total)
    {
        if (weight <= 0f || bone < 0 || bone >= skinBones.Length || bone >= bindposes.Length || skinBones[bone] == null) return;
        Matrix4x4 m = skinBones[bone].localToWorldMatrix * bindposes[bone];
        p += m.MultiplyPoint3x4(restPos[k]) * weight;
        n += m.MultiplyVector(restNormal[k]) * weight;
        total += weight;
    }

    void Build()
    {
        Material teeth = GameArt.Lit("ZipDents", new Color(0.62f, 0.6f, 0.55f), null, 0.85f);
        if (teeth.HasProperty("_Metallic")) teeth.SetFloat("_Metallic", 0.8f);
        Material tape = GameArt.Lit("ZipRuban", new Color(0.08f, 0.08f, 0.09f), null, 0.2f);
        if (skin != null)
        {
            chain = new Transform[Points - 1];
            for (int i = 0; i < chain.Length; i++) chain[i] = Segment("Maillon" + i, tape, teeth);
        }
        else
        {
            lower = Segment("Bas", tape, teeth);
            upper = Segment("Haut", tape, teeth);
        }
        slider = new GameObject("Curseur").transform;
        slider.SetParent(transform, false);
        Prim(slider, Vector3.zero, new Vector3(Width * 1.3f, 0.03f, 0.012f), teeth);
        var tab = Prim(slider, new Vector3(0f, -0.03f, 0.006f), new Vector3(0.018f, 0.04f, 0.004f), teeth);
        tab.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
        parts = GetComponentsInChildren<Renderer>();
        foreach (var r in parts) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Zone de sélection derrière le dos, au-delà de la capsule du personnage.
        trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
    }

    // Segment de longueur 1 (mis à l'échelle en hauteur) : ruban sombre, dents de part et d'autre.
    Transform Segment(string name, Material tape, Material teeth)
    {
        var seg = new GameObject(name).transform;
        seg.SetParent(transform, false);
        var inner = new GameObject("Bande").transform;
        inner.SetParent(seg, false);
        Prim(inner, new Vector3(0f, 0.5f, 0f), new Vector3(Width, 1f, 0.004f), tape);
        // Dents en quinconce, en une seule bande teintée métal (le détail fin se devine).
        Prim(inner, new Vector3(-Width * 0.18f, 0.5f, 0.003f), new Vector3(ToothSize * 0.9f, 1f, 0.004f), teeth);
        Prim(inner, new Vector3(Width * 0.18f, 0.5f, 0.0035f), new Vector3(ToothSize * 0.9f, 1f, 0.004f), teeth);
        return seg;
    }

    static GameObject Prim(Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameArt.Prim(PrimitiveType.Cube, parent, pos, size, m);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        return go;
    }

    void LateUpdate()
    {
        bool visible = model != null && model.gameObject.activeInHierarchy;
        foreach (var r in parts) if (r != null) r.enabled = visible;
        trigger.enabled = visible && owner != null && !owner.Dead;
        if (!visible) return;
        Vector3 fwd = model.forward;
        if (chain != null)
        {
            FollowSkin();
            return;
        }
        Vector3 a = OnBack(spine, fwd), b = OnBack(chest, fwd), c = OnBack(upperChest, fwd);
        // Commence un peu sous la première vertèbre, s'arrête sous la nuque.
        Vector3 start = a + (a - b).normalized * 0.06f, end = Vector3.Lerp(b, c, 0.8f);
        Place(lower, start, b, fwd);
        Place(upper, b, end, fwd);
        slider.position = end - (end - b).normalized * 0.02f;
        slider.rotation = Quaternion.LookRotation(-fwd, (end - b).normalized);
        // Sélection : une boîte derrière le dos (repère du personnage, sans tenir compte de l'inclinaison).
        trigger.center = transform.InverseTransformPoint((start + end) * 0.5f - fwd * 0.12f);
        trigger.size = new Vector3(0.32f, Mathf.Max(0.3f, Vector3.Distance(start, end) + 0.1f), 0.3f);
    }

    // La fermeture passe par les points collés à la peau, à peine soulevée au-dessus (pas de clignotement avec le dos).
    void FollowSkin()
    {
        for (int k = 0; k < Points; k++)
            if (!SkinPoint(k, out pos[k], out normal[k])) return;
        // Léger lissage des points intérieurs et des normales : une ligne régulière, sans maillon de travers.
        for (int k = 1; k < Points - 1; k++)
        {
            smoothPos[k] = pos[k] * 0.5f + (pos[k - 1] + pos[k + 1]) * 0.25f;
            smoothNormal[k] = (normal[k] * 2f + normal[k - 1] + normal[k + 1]).normalized;
        }
        for (int k = 1; k < Points - 1; k++)
        {
            pos[k] = smoothPos[k];
            normal[k] = smoothNormal[k];
        }
        for (int k = 0; k < Points; k++) pos[k] += normal[k] * SkinLift;
        for (int i = 0; i < chain.Length; i++)
        {
            // Chaque maillon dépasse un peu sur le suivant : pas de jour entre eux quand le dos se courbe.
            Vector3 dir = (pos[i + 1] - pos[i]).normalized;
            PlaceOnSkin(chain[i], pos[i] - dir * 0.004f, pos[i + 1] + dir * 0.004f, (normal[i] + normal[i + 1]).normalized);
        }
        Vector3 top = pos[Points - 1], below = pos[Points - 2];
        Vector3 up = (top - below).normalized, outward = normal[Points - 1];
        slider.position = top - up * 0.02f + outward * 0.004f;
        slider.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(outward, up).normalized, up);
        Vector3 mid = (pos[0] + pos[Points - 1]) * 0.5f, away = (normal[0] + normal[Points / 2] + normal[Points - 1]).normalized;
        trigger.center = transform.InverseTransformPoint(mid + away * 0.12f);
        trigger.size = new Vector3(0.32f, Mathf.Max(0.3f, Vector3.Distance(pos[0], pos[Points - 1]) + 0.1f), 0.3f);
    }

    // Segment de 'from' à 'to', face extérieure (dents) tournée selon 'outward'.
    static void PlaceOnSkin(Transform seg, Vector3 from, Vector3 to, Vector3 outward)
    {
        Vector3 up = to - from;
        float length = Mathf.Max(0.005f, up.magnitude);
        up /= length;
        Vector3 face = Vector3.ProjectOnPlane(outward, up);
        if (face.sqrMagnitude < 1e-6f) face = Vector3.ProjectOnPlane(Vector3.back, up);
        seg.position = from;
        seg.rotation = Quaternion.LookRotation(face.normalized, up);
        seg.localScale = new Vector3(1f, length, 1f);
    }

    Vector3 OnBack(Transform bone, Vector3 fwd) => bone.position - fwd * back;

    static void Place(Transform seg, Vector3 from, Vector3 to, Vector3 fwd)
    {
        Vector3 up = to - from;
        float length = Mathf.Max(0.01f, up.magnitude);
        seg.position = from;
        seg.rotation = Quaternion.LookRotation(-fwd, up / length);
        seg.localScale = new Vector3(1f, length, 1f);
    }

    public override bool CanInteract => owner != null && !owner.Dead;
    // Aucun texte quand on la vise (seul le contour apparaît).
    public override string Prompt => "";
    public override void Interact()
    {
        if (owner != null) GameSession.Instance.PullZipper(owner.PlayerId);
    }
}
