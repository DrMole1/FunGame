using System.Collections.Generic;
using UnityEngine;

// Chapitre 4 (V0.7) : décor du chapiteau.
// - La gamelle de cirque (modèle Meshy "Gamelle"), gravée "WOOF", posée au sol sous le Rendu de Gamelle : ni ramassable ni
//   interactive. Elle se remplit de croquettes (modèle Meshy "Croquette") selon la gamelle commune, identique dans tous
//   les Hubs.
// - Quatre rideaux rouges de cirque (modèle Meshy "Rideau" : drapé à cantonnière et franges dorées) sur leurs cintres dorés
//   en arc (modèle Meshy "Tringle", cintré), derrière les Chenils, sans collision. Les guirlandes restent derrière eux.
public static class HubDecor
{
    // Rideaux : de part et d'autre de l'axe des portes, derrière les premiers et les derniers Chenils (schéma du GDD).
    static readonly float[] CurtainAngles = { 55f, -55f, 120f, -120f };
    // Rideaux retirés pour le moment (demande du 13/11) : passer à true pour les réactiver, tout le reste est prêt.
    static readonly bool CurtainsEnabled = false;
    const float CurtainWidth = 4.2f, CurtainBottom = 0.3f, RodHeight = 5.05f, CurtainRadius = 11.25f;
    // Gamelle : sous le Rendu de Gamelle, au pied du mur.
    public const float BowlAngle = 28f, BowlRadius = 10.7f;

    public static void Build(Transform hub, Material gold, Material red)
    {
        if (CurtainsEnabled)
            foreach (float angle in CurtainAngles) AddCurtain(hub, angle, gold, red);
        HubBowl.Create(hub, Polar(BowlAngle, BowlRadius, 0f), Quaternion.Euler(0f, BowlAngle + 180f, 0f));
    }

    static Vector3 Polar(float angle, float r, float y)
    {
        float a = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
    }

    // Note temporaire 02 (V0.7) : la tringle est un cintre en arc, épais, aux couleurs exactes des guirlandes ; le rideau
    // (drapé à cantonnière et franges) garde le rouge du chapiteau et l'or des guirlandes (texture recolorée à l'import).
    const float RodLength = CurtainWidth + 0.6f, RodArch = 0.3f, RodThickness = 0.7f, RodBarRadius = 0.065f, RodBarSpan = 0.42f;

