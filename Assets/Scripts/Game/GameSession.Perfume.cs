using System.Collections.Generic;
using UnityEngine;

// V0.7.11 (7.9) : Parfum Odorant. Acheté au Terminal, il n'occupe pas l'inventaire : il rend l'acheteur "parfumé" pendant
// 8 minutes (un nouvel achat remplit la jauge et repart pour 8 minutes). Une jauge rouge pastel "Parfum" s'affiche au-dessus de
// la vie ; son libellé clignote lentement les 10 dernières secondes. À moins de 50 m d'un joueur parfumé, les autres joueurs
// voient des volutes rouge pastel tracer le chemin le plus court jusqu'à lui (selon les murs), qui apparaissent et s'effacent
// progressivement.
// Réseau (GDD 16, Note 13) : un seul message à l'achat, relayé par l'hôte (durée restante) ; le chemin est calculé chez chaque
// joueur, à partir des positions déjà synchronisées, et seulement toutes les 0,5 s (choix validé par l'utilisateur).
// Messages : PF -> PG|joueur|secondes                           achat du Parfum (et état envoyé aux joueurs qui arrivent)
public partial class GameSession
{
    public const float PerfumeDuration = 8f * 60f, PerfumeBlink = 10f, PerfumeRange = 50f, PerfumePathInterval = 0.5f;
    // V0.7.13 (Note 04) : volutes deux fois plus nombreuses, deux fois plus petites et plus lentes.
    const float PerfumeFade = 1.5f, ScentPerMeter = 5f, AuraPerSecond = 6f, ScentSpeed = 0.5f, ScentSize = 0.5f;
    static readonly Color PerfumeTint = new Color(1f, 0.62f, 0.64f, 0.55f);

    // Fin du parfum de chaque joueur (Time.time local).
    readonly Dictionary<string, float> perfumedUntil = new Dictionary<string, float>();

    class ScentTrail
    {
        public List<Vector3> path;
        public float length, weight, pathTimer;
    }
    readonly Dictionary<string, ScentTrail> scentTrails = new Dictionary<string, ScentTrail>();
    ParticleSystem scentParticles;

    float PerfumeLeft(string id) => perfumedUntil.TryGetValue(id, out float end) ? Mathf.Max(0f, end - Time.time) : 0f;

    // Le joueur local vient d'acheter (ou de recevoir) le Parfum.
    void StartPerfume()
    {
        perfumedUntil[net.MyId] = Time.time + PerfumeDuration;
        net.ToHost("PF");
    }

    void SendPerfumeSnapshot(string to)
    {
        foreach (var kv in perfumedUntil)
        {
            float left = kv.Value - Time.time;
            if (left > 0f) net.ToPeer(to, "PG|" + kv.Key + "|" + NetworkGame.F(left));
        }
    }

