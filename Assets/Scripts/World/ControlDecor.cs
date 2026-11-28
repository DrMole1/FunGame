using System.Collections.Generic;
using UnityEngine;

// V0.7.7 (chapitre 17, Note 09) : décor industriel des murs de la Control Room. Tuyauteries (en métal légèrement rouillé, sur
// des axes et des conduits différents : longues conduites horizontales, descentes verticales, coudes, brides et colliers),
// gaine d'aération, et les modèles Meshy de Resources/Art/Decor : bloc de ventilation mural, moteur-pompe et vannes.
// V0.7.7 : dix fois plus de tuyaux, tirés au hasard (graine fixe : les mêmes chez tous) sur les quatre murs, partant dans
// divers sens (du sol, du plafond, sortant du mur ; à l'horizontale, à la verticale, en diagonale), sans toucher aux portes,
// néons, écran, enseigne, casiers, à la Tête-De-Cloche ni aux modèles. Tous les tuyaux sont fusionnés en deux maillages (un
// par teinte de rouille) : quelques appels de rendu seulement, même en WebGL.
// V0.7.7 : tuyaux, gaine, blocs de ventilation, moteur et vannes ont des collisions (on ne les traverse plus).
public static class ControlDecor
{
    static Material rust, rustDark, duct;
    static Transform holder;
    static PipeMesh pipesLight, pipesDark;

    const int RandomRuns = 100;
    static readonly float[] Radii = { 0.03f, 0.04f, 0.05f, 0.06f, 0.075f, 0.09f, 0.11f, 0.13f };
    // V0.7.11 (Note 05) : en plus des mêmes tuyaux, des tuyaux un peu plus gros et quelques-uns beaucoup plus épais.
    static readonly float[] BigRadii = { 0.16f, 0.19f, 0.22f }, HugeRadii = { 0.3f, 0.36f };
    const float BigPipeChance = 0.14f, HugePipeChance = 0.06f;

    static float PickRadius(System.Random rnd)
    {
        double roll = rnd.NextDouble();
        if (roll < HugePipeChance) return HugeRadii[rnd.Next(HugeRadii.Length)];
        if (roll < HugePipeChance + BigPipeChance) return BigRadii[rnd.Next(BigRadii.Length)];
        return Radii[rnd.Next(Radii.Length)];
    }

    // V0.7.8 : la salle passe à 12 x 18 m. Le long du mur ouest, du nord au sud : moteur-pompe, casiers (sous un bloc de
    // ventilation), Tête-De-Cloche (en face du Vestiaire, à 8 m du mur nord), vannes (sous le second bloc de ventilation).
    public static float WestLockersZ(float n) => n - 5f;
    const float MotorFromNorth = 2.4f, WestValvesFromSouth = 1.8f, SouthVentFromSouth = 2.1f;

