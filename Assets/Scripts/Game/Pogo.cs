using UnityEngine;

// Pogo (GDD 4, V0.3.3) : un clown à ressort dans une grande boîte, dans le Hub. Cliquer sur la boîte l'ouvre (Bouton Rouge
// enclenché) ; Pogo fixe alors le joueur le plus proche. Il achète l'objet sélectionné (comme /sell) et sourit 3 s ;
// cliqué les mains vides, il se met en colère, et recommencer pendant sa colère lui vaut un coup de tête mortel.
// L'hôte décide de son état (GameSession) ; chaque client l'anime ici.
public class Pogo : Interactable
{
    public enum Mood { Closed = 0, Open = 1, Angry = 2 }

    const float BoxHeight = 1.95f, BustHeight = 2.0f;
    // Pivot de la tête (base du cou) au ras de la boîte ouverte ; le bas du modèle (ressort) reste dans la boîte.
    const float NeckY = BoxHeight + 0.2f, NeckFraction = 0.42f;
    // Le buste remonte de 'Rise' quand la boîte s'ouvre ; fermé, il est caché sous le couvercle.
    const float Rise = 1.5f;
    const float LookRange = 14f;

    public Mood State { get; private set; }

    Transform lid, bust, head;
    GameObject angryEyes;
    Light angryLight;
    float rise, riseVelocity, lidAngle;
    float smileUntil, attackTime = -10f;
    Vector3 attackDir;
    float yaw, pitch;

