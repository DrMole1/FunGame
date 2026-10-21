using System.Collections.Generic;
using UnityEngine;

// Canon (GDD 7.5, V0.4.6). Un joueur qui saute sur un Canon chargé rentre dedans (s'il est libre) : caméra forcée à la
// première personne, légèrement zoomée, viseur en rond blanc, inventaire caché ; le fût suit sa visée. Sauter ou bouger
// le fait sortir ; il n'y est pas invincible. Un clic gauche le projette en boulet de canon, en légère cloche, vers son
// curseur : il ne peut plus rien faire, la caméra fixe son point d'arrivée, il est invincible. Les joueurs percutés sont
// repoussés sans dégâts ; les entités percutées explosent et réapparaissent ailleurs 1 min 30 plus tard (pas si la manche
// a changé entre-temps). À l'impact, il retrouve son état normal.
// Messages : CE|canon -> CI|canon / CR|canon (entrer, accepté ou refusé), CX|canon (sortir), CF|canon|cap (tir, à tous),
// CK|entité -> EX|x|y|z (entité percutée, explosion), CP|joueur|dx|dz -> KB|dx|dz (joueur percuté, repoussé).
public partial class GameSession
{
    const float CannonSpeed = 22f, CannonGravity = 6f, CannonRadius = 0.3f, CannonMaxFlight = 6f;
    const float CannonHitRadius = 1.3f, CannonKnockback = 9f;
    public const float CannonRespawnDelay = 90f;

    enum CannonState { None, Entering, Inside, Flying }
    static CannonState cannonState;
    // Dans le Canon (personnage caché) ; projeté en boulet de canon.
    public static bool InCannon => cannonState == CannonState.Inside || cannonState == CannonState.Entering;
    public static bool CannonFlying => cannonState == CannonState.Flying;

    Beacon cannon;
    float cannonEnterAt;
    readonly List<Vector3> flightPath = new List<Vector3>();
    float flightClock;
    Vector3 flightGoal;
    readonly HashSet<string> flightKnocked = new HashSet<string>();
    readonly HashSet<int> flightSmashed = new HashSet<int>();
    GameObject localCannonBall;

    // Hôte : joueur dans chaque Canon ; entités percutées à faire réapparaître.
    readonly Dictionary<int, string> cannonOccupants = new Dictionary<int, string>();
    readonly List<(EntityKind kind, int zone, float at, int round)> respawns = new List<(EntityKind, int, float, int)>();

    bool CannonOccupied(int id) =>
        cannonOccupants.TryGetValue(id, out string who) && who.Length > 0 && (who == net.MyId || net.Roster.ContainsKey(who));