    // Repère de la Control Room : murs ouest (-hw) et est (+hw), nord (n, côté tunnel) et sud (s, l'écran), hauteur h.
    public static void Build(Transform room, float hw, float n, float s, float h, float vestiaireZ, float tunnelRadius, System.Action<Vector3, Vector3> box)
    {
        holder = new GameObject("DecorIndustriel").transform;
        holder.SetParent(room, false);
        rust = GameArt.Lit("TuyauRouille", new Color(0.42f, 0.36f, 0.31f), GameArt.Noise("rust", 64, 0.55f, 0.35f, 23), 0.45f);
        rust.SetFloat("_Metallic", 0.55f);
        rustDark = GameArt.Lit("TuyauRouilleSombre", new Color(0.27f, 0.22f, 0.19f), GameArt.Noise("rust", 64, 0.5f, 0.4f, 7), 0.35f);
        rustDark.SetFloat("_Metallic", 0.5f);
        duct = GameArt.Lit("GaineAeration", new Color(0.46f, 0.47f, 0.47f), GameArt.Noise("rust", 64, 0.7f, 0.25f, 31), 0.5f);
        duct.SetFloat("_Metallic", 0.6f);
        pipesLight = new PipeMesh();
        pipesDark = new PipeMesh();

        // ----- Mur ouest -----
        // Conduite principale tout le long, à 4,7 m ; une seconde, plus fine, juste dessous, qui plonge vers le sol au nord.
        float wx = -hw + 0.22f;
        Run(0.13f, false, new Vector3(wx, 4.7f, n - 0.2f), new Vector3(wx, 4.7f, s + 0.2f));
        Run(0.07f, true, new Vector3(wx + 0.05f, 4.25f, s + 0.2f), new Vector3(wx + 0.05f, 4.25f, n - MotorFromNorth), new Vector3(wx + 0.05f, 0.9f, n - MotorFromNorth));
        // Gaine d'aération rectangulaire sous le plafond.
        DuctRun(new Vector3(-hw + 0.45f, h - 0.4f, n - 0.1f), new Vector3(-hw + 0.45f, h - 0.4f, s + 0.1f), new Vector2(0.6f, 0.38f));

        // ----- Mur nord (de part et d'autre du tunnel) -----
        float nz = n - 0.2f;
        Run(0.1f, false, new Vector3(-hw + 0.2f, 5.25f, nz), new Vector3(hw - 0.2f, 5.25f, nz));
        // Deux descentes qui contournent la bouche du tunnel.
        float side = tunnelRadius + 1.4f;
        Run(0.08f, true, new Vector3(-side, 5.25f, nz), new Vector3(-side, 0f, nz));
        Run(0.06f, false, new Vector3(side, 5.25f, nz - 0.05f), new Vector3(side, 1.8f, nz - 0.05f), new Vector3(side + 2.5f, 1.8f, nz - 0.05f), new Vector3(side + 2.5f, 0f, nz - 0.05f));

        // ----- Mur est (autour de la porte du Vestiaire) -----
        float ex = hw - 0.2f;
        Run(0.09f, false, new Vector3(ex, h, n - 1.5f), new Vector3(ex, 4.4f, n - 1.5f), new Vector3(ex, 4.4f, vestiaireZ + 2.2f), new Vector3(ex, 0f, vestiaireZ + 2.2f));
        Run(0.06f, true, new Vector3(ex - 0.05f, h, vestiaireZ - 2f), new Vector3(ex - 0.05f, 0f, vestiaireZ - 2f));
        Run(0.06f, true, new Vector3(ex - 0.05f, h, vestiaireZ - 2.35f), new Vector3(ex - 0.05f, 3.6f, vestiaireZ - 2.35f), new Vector3(ex - 0.05f, 3.6f, s + 0.6f));

        // ----- Mur sud : une conduite au ras du plafond, au-dessus de l'enseigne -----
        Run(0.11f, false, new Vector3(-hw + 0.2f, 5.6f, s + 0.2f), new Vector3(hw - 0.2f, 5.6f, s + 0.2f));

        // ----- Tuyaux tirés au hasard -----
        RandomPipes(hw, n, s, h, vestiaireZ, tunnelRadius);
        Flush(pipesLight, "Tuyaux", rust);
        Flush(pipesDark, "TuyauxSombres", rustDark);

        // ----- Modèles Meshy -----
        // Blocs de ventilation accrochés en haut des murs ouest et nord, sous la gaine ; moteur-pompe au sol au nord-ouest (la
        // descente du mur ouest y plonge) ; vannes au sol, au nord-est et au sud-ouest.
        WallModel("Decor/Ventilation", new Vector3(-hw, 3.1f, WestLockersZ(n)), Vector3.right, 1.2f);
        WallModel("Decor/Ventilation", new Vector3(-hw, 3.1f, s + SouthVentFromSouth), Vector3.right, 1.2f);
        WallModel("Decor/Ventilation", new Vector3(hw - 4.5f, 3.9f, n), Vector3.back, 1.1f);
        FloorModel("Decor/Moteur", new Vector3(-hw + 0.7f, 0f, n - MotorFromNorth), 90f, 1.1f, box);
        FloorModel("Decor/Vannes", new Vector3(hw - 0.45f, 0f, vestiaireZ + 3.6f), -90f, 1.6f, box);
        FloorModel("Decor/Vannes", new Vector3(-hw + 0.42f, 0f, s + WestValvesFromSouth), 90f, 1.4f, box);
    }

    // V0.7.13 : les mêmes tuyaux, posés ailleurs (couloirs des Blockhaus) : BeginPipes, des Pipe (points en coordonnées de 'parent'),
    // puis EndPipes, qui fusionne le tout (avec sa collision).
    public static void BeginPipes(Transform parent)
    {
        if (rust == null)
        {
            rust = GameArt.Lit("TuyauRouille", new Color(0.42f, 0.36f, 0.31f), GameArt.Noise("rust", 64, 0.55f, 0.35f, 23), 0.45f);
            rust.SetFloat("_Metallic", 0.55f);
            rustDark = GameArt.Lit("TuyauRouilleSombre", new Color(0.27f, 0.22f, 0.19f), GameArt.Noise("rust", 64, 0.5f, 0.4f, 7), 0.35f);
            rustDark.SetFloat("_Metallic", 0.5f);
        }
        holder = parent;
        pipesLight = new PipeMesh();
        pipesDark = new PipeMesh();
    }

    public static void Pipe(float radius, bool dark, params Vector3[] points) => Run(radius, dark, points);

    public static float RandomPipeRadius(System.Random rnd) => PickRadius(rnd);

    public static void EndPipes()
    {
        Flush(pipesLight, "Tuyaux", rust);
        Flush(pipesDark, "TuyauxSombres", rustDark);
    }

