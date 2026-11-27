using System.Collections.Generic;
using UnityEngine;

// Jeton Noir (GDD 8.11, V0.7.22) : règles tenues par l'hôte, déplacements montrés chez tous (JetonNoir).
// - Balise posée sur un Jeton Noir : il gonfle un instant, la Balise éclate.
// - Balise posée dans son axe (tour : X et Z ; fou : diagonales ; dame : les deux), sur un sol de même hauteur, plat, sans creux, pont,
//   tunnel, toboggan ni surface solide jusqu'à elle (joueurs, entités et objets posés au sol ne comptent pas) : il fonce dessus
//   (vitesse du joueur x 3,2) ; arrivé, la Balise éclate (si elle a été ramassée entre-temps, rien n'est cassé).
// - Pendant qu'un Jeton Noir se déplace, poser une Balise ne déclenche aucun autre déplacement ; deux Jetons qui se heurtent s'arrêtent.
// - En chemin : un joueur percuté perd 50 PV et est projeté au loin ; une autre Balise éclate ; tout autre objet posé au sol explose et
//   disparaît ; une entité Cauchemar, Sanction ou Normale explose et disparaît (Cauchemar et Sanction : membres en éclats, sans sang)
//   et réapparaît ailleurs 1 min 30 plus tard.
// Messages : QB|jeton|balise (gonflement), QM|jeton|x|y|z|balise (départ), QS|jeton|x|y|z (arrêt), QX|appareil|0 ou 1 (Balise qui
// éclate, ou objet qui explose), QF|x|y|z (objet à ramasser qui explose), QP|jeton|x|y|z (position, pour un joueur qui arrive).
public partial class GameSession
{
    // Écart toléré avec l'axe (la moitié d'une case du damier de 1 m) ; au même niveau à 15 cm près.
    const float JetonAxisTolerance = 0.5f, JetonLevelTolerance = 0.15f, JetonMinTravel = 0.6f;
    const float JetonPathStep = 0.25f, JetonFloorTolerance = 0.08f;

    // Hôte : Jetons en déplacement, et leur numéro de déplacement avant le départ (le départ, QM, revient aussi à l'hôte).
    readonly Dictionary<int, int> jetonsMoving = new Dictionary<int, int>();
    // Chaque joueur : déplacements (Jeton, numéro) qui l'ont déjà touché.
    readonly HashSet<(int, int)> jetonHits = new HashSet<(int, int)>();

    void UpdateJetons()
    {
        if (JetonNoir.All.Count == 0) return;
        if (net.IsHost) UpdateJetonsHost();
        UpdateJetonLocalHits();
    }

    void ResetJetons()
    {
        jetonsMoving.Clear();
        jetonHits.Clear();
    }

    // ---------- Pose d'une Balise (hôte) ----------

    void JetonsOnBeaconPlaced(int beaconId, Vector3 pos)
    {
        if (JetonNoir.All.Count == 0 || WorldBuilder.IsInHub(pos)) return;
        if (JetonNoir.AnyMoving() || jetonsMoving.Count > 0) return;
        int zone = WorldBuilder.ZoneAt(pos);
        // Posée sur un Jeton Noir.
        foreach (var j in JetonNoir.All.Values)
        {
            if (j == null || j.Zone != zone) continue;
            Vector3 d = pos - j.Base;
            if (new Vector2(d.x, d.z).magnitude < JetonNoir.Radius + 0.25f && d.y > 0.3f && d.y < JetonNoir.Height * 1.3f)
            {
                net.ToAll("QB|" + j.Id + "|" + beaconId);
                BreakDevice(beaconId, false);
                return;
            }
        }
        // Dans l'axe d'un ou plusieurs Jetons Noirs.
        var starts = new List<(JetonNoir j, Vector3 target)>();
        foreach (var j in JetonNoir.All.Values)
        {
            if (j == null || j.Zone != zone || !j.gameObject.activeInHierarchy) continue;
            if (!JetonTarget(j, pos, out Vector3 target)) continue;
            if (!JetonPathClear(j, target)) continue;
            starts.Add((j, target));
        }
        foreach (var (j, target) in starts)
        {
            jetonsMoving[j.Id] = j.MoveSerial;
            net.ToAll("QM|" + j.Id + "|" + NetworkGame.F(target.x) + "|" + NetworkGame.F(target.y) + "|" + NetworkGame.F(target.z) + "|" + beaconId);
        }
    }

