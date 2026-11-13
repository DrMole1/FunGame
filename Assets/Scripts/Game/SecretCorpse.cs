using System.Collections.Generic;
using UnityEngine;

// Chapitre 15.3 (V0.7) : cadavre d'une Tête de Cloche, allongé sur le dos sur le plus haut terrain de la Zone 3 atteignable
// par un escalier, la tête légèrement disloquée, sans main ni avant-bras droits, sans pied ni bas de jambe gauches, des
// étincelles électriques jaillissant du cou et des moignons. La Carte Magnétique 1 (S1) est dans sa main gauche : un objet
// interactif qu'on ramasse. Une seconde après, la tête, les bras et les jambes s'agitent frénétiquement et les étincelles
// redoublent pendant une seconde (un joueur proche qui le regarde a des pulsations cardiaques et perd 20 de sérénité :
// GameSession.UpdateDread), puis tout revient à la normale.
// Construit à l'identique chez tous les clients (génération de la zone) ; seul l'état de la carte vient de l'hôte
// (GameSession.SecretCardTaken).
public class SecretCorpse : MonoBehaviour
{
    public static SecretCorpse Current { get; private set; }

    Transform head, model;
    Quaternion headRest;
    // Membres agités pendant le spasme (bras, jambes) et leur rotation de repos.
    Transform[] limbs = new Transform[0];
    Quaternion[] limbRest = new Quaternion[0];
    // Points d'où jaillissent les étincelles : le cou et les deux moignons.
    readonly List<ParticleSystem> sparks = new List<ParticleSystem>();
    Light sparkLight;
    SecretCard card;
    float twitchAt = -10f, nextSpark, nextJerk;
    bool scareUsed = true;

    public bool Twitching
    {
        get { float t = Time.time - twitchAt; return t >= 0f && t < GameRules.CorpseTwitchDuration; }
    }

    // Milieu du corps (pour savoir si le joueur le regarde).
    public Vector3 Center => transform.position + Vector3.up * 0.3f;

    // Le joueur local n'est effrayé qu'une fois par spasme.
    public bool ConsumeScare()
    {
        if (scareUsed) return false;
        scareUsed = true;
        return true;
    }

    // 'top' : dessus de la plateforme ; 'yaw' : orientation de la plateforme ; 'size' : son plus petit côté.
    public static SecretCorpse Build(Transform parent, Vector3 top, float yaw, float size)
    {
        var go = new GameObject("CadavreTeteDeCloche");
        go.transform.SetParent(parent, false);
        go.transform.position = top;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        var corpse = go.AddComponent<SecretCorpse>();
        corpse.BuildBody(size);
        Current = corpse;
        corpse.SetCardTaken(GameSession.SecretCardTaken, false);
        return corpse;
    }