    static void Flush(PipeMesh pipes, string name, Material mat)
    {
        if (pipes.Empty) return;
        var mesh = pipes.Build(name);
        var go = GameArt.MeshObject(name, holder, mesh, mat);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Collision exacte des tuyaux (maillage statique, fusionné).
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    // ---------- Tuyaux au hasard ----------

    // Un mur, vu de la pièce : u le long du mur (de u0 à u1), y vers le haut, 'inward' vers la pièce. Zones interdites en (u, y).
    class Wall
    {
        public Vector3 origin, along, inward;
        public float u0, u1;
        public readonly List<Rect> blocked = new List<Rect>();
        public Vector3 At(float u, float y, float depth) => origin + along * u + Vector3.up * y + inward * depth;
        public void Block(float ua, float ub, float ya, float yb) => blocked.Add(Rect.MinMaxRect(Mathf.Min(ua, ub), ya, Mathf.Max(ua, ub), yb));
        public bool Free(float u, float y, float margin)
        {
            if (u < u0 + 0.25f || u > u1 - 0.25f) return false;
            foreach (var r in blocked)
                if (u > r.xMin - margin && u < r.xMax + margin && y > r.yMin - margin && y < r.yMax + margin) return false;
            return true;
        }
    }

    static void RandomPipes(float hw, float n, float s, float h, float vz, float tunnelRadius)
    {
        var west = new Wall { origin = new Vector3(-hw, 0f, 0f), along = Vector3.forward, inward = Vector3.right, u0 = s, u1 = n };
        var east = new Wall { origin = new Vector3(hw, 0f, 0f), along = Vector3.forward, inward = Vector3.left, u0 = s, u1 = n };
        var north = new Wall { origin = new Vector3(0f, 0f, n), along = Vector3.right, inward = Vector3.back, u0 = -hw, u1 = hw };
        var south = new Wall { origin = new Vector3(0f, 0f, s), along = Vector3.right, inward = Vector3.forward, u0 = -hw, u1 = hw };

        // Ouest : Tête-De-Cloche assise, casiers, blocs de ventilation, moteur, vannes, gaine sous le plafond.
        west.Block(vz - 0.9f, vz + 0.9f, -1f, 1.7f);
        float lz = WestLockersZ(n);
        west.Block(lz - Lockers.Width * 0.5f - 0.2f, lz + Lockers.Width * 0.5f + 0.2f, -1f, 2.2f);
        foreach (float z in new[] { lz, s + SouthVentFromSouth }) west.Block(z - 0.95f, z + 0.95f, 2.3f, 3.9f);
        west.Block(n - MotorFromNorth - 1f, n - MotorFromNorth + 1f, -1f, 1.3f);
        west.Block(s + WestValvesFromSouth - 1f, s + WestValvesFromSouth + 1f, -1f, 1.6f);
        west.Block(s - 1f, n + 1f, h - 0.7f, h + 1f);
        // Est : porte du Vestiaire et son néon, vannes.
        east.Block(vz - 0.9f, vz + 0.9f, -1f, 2.45f);
        east.Block(vz - 0.85f, vz + 0.85f, 3f, 3.75f);
        east.Block(vz + 2.6f, vz + 4.6f, -1f, 1.8f);
        // Nord : bouche du tunnel et son néon, bloc de ventilation.
        north.Block(-tunnelRadius - 0.4f, tunnelRadius + 0.4f, -1f, 2.9f);
        north.Block(-0.9f, 0.9f, 2.9f, 3.8f);
        north.Block(hw - 5.4f, hw - 3.6f, 3.2f, 4.6f);
        // Sud : grand écran, enseigne, câble du promontoire au ras du sol.
        south.Block(-2.9f, 2.9f, 0.3f, 3.55f);
        south.Block(-3.9f, 3.9f, 3.55f, 5.4f);
        south.Block(-hw - 1f, hw + 1f, -1f, 0.35f);

        var walls = new[] { west, east, north, south };
        var rnd = new System.Random(7707);
        float total = 0f;
        foreach (var w in walls) total += w.u1 - w.u0;

        for (int run = 0; run < RandomRuns; run++)
        {
            // Mur au prorata de sa longueur.
            float pick = (float)rnd.NextDouble() * total;
            Wall wall = walls[0];
            foreach (var w in walls) { if (pick < w.u1 - w.u0) { wall = w; break; } pick -= w.u1 - w.u0; }
            float radius = PickRadius(rnd);
            bool dark = rnd.NextDouble() < 0.45;
            // Cinq épaisseurs de décollement : des tuyaux qui se croisent passent souvent l'un devant l'autre.
            float depth = 0.07f + radius + rnd.Next(5) * 0.13f;
            float margin = radius + 0.06f;

            // Départ : du sol, du plafond ou du mur.
            int startKind = rnd.Next(3);
            float u = 0f, y = 0f;
            bool found = false;
            for (int tries = 0; tries < 20 && !found; tries++)
            {
                u = Mathf.Lerp(wall.u0 + 0.4f, wall.u1 - 0.4f, (float)rnd.NextDouble());
                y = startKind == 0 ? 0f : startKind == 1 ? h : Mathf.Lerp(0.5f, h - 0.5f, (float)rnd.NextDouble());
                float probe = startKind == 0 ? 0.4f : startKind == 1 ? h - 0.4f : y;
                found = wall.Free(u, probe, margin);
            }
            if (!found) continue;

            var pts = new List<Vector3>();
            if (startKind == 2) pts.Add(wall.At(u, y, 0f));
            pts.Add(wall.At(u, y, depth));
            Vector2 dir = startKind == 0 ? Vector2.up : startKind == 1 ? Vector2.down
                : rnd.Next(2) == 0 ? new Vector2(rnd.Next(2) == 0 ? -1f : 1f, 0f) : new Vector2(0f, rnd.Next(2) == 0 ? -1f : 1f);
            int segments = 1 + rnd.Next(4);
            Vector2 cur = new Vector2(u, y);
            int made = 0;
            for (int k = 0; k < segments; k++)
            {
                float len = Mathf.Lerp(0.7f, 5.5f, (float)rnd.NextDouble());
                const float step = 0.08f;
                float done = 0f;
                Vector2 d = dir.normalized;
                while (done + step <= len)
                {
                    Vector2 p = cur + d * (done + step);
                    // Hors du sol et du plafond, on ne teste qu'à partir de 0,15 m de chaque bord.
                    bool edge = p.y < 0.15f || p.y > h - 0.15f;
                    if (edge && (k > 0 || startKind == 2)) break;
                    if (!edge && !wall.Free(p.x, p.y, margin)) break;
                    if (p.x < wall.u0 + 0.25f || p.x > wall.u1 - 0.25f) break;
                    done += step;
                }
                if (done < 0.35f) break;
                cur += d * done;
                pts.Add(wall.At(cur.x, cur.y, depth));
                made++;
                // Virage : d'horizontal à vertical et inversement ; de temps en temps, une diagonale.
                bool vertical = Mathf.Abs(dir.x) < 0.01f;
                bool horizontal = Mathf.Abs(dir.y) < 0.01f;
                if (rnd.NextDouble() < 0.15) dir = new Vector2(rnd.Next(2) == 0 ? -1f : 1f, rnd.Next(2) == 0 ? -1f : 1f);
                else if (vertical || (!horizontal && rnd.Next(2) == 0)) dir = new Vector2(rnd.Next(2) == 0 ? -1f : 1f, 0f);
                else dir = new Vector2(0f, rnd.Next(2) == 0 ? -1f : 1f);
            }
            if (made == 0) continue;

            // Arrivée : dans le sol ou le plafond si la voie est libre, sinon dans le mur, sinon une bride d'obturation.
            int endKind = rnd.Next(3);
            bool capped = false;
            if (endKind == 0 && Clear(wall, cur, 0f, margin, h)) pts.Add(wall.At(cur.x, 0f, depth));
            else if (endKind == 1 && Clear(wall, cur, h, margin, h)) pts.Add(wall.At(cur.x, h, depth));
            else if (rnd.NextDouble() < 0.7) pts.Add(wall.At(cur.x, cur.y, 0f));
            else capped = true;
            Run(radius, dark, pts.ToArray());
            if (capped)
            {
                Vector3 last = pts[pts.Count - 1], dirEnd = (last - pts[pts.Count - 2]).normalized;
                pipesDark.Cylinder(last - dirEnd * 0.02f, last + dirEnd * 0.03f, radius * 1.45f, true);
            }
        }
    }

    // Voie libre à la verticale, de 'from' jusqu'à la hauteur 'toY'.
    static bool Clear(Wall wall, Vector2 from, float toY, float margin, float h)
    {
        float a = Mathf.Max(Mathf.Min(from.y, toY), 0.2f), b = Mathf.Min(Mathf.Max(from.y, toY), h - 0.2f);
        for (float y = a; y <= b; y += 0.1f)
            if (!wall.Free(from.x, y, margin)) return false;
        return true;
    }

    // Conduite cylindrique passant par les points donnés : coudes aux angles, brides régulières (teinte opposée).
    static void Run(float radius, bool dark, params Vector3[] points)
    {
        var mesh = dark ? pipesDark : pipesLight;
        var other = dark ? pipesLight : pipesDark;
        for (int i = 0; i + 1 < points.Length; i++)
        {
            Vector3 a = points[i], b = points[i + 1], d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) continue;
            mesh.Cylinder(a, b, radius, false);
            int flanges = Mathf.FloorToInt(len / 2.5f);
            for (int k = 1; k <= flanges; k++)
            {
                Vector3 c = a + d * (k / (float)(flanges + 1));
                other.Cylinder(c - d / len * 0.015f, c + d / len * 0.015f, radius * 1.35f, true);
            }
            if (i + 1 < points.Length - 1) mesh.Sphere(b, radius * 1.15f);
        }
    }

