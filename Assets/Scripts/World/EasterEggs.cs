using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Easter Eggs du chapitre 15.1 (V0.6.14). Purement visuels et locaux : chaque joueur les voit (et les fait disparaître) chez lui,
// sans rien envoyer sur le réseau.

// EE1 : silhouette noire (tête et épaules) derrière une fenêtre allumée. Dès que le joueur la regarde, elle s'efface en
// 1 seconde (elle se fond dans la lumière de la vitre), et ne revient pas.
public class WindowSilhouette : MonoBehaviour
{
    const float LookRange = 120f, LookAngle = 9f;

    Material mat;
    float fade = -1f;

    // 'basePos' : bas de la vitre centrale, au milieu ; 'along' : le long du mur ; 'normal' : vers la salle.
    public static WindowSilhouette Create(Transform parent, Vector3 basePos, Vector3 along, Vector3 normal, float paneWidth, float paneHeight)
    {
        var go = new GameObject("EE1_Silhouette");
        go.transform.SetParent(parent, false);
        go.transform.position = basePos;
        go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
        var s = go.AddComponent<WindowSilhouette>();
        s.mat = new Material(GameArt.WindowLit) { name = "Silhouette", color = Color.black };
        GameArt.MeshObject("Forme", go.transform, Bust(paneWidth * 0.55f, paneHeight * 0.95f), s.mat);
        return s;
    }

    // Buste vu de face, le bas posé sur l'allège : épaules arrondies, cou, tête ovale. Plan XY, visible de -Z et de +Z.
    static Mesh Bust(float width, float height)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        void Fan(Vector3 center, List<Vector3> rim)
        {
            int c = v.Count;
            v.Add(center);
            v.AddRange(rim);
            for (int k = 0; k < rim.Count; k++)
            {
                int a = c + 1 + k, b = c + 1 + (k + 1) % rim.Count;
                t.AddRange(new[] { c, a, b, c, b, a });
            }
        }
        float headR = height * 0.17f, shoulders = height * 0.55f, neck = width * 0.12f;
        // Torse : un dôme aplati jusqu'aux épaules.
        var torso = new List<Vector3>();
        for (int k = 0; k <= 16; k++)
        {
            float a = Mathf.PI * k / 16f;
            torso.Add(new Vector3(Mathf.Cos(a) * width * 0.5f, Mathf.Min(Mathf.Sin(a) * shoulders * 1.25f, shoulders), 0f));
        }
        Fan(new Vector3(0f, shoulders * 0.4f, 0f), torso);
        // Cou.
        var neckRim = new List<Vector3> { new Vector3(-neck, shoulders - 0.02f, 0f), new Vector3(neck, shoulders - 0.02f, 0f),
            new Vector3(neck * 0.9f, shoulders + headR * 0.6f, 0f), new Vector3(-neck * 0.9f, shoulders + headR * 0.6f, 0f) };
        Fan(new Vector3(0f, shoulders + headR * 0.3f, 0f), neckRim);
        // Tête.
        var head = new List<Vector3>();
        Vector3 hc = new Vector3(0f, shoulders + headR * 1.35f, 0f);
        for (int k = 0; k < 20; k++)
        {
            float a = Mathf.PI * 2f * k / 20f;
            head.Add(hc + new Vector3(Mathf.Cos(a) * headR * 0.82f, Mathf.Sin(a) * headR, 0f));
        }
        Fan(hc, head);
        var m = new Mesh { name = "Silhouette" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void Update()
    {
        if (fade >= 0f)
        {
            fade += Time.deltaTime / GameRules.SilhouetteFade;
            mat.color = Color.Lerp(Color.black, GameArt.WindowLit.color, fade);
            if (fade >= 1f) gameObject.SetActive(false);
            return;
        }
        Camera cam = Camera.main;
        if (cam != null && EasterEggs.Looking(cam, transform.position + Vector3.up * 0.6f, LookRange, LookAngle)) fade = 0f;
    }
}

public static class EasterEggs
{
    // EE2 : armé (2 % de chance) au début de chaque partie ; le premier œil que voit le joueur se change en caméra.
    public static bool EyeCameraArmed;