    // Point d'arrivée sur l'axe de déplacement du Jeton, face à la Balise (false : Balise hors de ses axes ou d'un autre niveau).
    static bool JetonTarget(JetonNoir j, Vector3 beacon, out Vector3 target)
    {
        target = j.Base;
        Vector3 d = beacon - j.Base;
        if (Mathf.Abs(d.y) > JetonLevelTolerance) return false;
        float bestOff = float.MaxValue;
        Vector3 bestAxis = Vector3.zero;
        void Try(Vector3 axis)
        {
            axis.Normalize();
            float along = d.x * axis.x + d.z * axis.z;
            if (along < JetonMinTravel) return;
            float off = Mathf.Abs(d.x * axis.z - d.z * axis.x);
            if (off < JetonAxisTolerance && off < bestOff) { bestOff = off; bestAxis = axis * along; }
        }
        if (j.Rule != JetonNoir.Moves.Diagonal)
        {
            Try(new Vector3(Mathf.Sign(d.x), 0f, 0f));
            Try(new Vector3(0f, 0f, Mathf.Sign(d.z)));
        }
        if (j.Rule != JetonNoir.Moves.Straight) Try(new Vector3(Mathf.Sign(d.x), 0f, Mathf.Sign(d.z)));
        if (bestOff == float.MaxValue) return false;
        target = j.Base + bestAxis;
        return true;
    }

    // Ce qui ne gêne pas un Jeton : joueurs, entités, objets posés au sol ou à ramasser, éclats, et le Jeton lui-même.
    static bool JetonIgnores(Collider col, JetonNoir self)
    {
        if (col == null || col.gameObject.layer == 2) return true;
        if (self != null && col.transform.IsChildOf(self.transform)) return true;
        return col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<RemotePlayer>() != null
            || col.GetComponentInParent<Entity>() != null || col.GetComponentInParent<Beacon>() != null || col.GetComponentInParent<ItemPickup>() != null;
    }