    bool HandlePerfumeMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "PF" when host:
                net.ToAll("PG|" + from + "|" + NetworkGame.F(PerfumeDuration));
                return true;
            case "PG" when f.Length >= 3:
                perfumedUntil[f[1]] = Time.time + Mathf.Clamp(NetworkGame.P(f[2]), 0f, PerfumeDuration);
                return true;
        }
        return false;
    }

    void ClearPerfume()
    {
        perfumedUntil.Clear();
        foreach (var t in scentTrails.Values) t.weight = 0f;
        scentTrails.Clear();
        if (hud != null) hud.SetPerfume(0f, false);
    }

    void UpdatePerfume()
    {
        float left = PerfumeLeft(net.MyId);
        hud.SetPerfume(left / PerfumeDuration, left <= PerfumeBlink);
        UpdateScentTrails();
        UpdatePerfumeAura();
    }

    // V0.7.12 (Note 16) : quelques volutes autour de chaque joueur parfumé (le joueur local compris), sans en mettre trop.
    void UpdatePerfumeAura()
    {
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || string.IsNullOrEmpty(p.PlayerId) || !p.isActiveAndEnabled || !p.Alive || PerfumeLeft(p.PlayerId) <= 0f) continue;
            if (Random.value >= AuraPerSecond * ParticleDensity.Factor * Time.deltaTime) continue;
            if (scentParticles == null) scentParticles = BuildScentParticles();
            Vector2 o = Random.insideUnitCircle.normalized * Random.Range(0.35f, 0.7f);
            scentParticles.Emit(new ParticleSystem.EmitParams
            {
                position = p.Position + new Vector3(o.x, Random.Range(-0.6f, 0.5f), o.y),
                velocity = new Vector3(o.x * 0.15f, Random.Range(0.1f, 0.3f), o.y * 0.15f) * ScentSpeed,
                startSize = Random.Range(0.2f, 0.4f) * ScentSize,
                startLifetime = Random.Range(1.2f, 1.8f),
                startColor = PerfumeTint,
                rotation = Random.Range(0f, 360f),
            }, 1);
        }
    }

    // Volutes vers chaque joueur parfumé à moins de 50 m (hors du joueur local lui-même).
    void UpdateScentTrails()
    {
        Vector3 me = player.transform.position;
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || p.IsLocal || string.IsNullOrEmpty(p.PlayerId)) continue;
            bool wanted = PerfumeLeft(p.PlayerId) > 0f && p.isActiveAndEnabled && !status.Dead && (p.Position - me).sqrMagnitude < PerfumeRange * PerfumeRange;
            if (!scentTrails.TryGetValue(p.PlayerId, out var trail))
            {
                if (!wanted) continue;
                scentTrails[p.PlayerId] = trail = new ScentTrail();
            }
            trail.weight = Mathf.MoveTowards(trail.weight, wanted ? 1f : 0f, Time.deltaTime / PerfumeFade);
            trail.pathTimer -= Time.deltaTime;
            if (wanted && (trail.path == null || trail.pathTimer <= 0f))
            {
                trail.pathTimer = PerfumePathInterval;
                trail.path = ScentPath(me, p.Position);
                trail.length = 0f;
                for (int i = 1; i < trail.path.Count; i++) trail.length += Vector3.Distance(trail.path[i - 1], trail.path[i]);
            }
            if (trail.weight > 0f && trail.path != null) EmitScent(trail);
        }
        foreach (string id in new List<string>(scentTrails.Keys))
            if (scentTrails[id].weight <= 0f && PerfumeLeft(id) <= 0f) scentTrails.Remove(id);
    }

    // Chemin le plus court entre les deux joueurs : de cellule en cellule dans une même zone, sinon en ligne droite.
    List<Vector3> ScentPath(Vector3 from, Vector3 to)
    {
        int zone = WorldBuilder.ZoneAt(from);
        if (zone >= 1 && zone <= zones.Count && zone == WorldBuilder.ZoneAt(to) && !WorldBuilder.IsInHub(from) && !WorldBuilder.IsInHub(to))
        {
            var path = WorldBuilder.CellPath(zones[zone - 1], from, to);
            if (path != null) return path;
        }
        return new List<Vector3> { from, to };
    }

    void EmitScent(ScentTrail trail)
    {
        if (scentParticles == null) scentParticles = BuildScentParticles();
        // V0.7.12 (Note 17) : environ 2,5 volutes par mètre et par seconde, à pleine intensité (4 fois moins qu'avant).
        // V0.7.22 (Note 05 du Menu Pause) : réglage "Particules" (Normal : ce nombre ; Peu : moitié ; Très Peu : quart).
        float rate = trail.length * ScentPerMeter * trail.weight * ParticleDensity.Factor * Time.deltaTime;
        int count = Mathf.FloorToInt(rate) + (Random.value < rate - Mathf.Floor(rate) ? 1 : 0);
        for (int k = 0; k < count; k++)
        {
            float at = Random.value * trail.length;
            int i = 1;
            for (; i < trail.path.Count - 1; i++)
            {
                float seg = Vector3.Distance(trail.path[i - 1], trail.path[i]);
                if (at <= seg) break;
                at -= seg;
            }
            Vector3 a = trail.path[i - 1], b = trail.path[Mathf.Min(i, trail.path.Count - 1)];
            float segLength = Mathf.Max(0.001f, Vector3.Distance(a, b));
            Vector3 p = Vector3.Lerp(a, b, Mathf.Clamp01(at / segLength));
            // Posées sur le sol sous le chemin (les points intermédiaires sont au centre des cellules, au niveau du Hub).
            float top = Mathf.Max(a.y, b.y) + 1.5f;
            if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit hit, top + 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y;
            p += new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.15f, 0.9f), Random.Range(-0.35f, 0.35f));
            var e = new ParticleSystem.EmitParams
            {
                position = p,
                velocity = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.05f, 0.25f), Random.Range(-0.15f, 0.15f)) * ScentSpeed,
                startSize = Random.Range(0.25f, 0.6f) * ScentSize,
                startLifetime = Random.Range(1.2f, 2f),
                startColor = new Color(PerfumeTint.r, PerfumeTint.g, PerfumeTint.b, PerfumeTint.a * trail.weight),
                rotation = Random.Range(0f, 360f),
            };
            scentParticles.Emit(e, 1);
        }
    }

    ParticleSystem BuildScentParticles()
    {
        var go = new GameObject("VolutesDeParfum");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 6000;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        color.color = g;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f * ScentSpeed;
        noise.frequency = 0.6f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GameArt.SoftPuff;
        ps.Play();
        return ps;
    }
}