    // Gaine rectangulaire (section 'size' : largeur, hauteur), avec des joints de tôle tous les 1,5 m.
    static void DuctRun(Vector3 a, Vector3 b, Vector2 size)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        var g = Deco(PrimitiveType.Cube, (a + b) * 0.5f, new Vector3(size.x, size.y, len), duct);
        g.name = "Gaine";
        g.AddComponent<BoxCollider>();
        g.transform.localRotation = Quaternion.LookRotation(d / len);
        for (float t = 0.75f; t < len; t += 1.5f)
        {
            var j = Deco(PrimitiveType.Cube, a + d * (t / len), new Vector3(size.x + 0.04f, size.y + 0.04f, 0.05f), rustDark);
            j.name = "Joint";
            j.transform.localRotation = g.transform.localRotation;
        }
    }

    static GameObject Deco(PrimitiveType type, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameArt.Prim(type, holder, pos, size, mat);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    // Maillage de tuyaux : cylindres à 10 pans (bouchés ou non) et sphères basses pour les coudes.
    class PipeMesh
    {
        const int Sides = 10;
        readonly List<Vector3> v = new List<Vector3>(), nrm = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();
        public bool Empty => v.Count == 0;

        // Triangle tourné vers 'outward'.
        void Tri(int a, int b, int c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) < 0f) { int x = b; b = c; c = x; }
            t.Add(a); t.Add(b); t.Add(c);
        }

        public void Cylinder(Vector3 a, Vector3 b, float r, bool caps)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 dir = d / len;
            Vector3 x = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 y = Vector3.Cross(dir, x);
            int s0 = v.Count;
            for (int i = 0; i <= Sides; i++)
            {
                float ang = i * Mathf.PI * 2f / Sides;
                Vector3 o = x * Mathf.Cos(ang) + y * Mathf.Sin(ang);
                v.Add(a + o * r); nrm.Add(o); uv.Add(new Vector2(i / (float)Sides, 0f));
                v.Add(b + o * r); nrm.Add(o); uv.Add(new Vector2(i / (float)Sides, len * 2f));
            }
            for (int i = 0; i < Sides; i++)
            {
                int k = s0 + i * 2;
                Vector3 o = (nrm[k] + nrm[k + 2]).normalized;
                Tri(k, k + 1, k + 3, o);
                Tri(k, k + 3, k + 2, o);
            }
            if (!caps) return;
            foreach (bool end in new[] { false, true })
            {
                Vector3 c = end ? b : a, outward = end ? dir : -dir;
                int center = v.Count;
                v.Add(c); nrm.Add(outward); uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= Sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / Sides;
                    Vector3 o = x * Mathf.Cos(ang) + y * Mathf.Sin(ang);
                    v.Add(c + o * r); nrm.Add(outward); uv.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
                }
                for (int i = 0; i < Sides; i++) Tri(center, center + 1 + i, center + 2 + i, outward);
            }
        }

        public void Sphere(Vector3 c, float r)
        {
            const int lat = 5, lon = 10;
            int s0 = v.Count;
            for (int i = 0; i <= lat; i++)
            {
                float th = i * Mathf.PI / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float ph = j * Mathf.PI * 2f / lon;
                    Vector3 o = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(c + o * r); nrm.Add(o); uv.Add(new Vector2(j / (float)lon, i / (float)lat));
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = s0 + i * (lon + 1) + j, b = a + lon + 1;
                    Vector3 o = (nrm[a] + nrm[b] + nrm[a + 1] + nrm[b + 1]).normalized;
                    if (i > 0) Tri(a, b, a + 1, o);
                    if (i < lat - 1) Tri(a + 1, b, b + 1, o);
                }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(nrm);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // Modèle plaqué au mur : son dos (le côté le plus plat, supposé -Z) contre le mur, tourné vers 'inward'.
    static void WallModel(string name, Vector3 wallPoint, Vector3 inward, float height)
    {
        var pivot = new GameObject(name.Substring(name.LastIndexOf('/') + 1)).transform;
        pivot.SetParent(holder, false);
        pivot.localPosition = wallPoint;
        pivot.localRotation = Quaternion.LookRotation(inward);
        var model = GameArt.SpawnModel(name, pivot, height);
        if (model == null) { Object.Destroy(pivot.gameObject); return; }
        Bounds b = GameArt.LocalBounds(model.transform);
        // Centré verticalement sur le point donné, le dos contre le mur.
        model.transform.localPosition = new Vector3(0f, -height * 0.5f, -b.min.z);
        foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Collision : la boîte du bloc, contre le mur.
        Bounds wb = GameArt.LocalBounds(pivot);
        var col = pivot.gameObject.AddComponent<BoxCollider>();
        col.center = wb.center;
        col.size = wb.size;
    }

    static void FloorModel(string name, Vector3 at, float yaw, float height, System.Action<Vector3, Vector3> box)
    {
        var pivot = new GameObject(name.Substring(name.LastIndexOf('/') + 1)).transform;
        pivot.SetParent(holder, false);
        pivot.localPosition = at;
        pivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var model = GameArt.SpawnModel(name, pivot, height);
        if (model == null) { Object.Destroy(pivot.gameObject); return; }
        Bounds b = GameArt.LocalBounds(model.transform);
        Vector3 size = Quaternion.Euler(0f, yaw, 0f) * b.size;
        box(at + Vector3.up * height * 0.5f, new Vector3(Mathf.Abs(size.x), height, Mathf.Abs(size.z)));
    }
}

