using System.Collections.Generic;
using UnityEngine;

// V0.7.11 (chapitre 11) : outils de construction des environnements Château et Blockhaus : boîtes alignées ou tournées (damier
// continu, coordonnées monde), collisions du bâti, escaliers droits et en colimaçon, tours rondes, rubans (câbles), soustraction de
// rectangles.
public static partial class WorldBuilder
{
    // Pièces de géométrie d'une structure, fusionnées en quelques maillages (un par matériau) à la fin.
    internal class Kit
    {
        public readonly Transform root, colliders;
        public readonly MeshBuilder walls = new MeshBuilder(), floors = new MeshBuilder(), dark = new MeshBuilder(), grey = new MeshBuilder();
        public readonly string name;

        public Kit(Transform root, Transform colliders, string name)
        {
            this.root = root;
            this.colliders = colliders;
            this.name = name;
        }

        public void Collider(Vector3 center, Vector3 size)
        {
            var bc = colliders.gameObject.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
        }

        // Boîte alignée (dessinée et solide).
        public void Box(Vector3 center, Vector3 size, bool collide = true, MeshBuilder mb = null)
        {
            if (size.x <= 0.001f || size.y <= 0.001f || size.z <= 0.001f) return;
            (mb ?? walls).Box(center, size, 0.5f);
            if (collide) Collider(center, size);
        }

        // Boîte tournée autour de l'axe vertical.
        public void Turned(Vector3 center, Vector3 size, float yaw, bool collide = true, MeshBuilder mb = null)
        {
            if (size.x <= 0.001f || size.y <= 0.001f || size.z <= 0.001f) return;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            (mb ?? walls).WorldMesh(UnitCube.vertices, UnitCube.triangles, Matrix4x4.TRS(center, rot, size), 0.5f);
            if (!collide) return;
            var go = new GameObject("Bloc");
            go.transform.SetParent(colliders, false);
            go.transform.SetPositionAndRotation(center, rot);
            go.AddComponent<BoxCollider>().size = size;
        }

        // Sol horizontal (dessus seulement) et sa collision (dalle de 'thick' sous la surface).
        public void Floor(Rect r, float y, float thick = 0.5f, MeshBuilder mb = null, bool collide = true)
        {
            if (r.width <= 0.001f || r.height <= 0.001f) return;
            (mb ?? floors).WorldQuad(new Vector3(r.xMin, y, r.yMin), new Vector3(r.xMax, y, r.yMin), new Vector3(r.xMax, y, r.yMax), new Vector3(r.xMin, y, r.yMax), Vector3.up, 0.5f);
            if (collide) Collider(new Vector3(r.center.x, y - thick * 0.5f, r.center.y), new Vector3(r.width, thick, r.height));
        }

        // Dalle épaisse : dessus (sol), dessous (plafond) et tranches.
        public void Slab(Rect r, float top, float thick)
        {
            if (r.width <= 0.001f || r.height <= 0.001f) return;
            Box(new Vector3(r.center.x, top - thick * 0.5f, r.center.y), new Vector3(r.width, thick, r.height), true, walls);
        }

        public void Flush(Material wallMat = null)
        {
            if (!walls.IsEmpty) GameArt.MeshObject(name + "_Murs", root, walls.Build(name), wallMat != null ? wallMat : GameArt.CheckerWall);
            if (!floors.IsEmpty) GameArt.MeshObject(name + "_Sols", root, floors.Build(name), GameArt.CheckerFloor);
            if (!dark.IsEmpty) GameArt.MeshObject(name + "_Sombre", root, dark.Build(name), GameArt.WindowDark);
            if (!grey.IsEmpty) GameArt.MeshObject(name + "_Gris", root, grey.Build(name), GreyCeiling);
        }
    }