    void BuildBody(float size)
    {
        Transform hand = null;
        var stumps = new List<Vector3>();
        var art = Resources.Load<CreatureArt>("TeteDeClocheArt");
        if (art != null && art.model != null)
        {
            var m = Instantiate(art.model, transform);
            model = m.transform;
            m.name = "Modele";
            foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
            // Assez petit pour tenir à peu près sur la plateforme (les jambes peuvent dépasser du bord).
            float scale = art.scale * Mathf.Clamp((size + 0.9f) / 2.97f, 0.6f, 0.85f);
            model.localScale = Vector3.one * scale;
            float length = 2.97f * scale;
            var animator = m.GetComponent<Animator>();
            if (animator == null) animator = m.AddComponent<Animator>();
            // Pose de repos (bras en croix) : étendu sur le dos, c'est un corps sans vie. Aucune animation.
            animator.runtimeAnimatorController = null;
            Transform rightForearm = null, leftShin = null;
            if (animator.isHuman)
            {
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                // V0.7 : la carte est dans la main gauche ; le bras gauche est écarté du corps.
                hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform arm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                if (arm != null) arm.rotation = Quaternion.AngleAxis(35f, model.forward) * arm.rotation;
                rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                leftShin = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                var list = new List<Transform>();
                foreach (var b in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm,
                    HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg })
                {
                    Transform t = animator.GetBoneTransform(b);
                    if (t != null) list.Add(t);
                }
                limbs = list.ToArray();
            }
            animator.enabled = false;
            // Couché sur le dos, la tête vers -Z, le milieu du corps au centre de la plateforme.
            model.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            model.localPosition = new Vector3(0f, 0f, length * 0.5f);
            // V0.7 : la main et l'avant-bras droits sont absents, comme le pied gauche et la jambe gauche sous le genou.
            // L'os réduit à rien replie ces parties du maillage sur le coude et le genou, fermés par un moignon.
            if (rightForearm != null) rightForearm.localScale = Vector3.one * 0.001f;
            if (leftShin != null) leftShin.localScale = Vector3.one * 0.001f;
            // Posé sur la plateforme : son point le plus bas (sur le maillage réel, pas sur des bornes approximatives).
            float lowest = LowestPoint(m);
            if (lowest < float.MaxValue) model.position += Vector3.up * (transform.position.y - lowest + 0.01f);
            foreach (var r in m.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            Material flesh = GameArt.Lit("Moignon", new Color(0.22f, 0.03f, 0.03f), null, 0.5f);
            Material wire = GameArt.Lit("MoignonFil", new Color(0.55f, 0.35f, 0.15f), null, 0.8f);
            if (rightForearm != null) stumps.Add(Stump(rightForearm, length * 0.02f, flesh, wire));
            if (leftShin != null) stumps.Add(Stump(leftShin, length * 0.026f, flesh, wire));
            // Tête disloquée : tordue sur le côté et rejetée en arrière.
            if (head != null) head.localRotation *= Quaternion.Euler(25f, 40f, 35f);
        }
        else
        {
            // Sans le modèle : une silhouette en primitives couchée, la cloche de bronze renversée.
            Material rags = GameArt.Lit("TeteHaillons", new Color(0.4f, 0.43f, 0.38f), null, 0.05f);
            var body = GameArt.Prim(PrimitiveType.Capsule, transform, new Vector3(0f, 0.25f, 0.2f), new Vector3(0.5f, 0.9f, 0.35f), rags);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreach (var c in body.GetComponentsInChildren<Collider>()) Destroy(c);
            var bell = GameArt.Prim(PrimitiveType.Sphere, transform, new Vector3(0.15f, 0.3f, -0.85f), Vector3.one * 0.55f,
                GameArt.Lit("CloRouille", new Color(0.45f, 0.32f, 0.16f), null, 0.4f));
            foreach (var c in bell.GetComponentsInChildren<Collider>()) Destroy(c);
            head = bell.transform;
        }
        if (head != null) headRest = head.localRotation;
        limbRest = new Quaternion[limbs.Length];
        for (int i = 0; i < limbs.Length; i++) limbRest[i] = limbs[i].localRotation;

        // Étincelles électriques : au cou et aux deux moignons (fils arrachés).
        Vector3 neck = head != null ? head.position : transform.position + Vector3.up * 0.4f;
        sparks.Add(BuildSparks(transform, neck));
        foreach (Vector3 p in stumps) sparks.Add(BuildSparks(transform, p));
        var lightGo = new GameObject("Etincelle");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.position = neck + Vector3.up * 0.25f;
        sparkLight = lightGo.AddComponent<Light>();
        sparkLight.type = LightType.Point;
        sparkLight.color = new Color(0.75f, 0.85f, 1f);
        sparkLight.range = 5f;
        sparkLight.intensity = 0f;
        sparkLight.shadows = LightShadows.None;

        // La carte, dans la main gauche (à défaut, posée contre le flanc).
        Vector3 cardPos = hand != null ? hand.position + Vector3.up * 0.06f : transform.position - transform.right * 0.45f + Vector3.up * 0.15f;
        card = SecretCard.Create(transform, cardPos);
        nextSpark = Time.time + Random.Range(0.3f, 1.5f);
    }