    public static void RollNewGame() => EyeCameraArmed = Random.value < GameRules.EyeCameraChance;

    // Le joueur regarde ce point : assez près, presque au centre de l'écran, et rien (mur, décor) entre la caméra et lui.
    public static bool Looking(Camera cam, Vector3 point, float range, float maxAngle)
    {
        Vector3 to = point - cam.transform.position;
        float dist = to.magnitude;
        if (dist > range || dist < 0.1f || Vector3.Angle(cam.transform.forward, to) > maxAngle) return false;
        return Visible(cam, point, dist);
    }

    // Dans le champ de la caméra et sans obstacle (le personnage du joueur ne compte pas).
    public static bool Seen(Camera cam, Vector3 point, float range)
    {
        Vector3 vp = cam.WorldToViewportPoint(point);
        if (vp.z <= 0f || vp.z > range || vp.x < 0.08f || vp.x > 0.92f || vp.y < 0.08f || vp.y > 0.92f) return false;
        return Visible(cam, point, (point - cam.transform.position).magnitude);
    }

    static bool Visible(Camera cam, Vector3 point, float dist)
    {
        Vector3 from = cam.transform.position, dir = (point - from) / dist;
        foreach (var hit in Physics.RaycastAll(from, dir, dist - 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;
            return false;
        }
        return true;
    }

    // L'œil se change en caméra de surveillance, puis en œil, puis en caméra... de plus en plus vite, presque comme un bug,
    // pendant 1,5 seconde ; il redevient un œil.
    public static IEnumerator EyeToCamera(Transform eye)
    {
        var eyeParts = new List<Renderer>(eye.GetComponentsInChildren<Renderer>(true));
        var cam = BuildCamera(eye);
        float end = Time.time + GameRules.EyeCameraDuration;
        bool camera = true;
        while (Time.time < end && eye != null)
        {
            foreach (var r in eyeParts) if (r != null) r.enabled = !camera;
            cam.SetActive(camera);
            camera = !camera;
            float left = end - Time.time;
            yield return new WaitForSeconds(Mathf.Lerp(0.04f, 0.16f, left / GameRules.EyeCameraDuration) * Random.Range(0.6f, 1.4f));
        }
        if (cam != null) Object.Destroy(cam);
        foreach (var r in eyeParts) if (r != null) r.enabled = true;
    }

    // Caméra de surveillance à la place de l'œil (même taille) : boîtier gris, objectif noir, voyant rouge.
    static GameObject BuildCamera(Transform eye)
    {
        var root = new GameObject("EE2_Camera");
        root.transform.SetParent(eye, false);
        Material body = GameArt.Lit("CameraBoitier", new Color(0.72f, 0.73f, 0.74f), null, 0.55f);
        Material black = GameArt.Lit("CameraObjectif", new Color(0.02f, 0.02f, 0.02f), null, 0.9f);
        Prim(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.05f, 0.1f), new Vector3(0.62f, 0.5f, 1.1f), body);
        var lens = Prim(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0.68f), new Vector3(0.36f, 0.06f, 0.36f), black);
        lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Prim(root.transform, PrimitiveType.Cube, new Vector3(0f, 0.38f, 0.25f), new Vector3(0.7f, 0.06f, 1.25f), body);
        Prim(root.transform, PrimitiveType.Sphere, new Vector3(0.2f, 0.22f, 0.66f), Vector3.one * 0.07f, GameArt.Unlit("VoyantRouge", new Color(1f, 0.1f, 0.08f)));
        Prim(root.transform, PrimitiveType.Cube, new Vector3(0f, -0.1f, -0.55f), new Vector3(0.18f, 0.18f, 0.4f), body);
        root.SetActive(false);
        return root;
    }

    static GameObject Prim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameArt.Prim(type, parent, pos, size, mat);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }
}
