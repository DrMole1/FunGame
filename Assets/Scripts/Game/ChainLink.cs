using UnityEngine;

// Maillon de la chaîne du Roi, en fer rouillé, allongé dans l'axe Z local et long d'environ 24 cm (les maillons se
// chevauchent le long de la chaîne). Un modèle Resources/Art/Maillon le remplace s'il existe ; sinon, anneau ovale généré
// (les essais Meshy de la V0.4.4 ne donnaient pas un maillon fermé).
public static class ChainLink
{
    const float Length = 0.24f;

    static Mesh mesh;
    static Quaternion meshRotation = Quaternion.identity;
    static float meshScale = 1f;
    static Vector3 meshOffset;

    public static Transform Create(Transform parent)
    {
        if (mesh == null) Prepare();
        var link = new GameObject("Maillon").transform;
        link.SetParent(parent, false);
        var go = GameArt.MeshObject("Forme", link, mesh, GameArt.Lit("RoiChain", new Color(0.32f, 0.27f, 0.23f),
            GameArt.Noise("rust", 64, 0.55f, 0.35f, 23), 0.45f));
        go.transform.localRotation = meshRotation;
        go.transform.localScale = Vector3.one * meshScale;
        go.transform.localPosition = meshRotation * meshOffset * meshScale;
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return link;
    }

    // Copie le maillon en débris physique : il est projeté loin du point de rupture, tombe, roule, puis rétrécit
    // et disparaît. Aucune incidence sur le jeu (calque Ignore Raycast, chez chaque joueur).
    public static void Shatter(Transform link, Vector3 breakPoint)
    {
        var piece = Object.Instantiate(link.gameObject, link.position, link.rotation);
        piece.name = "Maillon brisé";
        piece.layer = 2;
        foreach (Transform t in piece.GetComponentsInChildren<Transform>()) t.gameObject.layer = 2;
        var box = piece.AddComponent<BoxCollider>();
        box.size = new Vector3(0.05f, 0.1f, Length * 0.9f);
        var rb = piece.AddComponent<Rigidbody>();
        rb.mass = 0.3f;
        Vector3 away = link.position - breakPoint;
        // Plus près de la rupture, plus violemment projeté.
        float burst = Mathf.Lerp(3f, 0.6f, Mathf.Clamp01(away.magnitude / 4f));
        rb.velocity = away.normalized * burst + Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(0.5f, 2f);
        rb.angularVelocity = Random.insideUnitSphere * 12f;
        piece.AddComponent<LinkDebris>();
    }

    static void Prepare()
    {
        var model = Resources.Load<GameObject>("Art/Maillon");
        var filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
        mesh = filter != null ? filter.sharedMesh : Ring();
        Bounds b = mesh.bounds;
        Vector3 s = b.size;
        // Le plus grand côté devient l'axe Z (le long de la chaîne), le plus fin l'axe X.
        Vector3 longAxis = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
        Vector3 thinAxis = s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
        if (thinAxis == longAxis) thinAxis = longAxis == Vector3.right ? Vector3.up : Vector3.right;
        meshRotation = Quaternion.Inverse(Quaternion.LookRotation(longAxis, Vector3.Cross(longAxis, thinAxis)));
        meshScale = Length / Mathf.Max(0.0001f, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
        meshOffset = -b.center;
    }

    // Anneau ovale : un tore étiré (deux demi-cercles reliés par deux barres droites), dans le plan YZ.
    static Mesh Ring()
    {
        const int around = 24, tube = 8;
        const float radius = 0.06f, straight = 0.06f, thickness = 0.018f;
        var verts = new Vector3[around * tube];
        var normals = new Vector3[verts.Length];
        for (int i = 0; i < around; i++)
        {
            float a = i * Mathf.PI * 2f / around;
            Vector3 dir = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
            Vector3 center = dir * radius + Vector3.forward * (Mathf.Cos(a) >= 0f ? straight : -straight);
            for (int j = 0; j < tube; j++)
            {
                float b = j * Mathf.PI * 2f / tube;
                Vector3 n = dir * Mathf.Cos(b) + Vector3.right * Mathf.Sin(b);
                verts[i * tube + j] = center + n * thickness;
                normals[i * tube + j] = n;
            }
        }
        var tris = new int[around * tube * 6];
        int t = 0;
        for (int i = 0; i < around; i++)
            for (int j = 0; j < tube; j++)
            {
                int a = i * tube + j, b = (i + 1) % around * tube + j;
                int c = (i + 1) % around * tube + (j + 1) % tube, d = i * tube + (j + 1) % tube;
                tris[t++] = a; tris[t++] = d; tris[t++] = b;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
        var m = new Mesh { name = "Maillon", vertices = verts, normals = normals, triangles = tris };
        m.RecalculateBounds();
        return m;
    }
}

// Maillon tombé au sol : rétrécit puis disparaît au bout de quelques secondes.
public class LinkDebris : MonoBehaviour
{
    const float Life = 3.5f, Fade = 0.6f;
    float age;
    Vector3 scale;

    void Start() => scale = transform.localScale;

    void Update()
    {
        age += Time.deltaTime;
        if (age > Life - Fade) transform.localScale = scale * Mathf.Clamp01((Life - age) / Fade);
        if (age >= Life) Destroy(gameObject);
    }
}