    // Moignon sanglant à la racine de l'os amputé 'bone' (rattaché à l'os parent pour suivre les spasmes), d'où dépassent
    // deux fils de cuivre arrachés. Rend sa position.
    Vector3 Stump(Transform bone, float radius, Material flesh, Material wire)
    {
        Vector3 at = bone.position;
        Transform parent = bone.parent != null ? bone.parent : transform;
        Vector3 dir = at - parent.position;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
        // Moignon aplati, à moitié rentré dans le membre.
        var cap = GameArt.Prim(PrimitiveType.Sphere, transform, Vector3.zero, new Vector3(radius * 2f, radius * 2f, radius * 1.1f), flesh);
        cap.name = "Moignon";
        cap.transform.position = at;
        cap.transform.rotation = Quaternion.LookRotation(dir);
        cap.transform.SetParent(parent, true);
        Clean(cap);
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.forward);
        side.Normalize();
        for (int i = 0; i < 2; i++)
        {
            Vector3 d = Quaternion.AngleAxis(i == 0 ? 25f : -30f, side) * dir;
            var w = GameArt.Prim(PrimitiveType.Cylinder, transform, Vector3.zero, new Vector3(radius * 0.2f, radius * 1.2f, radius * 0.2f), wire);
            w.name = "Fil";
            w.transform.position = at + d * radius * 1.2f;
            w.transform.rotation = Quaternion.FromToRotation(Vector3.up, d);
            w.transform.SetParent(parent, true);
            Clean(w);
        }
        return at + dir * radius;
    }

    static void Clean(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static float LowestPoint(GameObject model)
    {
        float lowest = float.MaxValue;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (Vector3 v in baked.vertices) lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v).y);
            Destroy(baked);
        }
        foreach (var mr in model.GetComponentsInChildren<MeshRenderer>()) lowest = Mathf.Min(lowest, mr.bounds.min.y);
        return lowest;
    }

    // Étincelles réalistes : traits blanc bleuté très brefs qui virent au jaune puis à l'orange en refroidissant, retombent,
    // freinent dans l'air et rebondissent sur le sol.
    static ParticleSystem BuildSparks(Transform parent, Vector3 at)
    {
        var go = new GameObject("Etincelles");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        go.transform.rotation = Quaternion.identity;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
        main.startColor = Color.white;
        main.gravityModifier = 1.6f;
        main.maxParticles = 250;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.03f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        // Refroidissement : blanc bleuté -> jaune -> orange -> éteint.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.85f, 0.92f, 1f), 0f), new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0.25f),
                    new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.7f), new GradientColorKey(new Color(0.6f, 0.15f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 8f;
        limit.dampen = 0.08f;
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Low;
        collision.bounce = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        collision.dampen = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        collision.lifetimeLoss = 0.25f;
        collision.collidesWith = Physics.DefaultRaycastLayers;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 1.5f;
        renderer.sharedMaterial = GameArt.Spark;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    // État de la carte (hôte). 'animate' : elle vient d'être prise, le cadavre s'agite une seconde plus tard.
    public void SetCardTaken(bool taken, bool animate)
    {
        if (card != null) card.gameObject.SetActive(!taken);
        if (taken && animate)
        {
            twitchAt = Time.time + GameRules.CorpseTwitchDelay;
            scareUsed = false;
        }
    }

    void Update()
    {
        bool twitching = Twitching;
        float settle = 1f - Mathf.Exp(-12f * Time.deltaTime);
        if (twitching)
        {
            // Secousses saccadées : une nouvelle pose toutes les 35 à 70 ms.
            if (Time.time >= nextJerk)
            {
                nextJerk = Time.time + Random.Range(0.035f, 0.07f);
                if (head != null) head.localRotation = headRest * Quaternion.Euler(Random.Range(-35f, 35f), Random.Range(-45f, 45f), Random.Range(-30f, 30f));
                for (int i = 0; i < limbs.Length; i++)
                    limbs[i].localRotation = limbRest[i] * Quaternion.Euler(Random.Range(-30f, 30f), Random.Range(-25f, 25f), Random.Range(-30f, 30f));
            }
        }
        else
        {
            if (head != null) head.localRotation = Quaternion.Slerp(head.localRotation, headRest, settle);
            for (int i = 0; i < limbs.Length; i++) limbs[i].localRotation = Quaternion.Slerp(limbs[i].localRotation, limbRest[i], settle);
        }
        if (sparks.Count == 0) return;
        // Crépitements espacés, d'un point au hasard ; pendant le spasme, des gerbes continues de partout.
        if (twitching)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(60f * Time.deltaTime));
            foreach (var ps in sparks) ps.Emit(n);
            if (Time.time >= nextSpark)
            {
                nextSpark = Time.time + 0.15f;
                GameAudio.Play3D("spark", sparks[Random.Range(0, sparks.Count)].transform.position, 0.75f, 15f, 0.2f);
            }
            sparkLight.intensity = Random.value < 0.3f ? 0.3f : Random.Range(2f, 4f);
            return;
        }
        if (Time.time >= nextSpark)
        {
            var ps = sparks[Random.Range(0, sparks.Count)];
            // Parfois une double décharge rapprochée, comme un court-circuit.
            nextSpark = Time.time + (Random.value < 0.25f ? Random.Range(0.08f, 0.15f) : Random.Range(0.6f, 2.2f));
            ps.Emit(Random.Range(8, 20));
            sparkLight.transform.position = ps.transform.position + Vector3.up * 0.25f;
            sparkLight.intensity = Random.Range(2f, 3.2f);
            GameAudio.Play3D("spark", ps.transform.position, 0.45f, 12f, 0.2f);
        }
        sparkLight.intensity = Mathf.MoveTowards(sparkLight.intensity, 0f, Time.deltaTime * 20f);
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    // Carte magnétique d'accès (badge de laboratoire), 'width' de large : plastique blanc cassé, bande magnétique noire,
    // liseré rouge WOOF et puce dorée.
    public static void BuildCard(Transform parent, float width)
    {
        float h = width * 0.63f, d = width * 0.03f;
        Material plastic = GameArt.Lit("CarteBlanc", new Color(0.88f, 0.9f, 0.92f), null, 0.6f);
        Material black = GameArt.Lit("CarteBande", new Color(0.03f, 0.03f, 0.03f), null, 0.4f);
        Material red = GameArt.Lit("CarteRouge", new Color(0.7f, 0.06f, 0.06f), null, 0.5f);
        Material gold = GameArt.Lit("CartePuce", new Color(0.9f, 0.72f, 0.3f), null, 0.85f);
        Prim(parent, Vector3.zero, new Vector3(width, h, d), plastic);
        Prim(parent, new Vector3(0f, h * 0.28f, -d * 0.55f), new Vector3(width * 0.98f, h * 0.18f, d * 0.2f), black);
        Prim(parent, new Vector3(0f, -h * 0.36f, d * 0.55f), new Vector3(width * 0.98f, h * 0.12f, d * 0.2f), red);
        Prim(parent, new Vector3(-width * 0.28f, h * 0.08f, d * 0.55f), new Vector3(width * 0.16f, h * 0.2f, d * 0.2f), gold);
    }

    static void Prim(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameArt.Prim(PrimitiveType.Cube, parent, pos, size, mat);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}

// La Carte Magnétique 1 dans la main du cadavre : objet interactif (contour), ramassé une seule fois par partie.
public class SecretCard : Interactable
{
    public static SecretCard Create(Transform parent, Vector3 pos)
    {
        var go = new GameObject("CarteMagnetique1");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        // À plat dans la paume, un peu de travers.
        go.transform.rotation = parent.rotation * Quaternion.Euler(80f, -25f, 0f);
        SecretCorpse.BuildCard(go.transform, 0.16f);
        var card = go.AddComponent<SecretCard>();
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(0.5f, 0.5f, 0.35f);
        return card;
    }

    public override bool CanInteract => !GameSession.SecretCardTaken;
    public override string Prompt => Loc.F("Ramasser {0}", Items.Get(ItemType.CarteMagnetique1).name);
    public override void Interact() => GameSession.Instance.TakeSecretCard();
}