// V0.7.7 (Note 07) : sur le mur de la Control Room opposé au Vestiaire, une Tête-De-Cloche inanimée est assise par terre, le
// dos contre le mur, immobile. Sans arme, sans comportement : un simple décor (avec une collision).
public static class SeatedTete
{
    public static void Build(Transform room, Vector3 wallFoot, Vector3 inward, System.Action<Vector3, Vector3> box)
    {
        var pivot = new GameObject("TeteDeClocheAssise").transform;
        pivot.SetParent(room, false);
        pivot.localPosition = wallFoot;
        pivot.localRotation = Quaternion.LookRotation(inward);
        var art = Resources.Load<CreatureArt>("TeteDeClocheArt");
        if (art == null || art.model == null) { Object.Destroy(pivot.gameObject); return; }
        var model = Object.Instantiate(art.model, pivot);
        model.name = "Modele";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * art.scale;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        var animator = model.GetComponent<Animator>();
        if (animator != null && animator.isHuman)
        {
            Pose(animator, pivot);
            animator.enabled = false;
        }
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (r is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;
        }
        // Dans la pièce : une boîte qui couvre le buste et les jambes allongées.
        box(wallFoot + inward * 0.75f + Vector3.up * 0.6f, Abs(Quaternion.LookRotation(inward) * new Vector3(0.9f, 1.2f, 1.5f)));
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // Pose assise à partir de la pose en T : bassin au sol, jambes allongées devant (genoux un peu relevés), dos légèrement
    // renversé contre le mur, bras tombant le long du corps, mains au sol, cloche penchée sur le côté.
    static void Pose(Animator a, Transform pivot)
    {
        Vector3 right = pivot.right, up = pivot.up, fwd = pivot.forward;
        void Turn(HumanBodyBones bone, Vector3 axis, float angle)
        {
            var t = a.GetBoneTransform(bone);
            if (t != null) t.rotation = Quaternion.AngleAxis(angle, axis) * t.rotation;
        }
        Turn(HumanBodyBones.Spine, right, -12f);
        Turn(HumanBodyBones.Chest, right, 6f);
        Turn(HumanBodyBones.Neck, right, 18f);
        Turn(HumanBodyBones.Head, fwd, 16f);
        Turn(HumanBodyBones.Head, right, 14f);
        foreach (int s in new[] { -1, 1 })
        {
            bool left = s < 0;
            Turn(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, right, -78f);
            Turn(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, up, s * 9f);
            Turn(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, right, -24f);
            Turn(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, right, -30f);
            // Bras le long du corps, un peu écartés, avant-bras posés vers le sol.
            Turn(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, fwd, -s * 72f);
            Turn(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, up, -s * 12f);
            Turn(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, right, -28f);
        }
        // Bassin posé au sol, le dos contre le mur.
        var hips = a.GetBoneTransform(HumanBodyBones.Hips);
        var spine = a.GetBoneTransform(HumanBodyBones.Chest) ?? a.GetBoneTransform(HumanBodyBones.Spine);
        Vector3 local = pivot.InverseTransformPoint(hips.position);
        Vector3 back = pivot.InverseTransformPoint(spine.position);
        a.transform.localPosition -= new Vector3(local.x, local.y - 0.2f, Mathf.Min(local.z, back.z) - 0.3f);
    }
}

// V0.7.7 (Note 08) : casiers en métal usés par le temps, dans le Vestiaire. Trois compartiments dont les portes (objets
// interactifs, sans texte au survol) s'ouvrent et se ferment, chez tous (état tenu par l'hôte, GameSession.LockerOpen).
// Bâti : maillage Resources/Art/Decor/CasiersBati (le modèle Meshy "Casiers" dont on a retiré les trois portes ouvertes,
// 2,26 x 1,8 x 0,7 m, face avant à z = 0,06) et son matériau Casiers ; portes : Resources/Art/Decor/PorteCasier s'il existe,
// sinon en tôle (primitives). Sans le maillage : bâti en primitives aux mêmes dimensions.
public static class Lockers
{
    public const int Count = 3;
    // V0.7.7 : deux jeux de casiers (Vestiaire, puis Control Room), soit 6 portes numérotées à la suite.
    public const int Sets = 2, TotalDoors = Count * Sets;
    const float Scale = 1.9f / 1.8f, SourceFront = 0.06f;
    public const float Width = 2.26f * Scale, Height = 1.8f * Scale, Depth = 0.7f * Scale;
    // Ouverture des compartiments (hauteur) et épaisseur des montants, mesurées sur le modèle.
    const float OpeningBottom = 0.22f * Scale, OpeningTop = 1.6f * Scale, Post = 0.05f * Scale;