    static void AddCurtain(Transform hub, float angle, Material gold, Material red)
    {
        var holder = new GameObject("Rideau").transform;
        holder.SetParent(hub, false);
        holder.localPosition = Polar(angle, CurtainRadius, 0f);
        // Face au centre du chapiteau.
        holder.localRotation = Quaternion.Euler(0f, angle + 180f, 0f);

        float height = RodHeight - 0.05f - CurtainBottom;
        var curtain = GameArt.SpawnModel("Rideau", holder, height);
        if (curtain != null)
        {
            Bounds b = GameArt.LocalBounds(curtain.transform);
            // Plaqué au mur : son épaisseur ramenée à 25 cm au plus.
            // Symétrie du chapiteau : les rideaux du nord sont en miroir de ceux du sud, et ceux de l'ouest en miroir de ceux de
            // l'est (le drapé tombe de l'autre côté).
            float mirror = (Mathf.Abs(angle) < 90f ? -1f : 1f) * (angle < 0f ? -1f : 1f);
            curtain.transform.localScale = new Vector3(mirror * CurtainWidth / Mathf.Max(0.01f, b.size.x), 1f, Mathf.Min(1f, 0.25f / Mathf.Max(0.01f, b.size.z)));
            curtain.transform.localPosition = Vector3.up * CurtainBottom;
            foreach (var r in curtain.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials) if (m != null) m.SetFloat("_Glossiness", 0.4f);
        }
        else
        {
            var drape = GameArt.Prim(PrimitiveType.Cube, holder, new Vector3(0f, CurtainBottom + height * 0.5f, 0f), new Vector3(CurtainWidth, height, 0.08f), red);
            Object.Destroy(drape.GetComponent<Collider>());
        }

        Mesh bent = BentRod();
        if (bent != null)
        {
            var rod = GameArt.MeshObject("Cintre", holder, bent, gold);
            rod.transform.localPosition = new Vector3(0f, RodHeight, 0.12f);
        }
        else
        {
            var bar = GameArt.Prim(PrimitiveType.Cylinder, holder, new Vector3(0f, RodHeight, 0.12f), new Vector3(0.1f, RodLength * 0.5f, 0.1f), gold);
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Object.Destroy(bar.GetComponent<Collider>());
        }
        // Les personnages les traversent : aucune collision, aucune ombre portée sur la piste.
        foreach (var c in holder.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        foreach (var r in holder.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Cintre : le modèle Meshy "Tringle" (barre ornée, pommeaux aux deux bouts) allongé à RodLength, épaissi à RodThickness
    // de haut et cintré en arc (le milieu RodArch plus haut que les bouts), comme les festons des guirlandes. Origine : au
    // milieu de la barre, à hauteur de ses bouts. Partagé par tous les rideaux.
    static Mesh bentRod;
    static Mesh BentRod()
    {
        if (bentRod != null) return bentRod;
        var prefab = Resources.Load<GameObject>("Art/Tringle");
        var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return null;
        Mesh src = mf.sharedMesh;
        Matrix4x4 m = mf.transform == prefab.transform
            ? Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale)
            : prefab.transform.worldToLocalMatrix.inverse * mf.transform.localToWorldMatrix;
        var verts = src.vertices;
        var bounds = new Bounds(m.MultiplyPoint3x4(verts[0]), Vector3.zero);
        for (int i = 0; i < verts.Length; i++) { verts[i] = m.MultiplyPoint3x4(verts[i]); bounds.Encapsulate(verts[i]); }
        // L'axe le plus long est celui de la barre.
        bool alongZ = bounds.size.z > bounds.size.x;
        float length = alongZ ? bounds.size.z : bounds.size.x;
        float thick = RodThickness / Mathf.Max(1e-4f, bounds.size.y);
        // Axe de la barre du haut (le modèle en a une seconde, plus fine, en dessous) : sur le tronçon nu entre le milieu
        // et les pommeaux, le quart supérieur des sommets en hauteur, la médiane en profondeur.
        Vector3 axis = bounds.center;
        {
            var ys = new List<float>();
            var ds = new List<float>();
            foreach (Vector3 p in verts)
            {
                float u = Mathf.Abs(((alongZ ? p.z : p.x) - (alongZ ? bounds.center.z : bounds.center.x)) / length);
                if (u < 0.15f || u > 0.3f) continue;
                ys.Add(p.y);
                ds.Add(alongZ ? p.x : p.z);
            }
            if (ys.Count > 8)
            {
                ys.Sort(); ds.Sort();
                axis.y = ys[ys.Count * 3 / 4];
                if (alongZ) axis.x = ds[ds.Count / 2]; else axis.z = ds[ds.Count / 2];
            }
        }
        var outV = new List<Vector3>(verts.Length + 600);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i] - axis;
            float t = (alongZ ? v.z : v.x) / length;                    // -0,5 .. 0,5
            float side = alongZ ? -v.x : v.z;
            outV.Add(Arc(t, v.y * thick, side * thick));
        }
        var outUv = new List<Vector2>(src.uv);
        while (outUv.Count < outV.Count) outUv.Add(Vector2.zero);
        // La seconde barre, fine, au-dessus de la barre principale : retirée entre les pommeaux (une seule barre épaisse).
        var tris = new List<int>();
        var srcTris = src.triangles;
        var cut = new bool[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i] - axis;
            // Près des bouts, seuil plus haut : les gros pommeaux de la barre principale restent entiers.
            float limit = Mathf.Abs((alongZ ? v.z : v.x) / length) < 0.4f ? RodBarRadius * 1.3f : 0.17f;
            cut[i] = v.y * thick > limit;
        }
        for (int i = 0; i < srcTris.Length; i += 3)
            if (!(cut[srcTris[i]] && cut[srcTris[i + 1]] && cut[srcTris[i + 2]]))
                tris.AddRange(new[] { srcTris[i], srcTris[i + 1], srcTris[i + 2] });
        // Barre épaisse le long de l'arc, entre les pommeaux.
        const int along = 32, around = 14;
        int start = outV.Count;
        for (int i = 0; i <= along; i++)
        {
            float t = Mathf.Lerp(-RodBarSpan, RodBarSpan, (float)i / along);
            for (int j = 0; j < around; j++)
            {
                float a = j * Mathf.PI * 2f / around;
                outV.Add(Arc(t, Mathf.Cos(a) * RodBarRadius, Mathf.Sin(a) * RodBarRadius));
                outUv.Add(new Vector2((float)i / along, (float)j / around));
            }
        }
        for (int i = 0; i < along; i++)
            for (int j = 0; j < around; j++)
            {
                int j1 = (j + 1) % around;
                int a0 = start + i * around + j, a1 = start + i * around + j1, b0 = a0 + around, b1 = a1 + around;
                tris.AddRange(new[] { a0, a1, b1, a0, b1, b0 });
            }
        bentRod = new Mesh { name = "CintreRideau" };
        bentRod.SetVertices(outV);
        bentRod.SetUVs(0, outUv);
        bentRod.SetTriangles(tris, 0);
        bentRod.RecalculateNormals();
        bentRod.RecalculateBounds();
        return bentRod;
    }

    // Point d'abscisse 't' (-0,5 .. 0,5 de la longueur) de l'arc, décalé de 'h' (vers le haut) et 'd' (vers l'avant) dans la
    // section, qui suit la pente de la courbe.
    static Vector3 Arc(float t, float h, float d)
    {
        float y = RodArch * (1f - 4f * t * t);
        float slope = -8f * RodArch * t / RodLength;
        float cos = 1f / Mathf.Sqrt(1f + slope * slope), sin = slope * cos;
        return new Vector3(t * RodLength - h * sin, y + h * cos, d);
    }
}

