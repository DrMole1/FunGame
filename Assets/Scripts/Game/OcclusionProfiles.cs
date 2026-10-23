using System.Collections.Generic;
using UnityEngine;

// Silhouettes des humanoïdes découpées dans le lecteur YouTube de la Télévision (voir TvScreen) : une capsule par
// segment osseux. Les rayons sont mesurés une fois par modèle dans l'éditeur (les maillages ne sont pas lisibles en
// jeu), rapportés à la distance bassin-tête, et rangés ici par nom d'avatar ("FunGame/Configurer le jeu").
public class OcclusionProfiles : ScriptableObject
{
    [System.Serializable]
    public class Profile
    {
        public string avatar;
        public float[] radii;   // par segment, divisés par la distance bassin-tête
    }

    public Profile[] profiles = new Profile[0];

    // ---------- Segments ----------

    public const int SegmentCount = 17;
    // Rayons par défaut (mètres, pour un humanoïde de 1,7 m : 0,55 m du bassin à la tête).
    public const float ReferenceSize = 0.55f;

    static readonly (HumanBodyBones a, HumanBodyBones b, float r)[] Limbs =
    {
        (HumanBodyBones.Hips, HumanBodyBones.Neck, 0.17f),
        (HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, 0.11f),
        (HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, 0.09f),
        (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.065f),
        (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.055f),
        (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.065f),
        (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.055f),
        (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.085f),
        (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.07f),
        (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.085f),
        (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.07f),
        (HumanBodyBones.Neck, HumanBodyBones.Head, 0.07f),
    };

    public struct Segment
    {
        public bool valid;
        public Vector3 a, b;
        public float defaultRadius;   // mètres, à l'échelle du modèle
    }

    // Segments de l'humanoïde dans sa pose actuelle ; 'size' : distance bassin-tête. Faux si le squelette est incomplet.
    public static bool Build(Animator animator, Segment[] segments, out float size)
    {
        size = 0f;
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips), head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (hips == null || head == null) return false;
        size = Mathf.Max(0.05f, Vector3.Distance(hips.position, head.position));
        float s = size / ReferenceSize;
        int i = 0;
        foreach (var (a, b, r) in Limbs)
        {
            Transform ta = animator.GetBoneTransform(a), tb = animator.GetBoneTransform(b);
            segments[i++] = ta != null && tb != null
                ? new Segment { valid = true, a = ta.position, b = tb.position, defaultRadius = r * s }
                : default;
        }
        // Crâne, au-dessus de l'os de la tête, dans le prolongement du cou.
        Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        Vector3 up = neck != null ? (head.position - neck.position).normalized : animator.transform.up;
        segments[i++] = new Segment { valid = true, a = head.position + up * 0.06f * s, b = head.position + up * 0.16f * s, defaultRadius = 0.13f * s };
        // Mains et pieds : un peu au-delà de l'os.
        segments[i++] = Extend(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.12f * s, 0.05f * s);
        segments[i++] = Extend(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.12f * s, 0.05f * s);
        segments[i++] = Foot(animator, HumanBodyBones.LeftFoot, s);
        segments[i++] = Foot(animator, HumanBodyBones.RightFoot, s);
        return true;
    }

    static Segment Extend(Animator animator, HumanBodyBones from, HumanBodyBones to, float length, float r)
    {
        Transform a = animator.GetBoneTransform(from), b = animator.GetBoneTransform(to);
        if (a == null || b == null) return default;
        return new Segment { valid = true, a = b.position, b = b.position + (b.position - a.position).normalized * length, defaultRadius = r };
    }

    static Segment Foot(Animator animator, HumanBodyBones bone, float s)
    {
        Transform foot = animator.GetBoneTransform(bone);
        if (foot == null) return default;
        return new Segment { valid = true, a = foot.position, b = foot.position + animator.transform.forward * 0.16f * s, defaultRadius = 0.06f * s };
    }

    // ---------- Rayons ----------

    static OcclusionProfiles instance;
    static bool loaded;
    readonly Dictionary<string, float[]> byAvatar = new Dictionary<string, float[]>();

    // Rayons mesurés (divisés par la distance bassin-tête) pour cet avatar, ou null.
    public static float[] RadiiFor(Avatar avatar)
    {
        if (!loaded)
        {
            loaded = true;
            instance = Resources.Load<OcclusionProfiles>("OcclusionProfiles");
            if (instance != null)
                foreach (var p in instance.profiles)
                    if (p != null && !string.IsNullOrEmpty(p.avatar) && p.radii != null && p.radii.Length == SegmentCount)
                        instance.byAvatar[p.avatar] = p.radii;
        }
        if (instance == null || avatar == null) return null;
        return instance.byAvatar.TryGetValue(avatar.name, out var radii) ? radii : null;
    }

    // Distance d'un point au segment [a, b].
    public static float Distance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }
}