    // Sol plat, de même hauteur, sans creux ni pont, tunnel, toboggan ou escalier, et aucune surface solide jusqu'à la cible.
    static bool JetonPathClear(JetonNoir j, Vector3 target)
    {
        Vector3 from = j.Base;
        Vector3 delta = target - from;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist < 0.01f) return false;
        Vector3 dir = delta / dist;
        for (float t = JetonPathStep; t <= dist + JetonNoir.Radius + 0.001f; t += JetonPathStep)
        {
            Vector3 p = from + dir * Mathf.Min(t, dist + JetonNoir.Radius);
            float best = float.NegativeInfinity;
            Collider floor = null;
            foreach (var hit in Physics.RaycastAll(p + Vector3.up * 1f, Vector3.down, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (JetonIgnores(hit.collider, j)) continue;
                if (hit.point.y > best) { best = hit.point.y; floor = hit.collider; }
            }
            if (floor == null || Mathf.Abs(best - from.y) > JetonFloorTolerance || WorldBuilder.JetonForbidden(floor)) return false;
        }
        // Aucune surface solide sur le trajet (le corps de la pièce, au-dessus du sol).
        Vector3 lo = from + Vector3.up * (JetonNoir.Radius + 0.2f), hi = from + Vector3.up * (JetonNoir.Height - JetonNoir.Radius);
        foreach (var hit in Physics.CapsuleCastAll(lo, hi, JetonNoir.Radius * 0.9f, dir, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!JetonIgnores(hit.collider, j)) return false;
        return true;
    }

    // ---------- Déplacements (hôte) ----------

    void UpdateJetonsHost()
    {
        if (jetonsMoving.Count == 0) return;
        foreach (var kv in new List<KeyValuePair<int, int>>(jetonsMoving))
        {
            int id = kv.Key;
            if (!JetonNoir.All.TryGetValue(id, out var j) || j == null) { jetonsMoving.Remove(id); continue; }
            // Départ pas encore reçu.
            if (j.MoveSerial == kv.Value) continue;
            Vector3 at = j.transform.position;
            // Heurte un autre Jeton Noir : il s'arrête (l'autre aussi s'il se déplaçait).
            foreach (var other in JetonNoir.All.Values)
            {
                if (other == null || other == j || other.Zone != j.Zone) continue;
                Vector3 d = other.transform.position - at;
                if (Mathf.Abs(d.y) > 0.5f || new Vector2(d.x, d.z).magnitude > JetonNoir.Radius * 2f + 0.02f) continue;
                StopJeton(j);
                if (other.Moving) StopJeton(other);
                break;
            }
            JetonSweep(j);
            if (!j.Moving && jetonsMoving.ContainsKey(id))
            {
                // Arrivé : la Balise visée éclate (si elle est toujours là).
                Beacon target = j.TargetBeacon >= 0 ? beacons.Get(j.TargetBeacon) : null;
                if (target != null && target.Kind == DeviceKind.Beacon && Flat(target.transform.position - j.transform.position) < JetonNoir.Radius + 0.6f)
                    BreakDevice(target.Id, false);
                StopJeton(j);
            }
        }
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    void StopJeton(JetonNoir j)
    {
        jetonsMoving.Remove(j.Id);
        Vector3 p = j.transform.position;
        net.ToAll("QS|" + j.Id + "|" + NetworkGame.F(p.x) + "|" + NetworkGame.F(p.y) + "|" + NetworkGame.F(p.z));
    }

    // Ce que le Jeton percute en chemin (hôte) : Balises, autres objets, objets à ramasser, entités.
    void JetonSweep(JetonNoir j)
    {
        Vector3 at = j.transform.position;
        float reach = JetonNoir.Radius + 0.35f;
        foreach (var b in new List<Beacon>(beacons.Placed))
        {
            if (b == null || b.IsHub || CannonOccupied(b.Id)) continue;
            Vector3 d = b.transform.position - at;
            if (d.y < -0.6f || d.y > JetonNoir.Height || Flat(d) > reach + 0.2f) continue;
            // La Balise visée éclate à l'arrivée ; les autres dès qu'il les touche.
            BreakDevice(b.Id, b.Kind != DeviceKind.Beacon);
        }
        foreach (var kv in new List<KeyValuePair<int, ItemPickup>>(items))
        {
            if (kv.Value == null || picked.Contains(kv.Key)) continue;
            Vector3 p = kv.Value.transform.position;
            Vector3 d = p - at;
            if (d.y < -0.6f || d.y > JetonNoir.Height || Flat(d) > reach) continue;
            picked.Add(kv.Key);
            net.ToAll("IR|" + round + "|" + kv.Key);
            net.ToAll("QF|" + NetworkGame.F(p.x) + "|" + NetworkGame.F(p.y) + "|" + NetworkGame.F(p.z));
        }
        foreach (var e in new List<Entity>(entities.Values))
        {
            if (e == null) continue;
            Vector3 d = e.transform.position - at;
            if (d.y < -1f || d.y > JetonNoir.Height || Flat(d) > JetonNoir.Radius + 0.6f) continue;
            JetonSmashEntity(e);
        }
    }

    // Appareil posé détruit : une Balise éclate (morceaux), un autre objet explose.
    void BreakDevice(int id, bool explode)
    {
        if (!beaconMessages.Remove(id)) return;
        net.ToAll("QX|" + id + "|" + (explode ? 1 : 0));
    }

    // Entité percutée (hôte) : elle explose et disparaît (Cauchemar et Sanction : membres en éclats), puis réapparaît ailleurs.
    void JetonSmashEntity(Entity e)
    {
        int id = e.Id;
        Vector3 pos = e.transform.position;
        int zone = Mathf.Max(1, e.Zone);
        bool shatter = e.Nightmare || e.Kind == EntityKind.TeteDeCloche;
        if (e.Kind != EntityKind.Reveil && e.Kind != EntityKind.TeteDeCloche) respawns.Add((e.Kind, zone, Time.time + GameRules.JetonNoirRespawnDelay, round));
        net.ToAll("EX|" + NetworkGame.F(pos.x) + "|" + NetworkGame.F(pos.y + 1f) + "|" + NetworkGame.F(pos.z) + "|" + id + "|" + (shatter ? 1 : 2));
        if (entities.TryGetValue(id, out var left) && left != null) Destroy(left.gameObject);
        entities.Remove(id);
        watching.Remove(id);
    }

    // ---------- Joueur local percuté (chez chacun) ----------

    void UpdateJetonLocalHits()
    {
        if (status.Dead || player == null) return;
        Vector3 me = player.transform.position;
        foreach (var j in JetonNoir.All.Values)
        {
            if (j == null || !j.Moving) continue;
            Vector3 d = me - j.transform.position;
            // Le centre du personnage est à 0,9 m au-dessus de ses pieds.
            if (d.y < 0f || d.y > JetonNoir.Height + 0.9f || Flat(d) > JetonNoir.Radius + 0.45f) continue;
            if (!jetonHits.Add((j.Id, j.MoveSerial))) continue;
            Vector3 away = new Vector3(d.x, 0f, d.z);
            Vector3 travel = j.Target - j.transform.position;
            travel.y = 0f;
            // Projeté dans le sens de la course du Jeton, et vers l'extérieur.
            Vector3 push = (travel.normalized * 1.5f + away.normalized).normalized;
            DamageLocal(GameRules.JetonNoirDamage, "Jeton Noir");
            GameAudio.Play3D("tour_impact", me, 0.9f, 20f, 0.1f, 0.8f);
            if (!status.Dead && !Teleporting && !CannonFlying && !InCannon) player.Knockback(push, GameRules.JetonNoirKnockback);
        }
    }

    // ---------- Messages ----------

    bool HandleJetonMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "QB" when f.Length >= 2:
                if (JetonNoir.All.TryGetValue(NetworkGame.I(f[1]), out var bumped) && bumped != null) bumped.Bump();
                return true;
            case "QM" when f.Length >= 6:
            {
                if (JetonNoir.All.TryGetValue(NetworkGame.I(f[1]), out var j) && j != null)
                    j.StartMove(new Vector3(NetworkGame.P(f[2]), NetworkGame.P(f[3]), NetworkGame.P(f[4])), NetworkGame.I(f[5]));
                return true;
            }
            case "QS" when f.Length >= 5:
            case "QP" when f.Length >= 5:
            {
                if (JetonNoir.All.TryGetValue(NetworkGame.I(f[1]), out var j) && j != null)
                    j.StopAt(new Vector3(NetworkGame.P(f[2]), NetworkGame.P(f[3]), NetworkGame.P(f[4])));
                return true;
            }
            case "QX" when f.Length >= 3:
            {
                int id = NetworkGame.I(f[1]);
                Beacon b = beacons.Get(id);
                if (b != null)
                {
                    Vector3 p = b.transform.position;
                    bool explode = f[2] == "1";
                    JetonNoir.Shatter(p, explode);
                    if (explode)
                    {
                        GameAudio.Play3D("cannon_impact", p, 0.8f, 30f, 0.1f, 1.2f);
                        Fx.Burst(p + Vector3.up * 0.4f, Fx.Dirt, 14, 3f, 0.1f);
                    }
                    else GameAudio.Play3D("orb_break", p, 0.9f, 20f, 0.1f);
                }
                beacons.Remove(id);
                return true;
            }
            case "QF" when f.Length >= 4:
            {
                var p = new Vector3(NetworkGame.P(f[1]), NetworkGame.P(f[2]), NetworkGame.P(f[3]));
                JetonNoir.Shatter(p, true);
                GameAudio.Play3D("cannon_impact", p, 0.7f, 25f, 0.1f, 1.3f);
                return true;
            }
        }
        return false;
    }

    // Joueur qui arrive : Jetons Noirs déjà déplacés.
    void SendJetonSnapshot(string to)
    {
        foreach (var j in JetonNoir.All.Values)
        {
            if (j == null || !j.Displaced) continue;
            Vector3 p = j.Moving ? j.Target : j.transform.position;
            net.ToPeer(to, "QP|" + j.Id + "|" + NetworkGame.F(p.x) + "|" + NetworkGame.F(p.y) + "|" + NetworkGame.F(p.z));
        }
    }
}