// Gamelle physique du Hub. Toutes les gamelles affichent la même quantité : GameSession règle HubBowl.Amount (la gamelle
// commune) et chacune se remplit à son rythme.
// Remplissage imagé : jusqu'à LooseMax croquettes, une croquette du modèle par croquette de la gamelle commune, au fond.
// Au-delà, un tas de croquettes monte dans la gamelle (de plus en plus lentement, plein vers FillScale x 3) et les croquettes
// du modèle se répartissent sur son dessus.
// V0.7.2 (chapitre 3.5) : à 99 de faim ou moins, la gamelle devient interactive : un clic pour se nourrir.
public class HubBowl : Interactable
{
    public static int Amount;
    static readonly List<HubBowl> all = new List<HubBowl>();

    const int LooseMax = 40;
    const float FillScale = 1500f;
    // Intérieur de la gamelle (modèle de 1,36 m de diamètre et 0,4 m de haut).
    const float Height = 0.4f, InnerBottom = 0.05f, InnerTop = 0.36f, InnerRadiusBottom = 0.57f, InnerRadiusTop = 0.62f;
    const float KibbleSize = 0.085f;

    int shown = -1;
    Transform heap;
    readonly List<Transform> kibbles = new List<Transform>();

    public static HubBowl Create(Transform hub, Vector3 localPos, Quaternion localRot)
    {
        var go = new GameObject("Gamelle");
        go.transform.SetParent(hub, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        var bowl = go.AddComponent<HubBowl>();
        bowl.Build();
        return bowl;
    }

    void OnEnable() => all.Add(this);
    void OnDisable()
    {
        all.Remove(this);
        SetHighlight(false);
    }

    public override bool CanInteract => GameSession.Instance != null && GameSession.Instance.CanEat;
    public override string Prompt => CanInteract ? Loc.T("Se nourrir") : "";
    public override void Interact() => GameSession.Instance.Eat();

    void Build()
    {
        var model = GameArt.SpawnModel("Gamelle", transform, Height);
        if (model == null)
        {
            var dish = GameArt.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, Height * 0.5f, 0f), new Vector3(1.36f, Height * 0.5f, 1.36f),
                GameArt.Lit("GamelleRouge", new Color(0.72f, 0.08f, 0.1f), null, 0.6f));
            Object.Destroy(dish.GetComponent<Collider>());
        }
        // "WOOF" gravé sur le flanc, face au centre du chapiteau : une lettre par facette, épousant l'arrondi.
        const string word = "WOOF";
        for (int i = 0; i < word.Length; i++)
        {
            float a = (i - 1.5f) * 9f;
            var pivot = new GameObject("Gravure").transform;
            pivot.SetParent(transform, false);
            pivot.localRotation = Quaternion.Euler(0f, a, 0f);
            var letter = GameArt.CreateText(pivot, word[i].ToString(), new Vector3(0f, Height * 0.5f, 0.693f), 0.0045f, new Color(0.16f, 0.05f, 0.03f));
            letter.fontStyle = FontStyle.Bold;
            // TextMesh se lit vers -Z : retourné vers l'extérieur de la gamelle.
            letter.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        // Tas de croquettes (dôme), caché tant qu'il y en a peu.
        heap = new GameObject("TasCroquettes").transform;
        heap.SetParent(transform, false);
        heap.gameObject.AddComponent<MeshFilter>().sharedMesh = DomeMesh();
        var heapRenderer = heap.gameObject.AddComponent<MeshRenderer>();
        heapRenderer.sharedMaterial = GameArt.Lit("TasCroquettes", new Color(0.62f, 0.38f, 0.18f), GameArt.Noise("croquettes", 128, 0.7f, 0.3f, 31), 0.4f);
        heapRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        heap.gameObject.SetActive(false);

        for (int i = 0; i < LooseMax; i++)
        {
            var k = GameArt.SpawnModel("Croquette", transform, KibbleSize);
            if (k == null)
            {
                k = GameArt.Prim(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * KibbleSize, GameArt.Lit("CroquetteSimple", new Color(0.45f, 0.26f, 0.12f), null, 0.3f));
                Object.Destroy(k.GetComponent<Collider>());
            }
            foreach (var r in k.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            k.SetActive(false);
            kibbles.Add(k.transform);
        }
        foreach (var c in GetComponentsInChildren<Collider>()) Object.Destroy(c);
        // Zone de sélection (clic pour manger).
        FitTrigger(new Vector3(1.4f, 0.6f, 1.4f));
        Refresh(Amount);
    }

    void Update()
    {
        if (shown != Amount) Refresh(Amount);
    }

    // Hauteur du tas (0 : vide, 1 : à ras bord).
    static float Fill(int amount) => amount <= LooseMax ? 0f : 1f - Mathf.Exp(-(amount - LooseMax) / FillScale);

    void Refresh(int amount)
    {
        shown = amount;
        float fill = Fill(amount);
        float level = Mathf.Lerp(InnerBottom, InnerTop, fill);
        float radius = Mathf.Lerp(InnerRadiusBottom, InnerRadiusTop, fill);
        heap.gameObject.SetActive(fill > 0.001f);
        heap.localPosition = Vector3.up * level;
        heap.localScale = new Vector3(radius, 1f, radius);

        // Mêmes positions dans tous les Hubs pour une même quantité.
        var rnd = new System.Random(1234);
        int visible = Mathf.Min(amount, LooseMax);
        for (int i = 0; i < kibbles.Count; i++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rnd.NextDouble()) * (radius - 0.06f);
            Vector3 rot = new Vector3((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f);
            Transform k = kibbles[i];
            k.gameObject.SetActive(i < visible);
            if (i >= visible) continue;
            float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
            // Sur le dôme du tas, ou au fond de la gamelle.
            float y = fill > 0.001f ? level + DomeHeight * (1f - (d * d) / (radius * radius)) : InnerBottom;
            k.localPosition = new Vector3(x, y, z);
            k.localRotation = Quaternion.Euler(rot);
        }
    }

    const float DomeHeight = 0.05f;

    // Dôme de rayon 1 (mis à l'échelle), légèrement bombé, bosselé comme un tas de croquettes.
    static Mesh dome;
    static Mesh DomeMesh()
    {
        if (dome != null) return dome;
        const int rings = 8, sides = 32;
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        v.Add(new Vector3(0f, DomeHeight, 0f));
        uv.Add(new Vector2(0.5f, 0.5f));
        var rnd = new System.Random(77);
        for (int r = 1; r <= rings; r++)
        {
            float d = (float)r / rings;
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                float bump = r < rings ? ((float)rnd.NextDouble() - 0.5f) * 0.02f : 0f;
                v.Add(new Vector3(Mathf.Cos(a) * d, DomeHeight * (1f - d * d) + bump, Mathf.Sin(a) * d));
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * d * 2f, 0.5f + Mathf.Sin(a) * d * 2f));
            }
        }
        for (int s = 0; s < sides; s++) t.AddRange(new[] { 0, 1 + (s + 1) % sides, 1 + s });
        for (int r = 1; r < rings; r++)
        {
            int a0 = 1 + (r - 1) * sides, b0 = 1 + r * sides;
            for (int s = 0; s < sides; s++)
            {
                int s1 = (s + 1) % sides;
                t.AddRange(new[] { a0 + s, a0 + s1, b0 + s1, a0 + s, b0 + s1, b0 + s });
            }
        }
        dome = new Mesh { name = "TasCroquettes" };
        dome.SetVertices(v);
        dome.SetUVs(0, uv);
        dome.SetTriangles(t, 0);
        dome.RecalculateNormals();
        dome.RecalculateBounds();
        return dome;
    }
}