    // V0.7.24 (17.4, S3) : fond de chaque compartiment (monde), là où la Cassette est posée en début de partie, et son cap (le
    // long du casier).
    static readonly Vector3[] compartments = new Vector3[TotalDoors];
    static readonly float[] compartmentYaw = new float[TotalDoors];
    static readonly bool[] compartmentBuilt = new bool[TotalDoors];
    public static bool CompartmentPoint(int door, out Vector3 point, out float yaw)
    {
        bool ok = door >= 0 && door < TotalDoors && compartmentBuilt[door];
        point = ok ? compartments[door] : Vector3.zero;
        yaw = ok ? compartmentYaw[door] : 0f;
        return ok;
    }

    // 'front' : milieu du bas de la face avant ; les portes regardent vers 'facing'.
    // 'set' : 0 (Vestiaire) ou 1 (Control Room) ; ses portes portent les numéros set * 3 à set * 3 + 2.
    public static void Build(Transform parent, Vector3 front, Vector3 facing, System.Action<Vector3, Vector3> box, int set = 0)
    {
        var holder = new GameObject("Casiers").transform;
        holder.SetParent(parent, false);
        holder.localPosition = front;
        holder.localRotation = Quaternion.LookRotation(facing);
        // Même teinte que le modèle : tôle gris-vert, rouillée.
        Material paint = GameArt.Lit("CasierTole", new Color(0.3f, 0.33f, 0.29f), GameArt.Noise("rust", 64, 0.6f, 0.4f, 41), 0.3f);
        paint.SetFloat("_Metallic", 0.45f);
        Material inner = GameArt.Lit("CasierInterieur", new Color(0.16f, 0.15f, 0.13f), GameArt.Noise("rust", 64, 0.55f, 0.35f, 13), 0.2f);

        float w = Width / Count, doorBottom = OpeningBottom, doorTop = OpeningTop;
        var mesh = Resources.Load<Mesh>("Art/Decor/CasiersBati");
        var meshMat = Resources.Load<Material>("Art/Decor/Casiers");
        if (mesh != null && meshMat != null)
        {
            var frame = GameArt.MeshObject("Bati", holder, mesh, meshMat);
            frame.transform.localScale = Vector3.one * Scale;
            frame.transform.localPosition = new Vector3(0f, 0f, -SourceFront * Scale);
            frame.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        else
        {
            // Fond, côtés, dessus, socle, cloisons et étagères.
            Prim(holder, new Vector3(0f, Height * 0.5f, -Depth + 0.01f), new Vector3(Width, Height, 0.02f), inner);
            foreach (float s in new[] { -1f, 1f })
                Prim(holder, new Vector3(s * (Width * 0.5f - 0.01f), Height * 0.5f, -Depth * 0.5f), new Vector3(0.02f, Height, Depth), paint);
            Prim(holder, new Vector3(0f, Height - 0.01f, -Depth * 0.5f), new Vector3(Width, 0.02f, Depth), paint);
            Prim(holder, new Vector3(0f, doorBottom * 0.5f, -Depth * 0.5f), new Vector3(Width, doorBottom, Depth), paint);
            for (int i = 1; i < Count; i++)
                Prim(holder, new Vector3(-Width * 0.5f + i * w, Height * 0.5f, -Depth * 0.5f), new Vector3(0.015f, Height, Depth), paint);
            for (int i = 0; i < Count; i++)
                Prim(holder, new Vector3(-Width * 0.5f + (i + 0.5f) * w, Height - 0.38f, -Depth * 0.5f), new Vector3(w - 0.02f, 0.015f, Depth - 0.03f), inner);
            // Fond des compartiments, plus sombre.
        }
        // Collisions : le bâti (les portes ont la leur).
        box(holder.localPosition + holder.localRotation * new Vector3(0f, Height * 0.5f, -Depth * 0.5f),
            Abs(holder.localRotation * new Vector3(Width, Height, Depth)));

        for (int i = 0; i < Count; i++)
        {
            // Charnière à gauche de chaque compartiment (vu de face).
            Vector3 hinge = new Vector3(-Width * 0.5f + i * w + Post, doorBottom, 0.015f);
            LockerDoor.Create(holder, set * Count + i, hinge, w - Post * 2f, doorTop - doorBottom, paint);
            int door = set * Count + i;
            compartments[door] = holder.TransformPoint(new Vector3(-Width * 0.5f + (i + 0.5f) * w, doorBottom + 0.005f, -Depth * 0.45f));
            compartmentYaw[door] = holder.eulerAngles.y + 90f;
            compartmentBuilt[door] = true;
        }
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static GameObject Prim(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameArt.Prim(PrimitiveType.Cube, parent, pos, size, mat);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }
}

public class LockerDoor : Interactable
{
    static readonly LockerDoor[] doors = new LockerDoor[Lockers.TotalDoors];
    public static LockerDoor Get(int i) => i >= 0 && i < doors.Length ? doors[i] : null;
    const float OpenAngle = 105f, SwingTime = 0.55f;
    public int Index { get; private set; }
    bool open;
    float angle;

    public static LockerDoor Create(Transform parent, int index, Vector3 hinge, float width, float height, Material paint)
    {
        var go = new GameObject("PorteCasier" + (index + 1));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = hinge;
        var door = go.AddComponent<LockerDoor>();
        door.Index = index;
        var leaf = new GameObject("Battant").transform;
        leaf.SetParent(go.transform, false);
        leaf.localPosition = new Vector3(width * 0.5f, height * 0.5f, 0f);
        var model = GameArt.SpawnModel("Decor/PorteCasier", leaf, height);
        if (model != null)
        {
            Bounds b = GameArt.LocalBounds(model.transform);
            model.transform.localScale = Vector3.Scale(model.transform.localScale, new Vector3(width / Mathf.Max(0.01f, b.size.x), 1f, 0.03f / Mathf.Max(0.01f, b.size.z)));
            b = GameArt.LocalBounds(model.transform);
            model.transform.localPosition += new Vector3(-b.center.x, -height * 0.5f, -b.center.z);
        }
        else
        {
            Material dark = GameArt.Lit("CasierFente", new Color(0.05f, 0.045f, 0.04f), null, 0.2f);
            Material handle = GameArt.Lit("CasierPoignee", new Color(0.3f, 0.29f, 0.28f), null, 0.7f);
            Part(leaf, Vector3.zero, new Vector3(width, height, 0.02f), paint);
            // Persiennes en haut et en bas, poignée et porte-étiquette.
            for (int k = 0; k < 4; k++)
                foreach (float y in new[] { height * 0.5f - 0.12f - k * 0.045f, -height * 0.5f + 0.1f + k * 0.045f })
                    Part(leaf, new Vector3(0f, y, 0.011f), new Vector3(width * 0.6f, 0.012f, 0.004f), dark);
            Part(leaf, new Vector3(width * 0.36f, 0f, 0.025f), new Vector3(0.025f, 0.12f, 0.03f), handle);
            Part(leaf, new Vector3(0f, height * 0.18f, 0.012f), new Vector3(0.12f, 0.05f, 0.005f), handle);
        }
        var col = leaf.gameObject.AddComponent<BoxCollider>();
        col.size = new Vector3(width, height, 0.03f);
        doors[index] = door;
        door.SetOpen(GameSession.LockerOpen(index), false);
        return door;
    }

    static void Part(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameArt.Prim(PrimitiveType.Cube, parent, pos, size, mat);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    public void SetOpen(bool value, bool animate)
    {
        if (value != open && animate) GameAudio.Play3D("door", transform.position + Vector3.up, 0.45f, 12f, 0.08f, 1.35f);
        open = value;
        if (!animate) angle = open ? OpenAngle : 0f;
        Apply();
    }

    void Update()
    {
        float target = open ? OpenAngle : 0f;
        if (Mathf.Approximately(angle, target)) return;
        angle = Mathf.MoveTowards(angle, target, OpenAngle / SwingTime * Time.deltaTime);
        Apply();
    }

    // Charnière à gauche : la porte s'ouvre vers l'avant, en tournant vers la gauche.
    void Apply() => transform.localRotation = Quaternion.Euler(0f, -angle, 0f);

    // Aucun texte au survol (GDD) : seul le contour indique l'objet.
    public override string Prompt => "";
    public override void Interact() => GameSession.Instance.ToggleLocker(Index);

    void OnDestroy()
    {
        if (doors[Index] == this) doors[Index] = null;
    }
}