    // Boulet de canon cartoon (modèle Meshy), centré sur le personnage.
    public static GameObject BuildCannonBall(Transform parent)
    {
        var ball = new GameObject("BouletDeCanon");
        ball.transform.SetParent(parent, false);
        var holder = new GameObject("Modele").transform;
        holder.SetParent(ball.transform, false);
        holder.localPosition = Vector3.down * 0.35f;
        if (GameArt.SpawnModel("Boulet", holder, 0.7f) == null)
            GameArt.Prim(PrimitiveType.Sphere, ball.transform, Vector3.zero, Vector3.one * 0.7f, GameArt.Lit("Boulet", new Color(0.08f, 0.08f, 0.09f), null, 0.8f));
        foreach (var c in ball.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        ball.AddComponent<Spinner>();
        ball.SetActive(false);
        return ball;
    }

    // Le boulet tourne sur lui-même en vol.
    class Spinner : MonoBehaviour
    {
        void Update() => transform.Rotate(new Vector3(540f, 0f, 120f) * Time.deltaTime, Space.Self);
    }

    void UpdateCannon()
    {
        if (net.IsHost) UpdateRespawns();
        switch (cannonState)
        {
            case CannonState.None:
                TryEnterCannon();
                break;
            case CannonState.Entering:
                // Sans réponse de l'hôte, on abandonne.
                if (Time.time - cannonEnterAt > 2f || status.Dead) cannonState = CannonState.None;
                break;
            case CannonState.Inside:
                UpdateInsideCannon();
                break;
            case CannonState.Flying:
                UpdateFlight();
                break;
        }
    }

    // Saut sur un Canon chargé : on demande à l'hôte d'y entrer.
    void TryEnterCannon()
    {
        if (status.Dead || PlayerController.InputLocked || localDig >= 0 || player.Grounded) return;
        if (Time.time - player.LastJumpTime > 1.5f) return;
        Vector3 me = player.transform.position;
        foreach (var b in beacons.Placed)
        {
            if (b == null || b.Kind != DeviceKind.Canon || !b.Charged) continue;
            Vector3 d = me - b.transform.position;
            if (new Vector2(d.x, d.z).magnitude > 1f || d.y < 0.3f || d.y > 2.2f) continue;
            cannon = b;
            cannonState = CannonState.Entering;
            cannonEnterAt = Time.time;
            net.ToHost("CE|" + b.Id);
            return;
        }
    }

    void EnterCannon()
    {
        if (cannon == null || status.Dead) { cannonState = CannonState.None; return; }
        cannonState = CannonState.Inside;
        player.SetFrozen(true);
        player.Teleport(cannon.transform.position + Vector3.up * 0.95f, OrbitCamera.Yaw);
        avatar.SetHidden(true);
        hud.SetCannonMode(true);
        OrbitCamera.CannonEye = cannon.Muzzle + Vector3.up * 0.35f;
        GameAudio.Play3D("pop", cannon.transform.position, 0.8f, 15f, 0.1f, 0.6f);
    }

    void UpdateInsideCannon()
    {
        if (status.Dead) { LeaveCannon(false); return; }
        if (cannon == null) { LeaveCannon(true); return; }
        cannon.AimCanon(OrbitCamera.Yaw);
        // Sauter ou bouger : il sort du Canon.
        if (player.WantsToMove) { LeaveCannon(true); return; }
        bool click = Input.GetMouseButtonDown(0) && OrbitCamera.Looking && Time.frameCount != OrbitCamera.LockFrame;
        if (!click) return;
        if (!cannon.Charged)
        {
            hud.Toast("Le Canon n'est plus chargé : il n'est plus relié à une Balise chargée.");
            return;
        }
        Fire();
    }

    // Sortie du Canon : à côté de lui (vivant), ou sur place (mort).
    void LeaveCannon(bool alive)
    {
        if (cannon != null) net.ToHost("CX|" + cannon.Id);
        cannonState = CannonState.None;
        OrbitCamera.CannonEye = null;
        hud.SetCannonMode(false);
        avatar.SetHidden(false);
        if (!alive) { cannon = null; return; }
        player.SetFrozen(false);
        if (cannon != null)
        {
            Vector3 back = Quaternion.Euler(0f, OrbitCamera.Yaw, 0f) * Vector3.back;
            player.Teleport(cannon.transform.position + back * 1.3f + Vector3.up * 1f, OrbitCamera.Yaw);
        }
        cannon = null;
    }

    // Tir : trajectoire calculée d'avance (légère cloche, jusqu'au premier obstacle), puis le vol.
    void Fire()
    {
        Camera cam = Camera.main;
        Vector3 dir = cam != null ? cam.transform.forward : Quaternion.Euler(0f, OrbitCamera.Yaw, 0f) * Vector3.forward;
        Vector3 start = cannon.Muzzle + dir * 0.8f;
        ComputeFlight(start, dir * CannonSpeed);
        net.ToAll("CF|" + cannon.Id + "|" + NetworkGame.F(OrbitCamera.Yaw));
        net.ToHost("CX|" + cannon.Id);
        cannon = null;
        OrbitCamera.CannonEye = null;
        OrbitCamera.FlightLook = flightGoal;
        OrbitCamera.Shake(1f);
        cannonState = CannonState.Flying;
        flightClock = 0f;
        flightKnocked.Clear();
        flightSmashed.Clear();
        if (localCannonBall == null) localCannonBall = BuildCannonBall(player.transform);
        localCannonBall.SetActive(true);
        player.Teleport(start, OrbitCamera.Yaw);
    }

    void ComputeFlight(Vector3 start, Vector3 velocity)
    {
        flightPath.Clear();
        flightPath.Add(start);
        Vector3 p = start;
        const float dt = 0.02f;
        for (float t = 0f; t < CannonMaxFlight; t += dt)
        {
            Vector3 next = p + velocity * dt;
            velocity += Vector3.down * CannonGravity * dt;
            Vector3 step = next - p;
            if (WorldHit(p, step, out RaycastHit hit))
            {
                flightPath.Add(hit.point + hit.normal * (CannonRadius + 0.15f));
                break;
            }
            flightPath.Add(next);
            p = next;
        }
        flightGoal = flightPath[flightPath.Count - 1];
    }

    // Premier obstacle du décor sur le trajet (les joueurs et les entités ne l'arrêtent pas : ils sont percutés).
    static bool WorldHit(Vector3 from, Vector3 step, out RaycastHit best)
    {
        best = default;
        float bestDist = float.MaxValue;
        foreach (var h in Physics.SphereCastAll(from, CannonRadius, step.normalized, step.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<RemotePlayer>() != null || h.collider.GetComponentInParent<Entity>() != null) continue;
            if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; }
        }
        return bestDist < float.MaxValue;
    }