    public static Pogo Create(Transform anchor)
    {
        var go = new GameObject("Pogo");
        go.transform.SetParent(anchor, false);
        var pogo = go.AddComponent<Pogo>();

        // Boîte (ouverte sur le dessus) et couvercle articulé sur son bord arrière.
        var box = GameArt.SpawnModel("PogoBox", go.transform, BoxHeight);
        float width = 1.5f;
        if (box == null)
        {
            Material red = GameArt.Lit("PogoBoxRed", new Color(0.45f, 0.06f, 0.07f), null, 0.3f);
            Material blue = GameArt.Lit("PogoBoxBlue", new Color(0.12f, 0.22f, 0.45f), null, 0.3f);
            float t = 0.08f;
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, t * 0.5f, 0f), new Vector3(width, t, width), red);
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, BoxHeight * 0.5f, width * 0.5f), new Vector3(width, BoxHeight, t), blue);
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(0f, BoxHeight * 0.5f, -width * 0.5f), new Vector3(width, BoxHeight, t), red);
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(width * 0.5f, BoxHeight * 0.5f, 0f), new Vector3(t, BoxHeight, width), red);
            GameArt.Prim(PrimitiveType.Cube, go.transform, new Vector3(-width * 0.5f, BoxHeight * 0.5f, 0f), new Vector3(t, BoxHeight, width), red);
        }
        else width = Mathf.Max(GameArt.LocalBounds(box.transform).size.x, GameArt.LocalBounds(box.transform).size.z);

        pogo.lid = new GameObject("Charniere").transform;
        pogo.lid.SetParent(go.transform, false);
        pogo.lid.localPosition = new Vector3(0f, BoxHeight, -width * 0.5f);
        var lidMat = GameArt.Lit("PogoLid", new Color(0.4f, 0.07f, 0.08f), null, 0.35f);
        GameArt.Prim(PrimitiveType.Cube, pogo.lid, new Vector3(0f, 0.04f, width * 0.5f), new Vector3(width * 1.04f, 0.08f, width * 1.04f), lidMat);
        GameArt.Prim(PrimitiveType.Cube, pogo.lid, new Vector3(0f, 0.09f, width * 0.5f), new Vector3(width * 0.8f, 0.03f, width * 0.8f),
            GameArt.Lit("PogoGold", new Color(0.75f, 0.58f, 0.2f), null, 0.6f));
        foreach (var c in pogo.lid.GetComponentsInChildren<Collider>()) Destroy(c);

        // Buste du clown : pivot à la base du cou pour regarder les joueurs.
        pogo.bust = new GameObject("Buste").transform;
        pogo.bust.SetParent(go.transform, false);
        pogo.head = new GameObject("Tete").transform;
        pogo.head.SetParent(pogo.bust, false);
        pogo.head.localPosition = new Vector3(0f, NeckY, 0f);
        var model = GameArt.SpawnModel("Pogo", pogo.head, BustHeight);
        if (model != null) model.transform.localPosition = new Vector3(0f, -BustHeight * NeckFraction, 0f);
        else
        {
            GameArt.Prim(PrimitiveType.Sphere, pogo.head, new Vector3(0f, 0.55f, 0f), Vector3.one * 0.55f, GameArt.Lit("PogoFace", new Color(0.92f, 0.88f, 0.82f)));
            GameArt.Prim(PrimitiveType.Cylinder, pogo.head, new Vector3(0f, 0.2f, 0f), new Vector3(0.8f, 0.06f, 0.8f), GameArt.Lit("PogoCollar", new Color(0.9f, 0.88f, 0.8f)));
        }

        // Yeux rouges de colère, posés devant les yeux peints du visage.
        pogo.angryEyes = new GameObject("Yeux");
        pogo.angryEyes.transform.SetParent(pogo.head, false);
        var eyeMat = GameArt.Emissive("PogoAngryEye", new Color(1f, 0.1f, 0.05f), new Color(1f, 0.05f, 0f) * 2.5f);
        foreach (float x in new[] { -EyeSpacing, EyeSpacing })
        {
            var eye = GameArt.Prim(PrimitiveType.Sphere, pogo.angryEyes.transform, new Vector3(x, EyeHeight, EyeDepth), new Vector3(0.09f, 0.065f, 0.04f), eyeMat);
            Destroy(eye.GetComponent<Collider>());
        }
        pogo.angryLight = new GameObject("Colere").AddComponent<Light>();
        pogo.angryLight.transform.SetParent(pogo.head, false);
        pogo.angryLight.transform.localPosition = new Vector3(0f, EyeHeight, 0.4f);
        pogo.angryLight.type = LightType.Point;
        pogo.angryLight.color = new Color(1f, 0.15f, 0.05f);
        pogo.angryLight.range = 3.5f;
        pogo.angryLight.intensity = 2f;
        pogo.angryLight.shadows = LightShadows.None;
        pogo.angryEyes.SetActive(false);
        pogo.angryLight.enabled = false;

        pogo.rise = -Rise;
        pogo.bust.localPosition = new Vector3(0f, pogo.rise, 0f);
        // Sélection : toute la boîte (et le clown quand il en sort).
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = new Vector3(0f, BoxHeight * 0.75f, 0f);
        col.size = new Vector3(width * 1.1f, BoxHeight * 1.5f, width * 1.1f);
        var solid = go.AddComponent<BoxCollider>();
        solid.center = new Vector3(0f, BoxHeight * 0.5f, 0f);
        solid.size = new Vector3(width, BoxHeight, width);
        return pogo;
    }

    // Position des yeux peints dans le repère de la tête (réglée sur le modèle Meshy).
    const float EyeHeight = 0.52f, EyeDepth = 0.36f, EyeSpacing = 0.115f;

    // État décidé par l'hôte. 'attacker' : joueur que Pogo frappe (coup de tête), vide sinon.
    public void SetState(Mood mood, bool smiling, string attacked)
    {
        Mood before = State;
        State = mood;
        Vector3 at = head.position;
        if (before == Mood.Closed && mood != Mood.Closed)
        {
            GameAudio.Play3D("jack_open", at, 1f, 30f, 0f);
            riseVelocity = 9f;
        }
        if (mood == Mood.Angry && before != Mood.Angry) GameAudio.Play3D("jack_growl", at, 1f, 25f, 0.05f);
        if (smiling && Time.time > smileUntil)
        {
            smileUntil = Time.time + GameRules.PogoSmile;
            riseVelocity += 4f;
            GameAudio.Play3D("jack_bells", at, 0.9f, 25f, 0.04f);
        }
        if (!smiling) smileUntil = 0f;
        if (!string.IsNullOrEmpty(attacked))
        {
            PlayerPresence target = null;
            foreach (var p in PlayerPresence.All) if (p.PlayerId == attacked) target = p;
            if (target != null)
            {
                attackTime = Time.time;
                attackDir = target.Position - head.position;
                GameAudio.Play3D("jack_hit", target.Position, 1f, 25f, 0.05f);
            }
        }
        angryEyes.SetActive(mood == Mood.Angry);
        angryLight.enabled = mood == Mood.Angry;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        bool open = State != Mood.Closed;
        // Le couvercle s'ouvre avant que Pogo ne jaillisse, et se referme une fois qu'il est rentré.
        float lidTarget = open || rise > -Rise * 0.85f ? -118f : 0f;
        lidAngle = Mathf.MoveTowards(lidAngle, lidTarget, (open ? 600f : 240f) * dt);
        lid.localRotation = Quaternion.Euler(lidAngle, 0f, 0f);

        // Ressort : Pogo jaillit avec du rebond, et redescend plus lentement.
        float target = open && lidAngle < -60f ? 0f : -Rise;
        riseVelocity += ((target - rise) * (open ? 90f : 40f) - riseVelocity * (open ? 6f : 11f)) * dt;
        rise += riseVelocity * dt;
        rise = Mathf.Max(rise, -Rise);
        bust.localPosition = new Vector3(0f, rise, 0f);

        // Regard fixé sur le joueur le plus proche ; sourire = dodelinement joyeux ; colère = tremblement.
        float wantYaw = 0f, wantPitch = 0f;
        PlayerPresence nearest = Nearest();
        if (open && nearest != null)
        {
            Vector3 d = transform.InverseTransformDirection(nearest.Position + Vector3.up * 0.5f - head.position);
            wantYaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -130f, 130f);
            wantPitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -25f, 35f);
        }
        float k = 1f - Mathf.Exp(-6f * dt);
        yaw = Mathf.LerpAngle(yaw, wantYaw, k);
        pitch = Mathf.Lerp(pitch, wantPitch, k);
        float roll = 0f;
        Vector3 shake = Vector3.zero;
        if (Time.time < smileUntil) roll = Mathf.Sin(Time.time * 9f) * 12f;
        if (State == Mood.Angry) shake = new Vector3(Mathf.Sin(Time.time * 61f), 0f, Mathf.Sin(Time.time * 53f)) * 0.025f;
        head.localRotation = Quaternion.Euler(pitch, yaw, roll);

        // Coup de tête : élan vers la victime puis retour.
        float ta = Time.time - attackTime;
        Vector3 lunge = Vector3.zero;
        if (ta < 0.45f)
        {
            float push = ta < 0.12f ? ta / 0.12f : 1f - (ta - 0.12f) / 0.33f;
            lunge = transform.InverseTransformDirection(attackDir.normalized) * push * 0.9f;
        }
        head.localPosition = new Vector3(0f, NeckY, 0f) + shake + lunge;
    }

    PlayerPresence Nearest()
    {
        PlayerPresence best = null;
        float bestSq = LookRange * LookRange;
        foreach (var p in PlayerPresence.All)
        {
            if (!p.Alive) continue;
            float sq = (p.Position - head.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = p; }
        }
        return best;
    }

    public override string Prompt
    {
        get
        {
            if (State == Mood.Closed) return "Ouvrir la boîte de Pogo";
            var session = GameSession.Instance;
            ItemType held = session != null ? session.SelectedItem : ItemType.None;
            if (held != ItemType.None) return "Vendre " + Items.Get(held).name + " à Pogo (" + Items.Get(held).sellPrice + " golds)";
            return State == Mood.Angry ? "Pogo est en colère..." : "Pogo attend un objet";
        }
    }

    // Pogo lit l'objet sélectionné au moment du clic.
    public override bool KeepsSelection => true;
    public override void Interact() => GameSession.Instance.InteractPogo();
}