    static Material GreyCeiling => GameArt.Lit("PlafondGris", new Color(0.42f, 0.42f, 0.43f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);

    static Mesh unitCube;
    static Mesh UnitCube
    {
        get
        {
            if (unitCube != null) return unitCube;
            var mb = new MeshBuilder();
            mb.Box(Vector3.zero, Vector3.one, 1f);
            return unitCube = mb.Build("CubeUnite");
        }
    }

    // ---------- Rectangles ----------

    // 'a' privé de 'b' : jusqu'à quatre rectangles.
    static List<Rect> Subtract(Rect a, Rect b)
    {
        var result = new List<Rect>();
        if (!a.Overlaps(b) || a.xMax <= b.xMin || a.xMin >= b.xMax || a.yMax <= b.yMin || a.yMin >= b.yMax)
        {
            result.Add(a);
            return result;
        }
        if (b.yMin > a.yMin) result.Add(Rect.MinMaxRect(a.xMin, a.yMin, a.xMax, b.yMin));
        if (b.yMax < a.yMax) result.Add(Rect.MinMaxRect(a.xMin, b.yMax, a.xMax, a.yMax));
        float y0 = Mathf.Max(a.yMin, b.yMin), y1 = Mathf.Min(a.yMax, b.yMax);
        if (b.xMin > a.xMin) result.Add(Rect.MinMaxRect(a.xMin, y0, b.xMin, y1));
        if (b.xMax < a.xMax) result.Add(Rect.MinMaxRect(b.xMax, y0, a.xMax, y1));
        return result;
    }

    static List<Rect> Subtract(List<Rect> pieces, IEnumerable<Rect> holes)
    {
        foreach (Rect h in holes)
        {
            var next = new List<Rect>();
            foreach (Rect p in pieces) next.AddRange(Subtract(p, h));
            pieces = next;
        }
        return pieces;
    }

    // ---------- Escaliers ----------

    // Escalier droit plein : son bas au milieu de 'foot', il monte dans 'dir' (axe X ou Z) de 'y0' à 'y1'. Marches dessinées,
    // collision en rampe (comme les autres escaliers du jeu : les entités l'empruntent).
    const float KitStepRise = 0.25f, KitStepRun = 0.3f;

    static float StairRun(float rise) => Mathf.CeilToInt(rise / KitStepRise) * KitStepRun;

    static void KitStairs(Kit kit, Vector3 foot, Vector3 dir, float width, float y0, float y1, bool solid = true)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / KitStepRise));
        float rise = (y1 - y0) / steps;
        bool alongX = Mathf.Abs(dir.x) > 0.5f;
        for (int k = 1; k <= steps; k++)
        {
            float top = y0 + k * rise;
            float bottom = solid ? y0 : top - 0.35f;
            Vector3 c = foot + dir * ((k - 0.5f) * KitStepRun) + Vector3.up * ((top + bottom) * 0.5f);
            Vector3 size = alongX ? new Vector3(KitStepRun, top - bottom, width) : new Vector3(width, top - bottom, KitStepRun);
            kit.Box(c, size, false);
        }
        Vector3 head = foot + dir * (steps * KitStepRun);
        AddRamp(kit.colliders, foot + Vector3.up * y0, head + Vector3.up * y1, width);
    }

    // Escalier en colimaçon autour de 'center', entre les rayons 'rIn' et 'rOut', de 'y0' à 'y1', en partant de l'angle 'start' (degrés,
    // 0 = +Z, sens horaire vu du dessus). Retourne l'angle d'arrivée. Chaque marche a sa collision (les entités l'empruntent).
    const float SpiralRise = 0.2f, SpiralTread = 0.34f;

    // V0.7.16 (Note 04) : décalage qui sépare deux faces qui seraient dans le même plan (scintillement des textures).
    const float SeamLift = 0.01f;

    static float KitSpiral(Kit kit, Vector3 center, float rIn, float rOut, float y0, float y1, float start)
    {
        float rMid = (rIn + rOut) * 0.5f;
        float stepAngle = SpiralTread / rMid * Mathf.Rad2Deg;
        int steps = Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / SpiralRise));
        float rise = (y1 - y0) / steps;
        float angle = start;
        // V0.7.15 (Note 03) : chaque marche fait 'rOut - rIn' (3 m) du centre vers le mur, et assez de profondeur pour couvrir l'écart
        // avec la suivante jusqu'au bord extérieur (avant, la boîte était tournée d'un quart de tour : 3 m le long de la rampe et
        // 0,4 m du centre vers le mur, d'où un escalier très étroit).
        float width = rOut - rIn, tread = SpiralTread * (rOut / rMid) * 1.25f;
        for (int k = 1; k <= steps; k++)
        {
            angle = start + k * stepAngle;
            float top = y0 + k * rise;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            // Boîte tournée de 'angle' : son x (la profondeur de la marche) suit la tangente, son z (la largeur) le rayon.
            // V0.7.19 (Note 03) : 1 cm sous le niveau visé : la marche qui arrive à un palier (Blockhaus) ou au dernier étage (Tours,
            // Donjon) n'a plus son dessus dans le même plan que lui (scintillement des textures).
            kit.Turned(center + dir * rMid + Vector3.up * (top - 0.15f - SeamLift), new Vector3(tread, 0.3f, width), angle);
        }
        return angle;
    }

    // ---------- Tours rondes (Tours et Donjon du Château, Maisons-Fou) ----------

    // Mur circulaire de 'sides' pans (épaisseur 1 m) : percé d'une porte (angle 'doorAngle', s'il est >= 0... ou de deux) et de
    // meurtrières. Chaque pan est découpé autour de ses ouvertures (rectangles en coordonnées du pan : u le long, y en hauteur).
    static void KitRoundWall(Kit kit, Vector3 center, float radius, float y0, float y1, int sides, List<float> doorAngles, float doorWidth,
        float doorHeight, List<(int side, float y)> slits, float thick = 1f)
    {
        float chord = 2f * radius * Mathf.Sin(Mathf.PI / sides) + 0.08f;
        for (int k = 0; k < sides; k++)
        {
            float a = (k + 0.5f) * 360f / sides;
            var pieces = new List<Rect> { Rect.MinMaxRect(-chord * 0.5f, y0, chord * 0.5f, y1) };
            var holes = new List<Rect>();
            foreach (float door in doorAngles)
                if (Mathf.Abs(Mathf.DeltaAngle(a, door)) < 360f / sides * 0.5f + doorWidth / (2f * Mathf.PI * radius) * 180f)
                {
                    // Ouverture centrée sur l'angle de la porte, projetée sur ce pan.
                    float off = Mathf.Sin(Mathf.DeltaAngle(a, door) * Mathf.Deg2Rad) * radius;
                    holes.Add(Rect.MinMaxRect(off - doorWidth * 0.5f, y0 - 1f, off + doorWidth * 0.5f, y0 + doorHeight));
                }
            if (slits != null)
                foreach (var (side, y) in slits)
                    if (side == k) holes.Add(Rect.MinMaxRect(-RoundSlitHalf, y - 0.9f, RoundSlitHalf, y + 0.9f));
            pieces = Subtract(pieces, holes);
            Quaternion rot = Quaternion.Euler(0f, a, 0f);
            Vector3 outward = rot * Vector3.forward, along = rot * Vector3.right;
            foreach (Rect p in pieces)
            {
                Vector3 c = center + outward * (radius - thick * 0.5f) + along * p.center.x + Vector3.up * p.center.y;
                kit.Turned(c, new Vector3(p.width, p.height, thick), a);
            }
            // Fond noir des meurtrières (on y voit l'obscurité du dehors).
            if (slits != null)
                foreach (var (side, y) in slits)
                    if (side == k)
                        kit.dark.WorldQuad(center + outward * (radius + 0.02f) + along * RoundSlitHalf + Vector3.up * (y - 0.9f),
                            center + outward * (radius + 0.02f) - along * RoundSlitHalf + Vector3.up * (y - 0.9f),
                            center + outward * (radius + 0.02f) - along * RoundSlitHalf + Vector3.up * (y + 0.9f),
                            center + outward * (radius + 0.02f) + along * RoundSlitHalf + Vector3.up * (y + 0.9f), outward, 1f);
        }
    }

    // V0.7.19 (Note 05) : meurtrières des Tours et du Donjon 30 % plus larges (0,28 -> 0,364 m).
    const float RoundSlitHalf = 0.14f * 1.3f;

    // Disque horizontal (dalle de 'thick') de rayon 'radius' ; les pans dont l'angle est dans 'skip' (secteur [a0, a1]) sont laissés
    // ouverts (passage d'un escalier).
    static void KitDisc(Kit kit, Vector3 center, float radius, float top, float thick, int sides, float skipFrom = 0f, float skipTo = 0f, float innerHole = 0f)
    {
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * 360f / sides, a1 = (k + 1) * 360f / sides, am = (a0 + a1) * 0.5f;
            if (skipTo > skipFrom && InSector(am, skipFrom, skipTo)) continue;
            Vector3 p0 = center + Quaternion.Euler(0f, a0, 0f) * Vector3.forward * radius;
            Vector3 p1 = center + Quaternion.Euler(0f, a1, 0f) * Vector3.forward * radius;
            Vector3 q0 = center + Quaternion.Euler(0f, a0, 0f) * Vector3.forward * innerHole;
            Vector3 q1 = center + Quaternion.Euler(0f, a1, 0f) * Vector3.forward * innerHole;
            Vector3 up = Vector3.up * top, down = Vector3.up * (top - thick);
            kit.floors.WorldQuad(q0 + up, p0 + up, p1 + up, q1 + up, Vector3.up, 0.5f);
            kit.walls.WorldQuad(q0 + down, q1 + down, p1 + down, p0 + down, Vector3.down, 0.5f);
            kit.walls.WorldQuad(p0 + down, p1 + down, p1 + up, p0 + up, (p0 + p1 - 2f * center).normalized, 0.5f);
            // V0.7.19 (Note 02) : la tranche de la dalle aussi le long de l'ouverture (escalier) : elle n'a plus l'air d'une surface
            // plane vue depuis l'escalier.
            if (skipTo > skipFrom)
            {
                float step = 360f / sides;
                if (InSector(am - step, skipFrom, skipTo))
                    kit.walls.WorldQuad(q0 + down, q0 + up, p0 + up, p0 + down, Quaternion.Euler(0f, a0 - 90f, 0f) * Vector3.forward, 0.5f);
                if (InSector(am + step, skipFrom, skipTo))
                    kit.walls.WorldQuad(q1 + down, p1 + down, p1 + up, q1 + up, Quaternion.Euler(0f, a1 + 90f, 0f) * Vector3.forward, 0.5f);
            }
            // Collision : un bloc tourné couvrant le pan, du trou central au bord.
            float len = radius - innerHole, chord = 2f * radius * Mathf.Sin(Mathf.PI / sides) + 0.05f;
            Vector3 mid = center + Quaternion.Euler(0f, am, 0f) * Vector3.forward * (innerHole + len * 0.5f) + Vector3.up * (top - thick * 0.5f);
            var go = new GameObject("Dalle");
            go.transform.SetParent(kit.colliders, false);
            go.transform.SetPositionAndRotation(mid, Quaternion.Euler(0f, am, 0f));
            go.AddComponent<BoxCollider>().size = new Vector3(chord, thick, len);
        }
    }

    static bool InSector(float angle, float from, float to)
    {
        float span = Mathf.Repeat(to - from, 360f);
        return Mathf.Repeat(angle - from, 360f) <= span;
    }

    // Créneaux le long d'un cercle (sommet d'une tour).
    static void KitRoundMerlons(Kit kit, Vector3 center, float radius, float top, int sides, float skipFrom = 0f, float skipTo = 0f)
    {
        float chord = 2f * radius * Mathf.Sin(Mathf.PI / sides);
        for (int k = 0; k < sides; k += 2)
        {
            float a = (k + 0.5f) * 360f / sides;
            if (skipTo > skipFrom && InSector(a, skipFrom, skipTo)) continue;
            Vector3 c = center + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (radius - 0.5f) + Vector3.up * (top + 0.75f);
            kit.Turned(c, new Vector3(chord, 1.5f, 1f), a);
        }
    }

    // Créneaux le long d'un segment droit (remparts, toits des salles), merlons de 1 m tous les 2 m.
    static void KitMerlons(Kit kit, Vector3 a, Vector3 b, float top, float inward, Vector3 inwardDir, List<(float from, float to)> gaps = null, MeshBuilder mb = null, bool collide = true)
    {
        Vector3 d = b - a;
        float length = d.magnitude;
        if (length < 0.5f) return;
        Vector3 dir = d / length;
        bool alongX = Mathf.Abs(dir.x) > 0.5f;
        for (float t = 0.5f; t + 0.5f <= length; t += 2f)
        {
            if (gaps != null && gaps.Exists(g => t >= g.from - 0.6f && t <= g.to + 0.6f)) continue;
            Vector3 c = a + dir * t + inwardDir * inward + Vector3.up * (top + 0.6f);
            var size = alongX ? new Vector3(1f, 1.2f, 0.6f) : new Vector3(0.6f, 1.2f, 1f);
            if (mb != null) { mb.Box(c, size, 0.5f); if (collide) kit.Collider(c, size); }
            else kit.Box(c, size, collide);
        }
    }

    // ---------- Câbles (leviers) ----------

    // Ruban plat le long de 'points' (déjà posés sur les sols et les murs), noir et peu visible au départ.
    static Renderer KitRibbon(Transform parent, List<Vector3> points, float width)
    {
        if (points.Count < 2) return null;
        var vertices = new Vector3[points.Count * 2];
        var triangles = new int[(points.Count - 1) * 6];
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 d = points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)];
            Vector3 side = Vector3.Cross(Mathf.Abs(d.normalized.y) > 0.9f ? Vector3.forward : Vector3.up, d).normalized * width * 0.5f;
            vertices[i * 2] = points[i] - side;
            vertices[i * 2 + 1] = points[i] + side;
        }
        for (int i = 0; i < points.Count - 1; i++)
        {
            int t = i * 6, v = i * 2;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        var mesh = new Mesh { name = "CableLevier", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = GameArt.MeshObject("Cable", parent, mesh, Glyph.CableMaterial(false));
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    // ---------- Néons muraux (V0.7.12 : remparts du Château, fenêtres des Blockhaus) ----------

    // Néon accroché à un mur, face à 'outward' ; il s'éteint pendant le Couvre Feu et clignote près d'un Roi, comme les autres.
    // 'scale' : facteur sur les trois dimensions du tube.
    static void AddWallNeon(Transform parent, Vector3 pos, Vector3 outward, float range, float intensity, System.Random rnd, float scale = 1f)
    {
        intensity *= LampDim;
        range *= LampReach;
        var go = new GameObject("NeonMural");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(outward));
        var bulb = GameArt.Prim(PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(1.1f, 0.07f, 0.07f) * scale, GameArt.Unlit("Neon", new Color(0.7f, 0.7f, 0.66f)));
        foreach (var c in bulb.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        var l = AddLight(go.transform, Vector3.forward * 0.45f, new Color(0.85f, 0.87f, 0.9f), intensity, range);
        go.AddComponent<FlickerLight>().Init(l, bulb.GetComponent<Renderer>(), rnd.NextDouble() < 0.2);
    }

    // ---------- Lustres (Château) ----------

    // Lustre de fer (modèle Meshy "Lustre", sinon un anneau de bougies) suspendu au plafond, avec sa lumière chaude ; il s'éteint
    // pendant le Couvre Feu, comme les néons.
    static void AddChandelier(Transform parent, Vector3 ceilingPos, float range, float intensity, System.Random rnd)
    {
        intensity *= LampDim;
        range *= LampReach;
        var go = new GameObject("Lustre");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = ceilingPos + Vector3.down * 1.3f;
        Renderer glow;
        var model = GameArt.SpawnModel("Lustre", go.transform, 1.1f);
        var flame = GameArt.Unlit("FlammeBougie", new Color(1f, 0.82f, 0.5f));
        if (model == null)
        {
            Material iron = GameArt.Lit("LustreFer", new Color(0.12f, 0.12f, 0.12f), null, 0.4f);
            var ring = GameArt.MeshObject("Anneau", go.transform, ItemPickup.Torus(0.55f, 0.04f, 24, 6), iron);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            model.transform.localPosition = Vector3.down * 0.4f;
            // V0.7.13 (Note 20) : un lustre au pourtour bien circulaire (même largeur en X et en Z).
            Transform mesh = model.transform.GetChild(0);
            Bounds lb = GameArt.LocalBounds(model.transform);
            float round = Mathf.Max(lb.size.x, lb.size.z);
            if (lb.size.x > 0.01f && lb.size.z > 0.01f)
            {
                Vector3 k = new Vector3(round / lb.size.x, 1f, round / lb.size.z);
                // Échelle appliquée dans le repère du modèle (rotation d'import comprise).
                Vector3 local = Quaternion.Inverse(mesh.localRotation) * k;
                mesh.localScale = Vector3.Scale(mesh.localScale, new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)));
                Bounds nb = GameArt.LocalBounds(model.transform);
                mesh.localPosition -= new Vector3(nb.center.x, 0f, nb.center.z);
            }
        }
        GameArt.Prim(PrimitiveType.Cylinder, go.transform, Vector3.up * 0.65f, new Vector3(0.02f, 0.65f, 0.02f), GameArt.Metal);
        var bulb = GameArt.Prim(PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * 0.16f, flame);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        glow = bulb.GetComponent<Renderer>();
        var l = AddLight(go.transform, Vector3.down * 0.3f, new Color(1f, 0.78f, 0.55f), intensity, range);
        go.AddComponent<FlickerLight>().Init(l, glow, rnd.NextDouble() < 0.3);
    }

    // ---------- Torches (V0.7.13, Château) ----------

    // Torche noire (modèle Meshy "Torche", sinon un manche de fer) à flamme blanche lumineuse : accrochée à un mur ('onWall' :
    // 'pos' sur la face du mur, 'facing' vers la pièce) ou posée au sol ('pos' au sol). Elle s'éteint pendant le Couvre Feu.
    static void AddTorch(Transform parent, Vector3 pos, Vector3 facing, bool onWall, System.Random rnd)
    {
        var go = new GameObject("Torche");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing));
        float height = onWall ? WallTorchHeight : FloorTorchHeight;
        var model = GameArt.SpawnModel("Torche", go.transform, height);
        float top;
        if (model != null)
        {
            Bounds b = GameArt.LocalBounds(model.transform);
            // Au mur : centrée sur le point donné, le dos contre le mur ; au sol : posée.
            model.transform.localPosition = onWall ? new Vector3(0f, -height * 0.5f, -b.min.z) : Vector3.zero;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            top = onWall ? height * 0.5f : height;
            if (!onWall)
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.center = Vector3.up * (height * 0.5f);
                col.height = height;
                col.radius = 0.12f;
            }
        }
        else
        {
            Material iron = GameArt.Lit("TorcheFer", new Color(0.06f, 0.06f, 0.06f), null, 0.3f);
            float y0 = onWall ? -height * 0.5f : 0f;
            var stick = GameArt.Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0f, y0 + height * 0.5f, onWall ? 0.18f : 0f), new Vector3(0.07f, height * 0.5f, 0.07f), iron);
            if (onWall) foreach (var c in stick.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            top = y0 + height;
        }
        var flame = GameArt.Prim(PrimitiveType.Sphere, go.transform, new Vector3(0f, top + 0.12f, onWall ? 0.18f : 0f), new Vector3(0.16f, 0.26f, 0.16f), GameArt.Unlit("FlammeBlanche", new Color(1f, 1f, 0.97f)));
        foreach (var c in flame.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        var l = AddLight(go.transform, new Vector3(0f, top + 0.3f, onWall ? 0.5f : 0f), new Color(0.95f, 0.96f, 1f), TorchIntensity * LampDim, TorchRange * LampReach);
        go.AddComponent<FlickerLight>().Init(l, flame.GetComponent<Renderer>(), rnd.NextDouble() < 0.25);
    }

    const float WallTorchHeight = 0.9f, FloorTorchHeight = 1.7f, TorchIntensity = 1.5f, TorchRange = 10f;
}
