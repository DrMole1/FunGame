using UnityEngine;

// Chapitre 15.3 (V0.6.14) : cadavre d'une Tête de Cloche, allongé sur le dos au sommet de la plus haute plateforme atteignable
// de la Zone 3, la tête légèrement disloquée, des étincelles électriques jaillissant du cou. La Carte Magnétique 1 (S1) est
// dans sa main : un objet interactif qu'on ramasse. Une seconde après, la tête s'agite frénétiquement et les étincelles
// redoublent pendant une seconde, puis tout revient à la normale.
// Construit à l'identique chez tous les clients (génération de la zone) ; seul l'état de la carte vient de l'hôte
// (GameSession.SecretCardTaken).
public class SecretCorpse : MonoBehaviour
{
    public static SecretCorpse Current { get; private set; }

    Transform head;
    Quaternion headRest;
    ParticleSystem sparks;
    Light sparkLight;
    SecretCard card;
    float twitchAt = -10f, nextSpark;

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
        var art = Resources.Load<CreatureArt>("TeteDeClocheArt");
        if (art != null && art.model != null)
        {
            var model = Instantiate(art.model, transform);
            model.name = "Modele";
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            // Assez petit pour tenir à peu près sur la plateforme (les jambes peuvent dépasser du bord).
            float scale = art.scale * Mathf.Clamp((size + 0.9f) / 2.97f, 0.6f, 0.85f);
            model.transform.localScale = Vector3.one * scale;
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            // Pose de repos (bras en croix) : étendu sur le dos, c'est un corps sans vie. Aucune animation.
            animator.runtimeAnimatorController = null;
            if (animator.isHuman)
            {
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                // Bras droit écarté du corps, la carte au creux de la main.
                Transform arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (arm != null) arm.rotation = Quaternion.AngleAxis(-35f, model.transform.forward) * arm.rotation;
            }
            animator.enabled = false;
            // Couché sur le dos, la tête vers -Z, le milieu du corps au centre de la plateforme.
            float length = 2.97f * scale;
            model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            model.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
            // Posé sur la plateforme : son point le plus bas (sur le maillage réel, pas sur des bornes approximatives).
            float lowest = LowestPoint(model);
            if (lowest < float.MaxValue) model.transform.position += Vector3.up * (transform.position.y - lowest + 0.01f);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
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

        // Étincelles électriques au niveau du cou.
        Vector3 neck = head != null ? head.position : transform.position + Vector3.up * 0.4f;
        sparks = BuildSparks(transform, neck);
        var lightGo = new GameObject("Etincelle");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.position = neck + Vector3.up * 0.2f;
        sparkLight = lightGo.AddComponent<Light>();
        sparkLight.type = LightType.Point;
        sparkLight.color = new Color(0.75f, 0.85f, 1f);
        sparkLight.range = 4f;
        sparkLight.intensity = 0f;
        sparkLight.shadows = LightShadows.None;

        // La carte, dans la main droite (à défaut, posée contre le flanc).
        Vector3 cardPos = hand != null ? hand.position + Vector3.up * 0.06f : transform.position + transform.right * 0.45f + Vector3.up * 0.15f;
        card = SecretCard.Create(transform, cardPos);
        nextSpark = Time.time + Random.Range(0.3f, 1.5f);
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

    static ParticleSystem BuildSparks(Transform parent, Vector3 at)
    {
        var go = new GameObject("Etincelles");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.7f), new Color(0.7f, 0.85f, 1f));
        main.gravityModifier = 1.2f;
        main.maxParticles = 200;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.08f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.05f;
        renderer.lengthScale = 2f;
        renderer.sharedMaterial = GameArt.Spark;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    // État de la carte (hôte). 'animate' : elle vient d'être prise, le cadavre s'agite une seconde plus tard.
    public void SetCardTaken(bool taken, bool animate)
    {
        if (card != null) card.gameObject.SetActive(!taken);
        if (taken && animate) twitchAt = Time.time + GameRules.CorpseTwitchDelay;
    }

    void Update()
    {
        float t = Time.time - twitchAt;
        bool twitching = t >= 0f && t < GameRules.CorpseTwitchDuration;
        if (head != null)
            head.localRotation = twitching
                ? headRest * Quaternion.Euler(Random.Range(-35f, 35f), Random.Range(-45f, 45f), Random.Range(-30f, 30f))
                : Quaternion.Slerp(head.localRotation, headRest, 1f - Mathf.Exp(-12f * Time.deltaTime));
        if (sparks == null) return;
        // Crépitements espacés ; pendant le spasme, une gerbe continue.
        if (twitching)
        {
            sparks.Emit(Mathf.Max(1, Mathf.RoundToInt(70f * Time.deltaTime)));
            if (Time.time >= nextSpark)
            {
                nextSpark = Time.time + 0.18f;
                GameAudio.Play3D("spark", sparks.transform.position, 0.7f, 15f, 0.2f);
            }
            sparkLight.intensity = Random.Range(1.5f, 3.5f);
            return;
        }
        if (Time.time >= nextSpark)
        {
            nextSpark = Time.time + Random.Range(0.6f, 2.2f);
            sparks.Emit(Random.Range(6, 14));
            sparkLight.intensity = 2.5f;
            GameAudio.Play3D("spark", sparks.transform.position, 0.45f, 12f, 0.2f);
        }
        sparkLight.intensity = Mathf.MoveTowards(sparkLight.intensity, 0f, Time.deltaTime * 18f);
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
        go.transform.rotation = parent.rotation * Quaternion.Euler(80f, 25f, 0f);
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