    void UpdateFlight()
    {
        flightClock += Time.deltaTime;
        float f = flightClock / 0.02f;
        int i = Mathf.FloorToInt(f);
        if (i >= flightPath.Count - 1)
        {
            Land();
            return;
        }
        Vector3 pos = Vector3.Lerp(flightPath[i], flightPath[i + 1], f - i);
        player.Teleport(pos, player.transform.eulerAngles.y);
        CheckFlightHits(pos);
    }

    // Joueurs repoussés, entités pulvérisées, une seule fois chacun par vol.
    void CheckFlightHits(Vector3 pos)
    {
        foreach (var kv in net.Remotes)
        {
            var rp = kv.Value;
            if (rp == null || rp.Dead || flightKnocked.Contains(kv.Key)) continue;
            if (Vector3.Distance(rp.transform.position, pos) > CannonHitRadius) continue;
            flightKnocked.Add(kv.Key);
            Vector3 away = rp.transform.position - pos;
            net.ToHost("CP|" + kv.Key + "|" + NetworkGame.F(away.x) + "|" + NetworkGame.F(away.z));
        }
        foreach (var e in entities.Values)
        {
            if (e == null || flightSmashed.Contains(e.Id)) continue;
            Vector3 c = e.transform.position + Vector3.up * e.ChestHeight * 0.6f;
            if (Vector3.Distance(c, pos) > CannonHitRadius + 0.3f) continue;
            flightSmashed.Add(e.Id);
            net.ToHost("CK|" + e.Id);
        }
    }

    // Impact : le joueur retrouve son état normal, debout au sol près du point d'arrivée.
    void Land()
    {
        cannonState = CannonState.None;
        OrbitCamera.FlightLook = null;
        OrbitCamera.Shake(0.6f);
        if (localCannonBall != null) localCannonBall.SetActive(false);
        Vector3 at = flightGoal;
        GameAudio.Play3D("cannon_impact", at, 1f, 30f, 0.1f);
        Fx.Burst(at, Fx.Dirt, 18, 4f, 0.1f);
        Vector3 feet = Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? ground.point : at;
        player.SetFrozen(false);
        player.Teleport(feet + Vector3.up * 0.95f, player.transform.eulerAngles.y);
        avatar.SetHidden(false);
        hud.SetCannonMode(false);
    }

    // ---------- Hôte ----------

    void UpdateRespawns()
    {
        for (int i = respawns.Count - 1; i >= 0; i--)
        {
            var r = respawns[i];
            // La manche a changé : une autre entité a pris sa place.
            if (r.round != round || phase != Phase.Running || maze == null) { respawns.RemoveAt(i); continue; }
            if (Time.time < r.at) continue;
            respawns.RemoveAt(i);
            var rnd = new System.Random(seed + nextEntityId * 131 + Mathf.RoundToInt(Time.time * 10f));
            SpawnKind(r.kind, r.zone, rnd);
        }
    }

    void SpawnKind(EntityKind kind, int zone, System.Random rnd)
    {
        float clearance = kind == EntityKind.Spider ? GameRules.SpiderSpawnClearance : GameRules.EntitySpawnClearance;
        switch (kind)
        {
            case EntityKind.Dame: SpawnAt<Dame>(zone, rnd, clearance); break;
            case EntityKind.Tour: SpawnAt<Tour>(zone, rnd, clearance); break;
            case EntityKind.Pion: SpawnAt<Pion>(zone, rnd, clearance); break;
            case EntityKind.Cloche: SpawnAt<Cloche>(zone, rnd, clearance); break;
            case EntityKind.Spider: SpawnAt<Spider>(zone, rnd, clearance); break;
            case EntityKind.Fou: SpawnAt<Fou>(zone, rnd, clearance); break;
            case EntityKind.Roi: SpawnAt<Roi>(zone, rnd, clearance); break;
        }
    }

    // Une entité percutée par un boulet explose et meurt ; elle réapparaîtra ailleurs 1 min 30 plus tard.
    void SmashEntity(int id)
    {
        if (!entities.TryGetValue(id, out var e) || e == null) return;
        Vector3 pos = e.transform.position;
        int node = maze != null ? maze.nav.NodeAt(pos) : -1;
        int zone = node >= 0 ? Mathf.Max(0, maze.nav.ZoneOfNode(node)) : 0;
        respawns.Add((e.Kind, zone, Time.time + CannonRespawnDelay, round));
        Destroy(e.gameObject);
        entities.Remove(id);
        watching.Remove(id);
        net.ToAll("EX|" + NetworkGame.F(pos.x) + "|" + NetworkGame.F(pos.y + 1f) + "|" + NetworkGame.F(pos.z));
    }

    static void Explode(Vector3 at)
    {
        GameAudio.Play3D("cannon_impact", at, 1f, 35f, 0.05f, 0.7f);
        Fx.Burst(at, GameArt.Lit("Eclats", new Color(0.12f, 0.12f, 0.12f), null, 0.2f), 30, 7f, 0.14f, 1.4f);
        Fx.Burst(at, GameArt.Emissive("Flammeche", new Color(1f, 0.5f, 0.1f), new Color(1.5f, 0.6f, 0.1f)), 16, 5f, 0.08f, 0.8f);
    }

    // Messages du Canon (renvoie false si le message n'en est pas un).
    bool HandleCannonMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "CE" when host && f.Length >= 2:
            {
                int id = NetworkGame.I(f[1]);
                Beacon b = beacons.Get(id);
                if (b != null && b.Kind == DeviceKind.Canon && !CannonOccupied(id))
                {
                    cannonOccupants[id] = from;
                    net.ToPeer(from, "CI|" + id);
                }
                else net.ToPeer(from, "CR|" + id);
                return true;
            }
            case "CX" when host && f.Length >= 2:
            {
                int id = NetworkGame.I(f[1]);
                if (cannonOccupants.TryGetValue(id, out string who) && who == from) cannonOccupants.Remove(id);
                return true;
            }
            case "CI" when f.Length >= 2:
                if (cannonState == CannonState.Entering && cannon != null && cannon.Id == NetworkGame.I(f[1])) EnterCannon();
                return true;
            case "CR" when f.Length >= 2:
                if (cannonState == CannonState.Entering)
                {
                    cannonState = CannonState.None;
                    cannon = null;
                    hud.Toast("Quelqu'un est déjà dans ce Canon.");
                }
                return true;
            case "CF" when f.Length >= 3:
            {
                Beacon b = beacons.Get(NetworkGame.I(f[1]));
                if (b != null) b.FireCanon(NetworkGame.P(f[2]));
                return true;
            }
            case "CK" when host && f.Length >= 2:
                SmashEntity(NetworkGame.I(f[1]));
                return true;
            case "EX" when f.Length >= 4:
                Explode(new Vector3(NetworkGame.P(f[1]), NetworkGame.P(f[2]), NetworkGame.P(f[3])));
                return true;
            case "CP" when host && f.Length >= 4:
                net.ToPeer(f[1], "KB|" + f[2] + "|" + f[3]);
                return true;
            case "KB" when f.Length >= 3:
                if (!status.Dead && !Teleporting && !CannonFlying && !InCannon)
                    player.Knockback(new Vector3(NetworkGame.P(f[1]), 0f, NetworkGame.P(f[2])), CannonKnockback);
                return true;
        }
        return false;
    }

    // Partie quittée ou recommencée : plus de Canon en cours.
    void ResetCannon()
    {
        if (cannonState != CannonState.None && player != null)
        {
            player.SetFrozen(false);
            avatar.SetHidden(false);
        }
        cannonState = CannonState.None;
        cannon = null;
        OrbitCamera.CannonEye = null;
        OrbitCamera.FlightLook = null;
        if (hud != null) hud.SetCannonMode(false);
        if (localCannonBall != null) localCannonBall.SetActive(false);
        cannonOccupants.Clear();
        respawns.Clear();
    }
}
