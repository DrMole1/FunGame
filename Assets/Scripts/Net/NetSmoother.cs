using System.Collections.Generic;
using UnityEngine;

// Lissage d'un objet distant (autre joueur, La Dame) : les états reçus, horodatés par l'émetteur, sont rejoués
// avec un léger retard en interpolant entre les deux qui encadrent l'instant affiché. Un paquet en retard ou
// perdu ne provoque ainsi ni arrêt ni accélération.
public class NetSmoother
{
    // Retard d'affichage : couvre l'irrégularité d'arrivée et un paquet perdu (envoi à 20 Hz).
    const double Delay = 0.11;
    const double MaxExtrapolation = 0.25;
    // Au-delà, c'est une téléportation : pas d'interpolation.
    const float TeleportDistance = 5f;
    // Remontée maximale du décalage d'horloge (s par s) quand les paquets arrivent plus tard qu'avant.
    const double OffsetRelax = 0.01;

    struct Snap
    {
        public double t;
        public Vector3 pos;
        public float yaw;
    }

    readonly List<Snap> snaps = new List<Snap>();
    double offset, lastReceive, lastTime;
    bool started;

    public bool HasData => started;
    public Vector3 LatestPosition => snaps.Count > 0 ? snaps[snaps.Count - 1].pos : Vector3.zero;

    // Retourne false (et ignore l'état) s'il est plus ancien que le dernier reçu : arrivé dans le désordre.
    public bool Push(double senderTime, Vector3 pos, float yaw)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (started && senderTime <= lastTime) return false;
        // Décalage entre les horloges : celui du paquet le plus rapide, qui remonte lentement pour suivre la dérive.
        double o = now - senderTime;
        offset = started ? System.Math.Min(o, offset + (now - lastReceive) * OffsetRelax) : o;
        started = true;
        lastReceive = now;
        lastTime = senderTime;
        snaps.Add(new Snap { t = senderTime, pos = pos, yaw = yaw });
        if (snaps.Count > 32) snaps.RemoveAt(0);
        return true;
    }

    // Position et orientation à afficher maintenant.
    public void Sample(out Vector3 pos, out float yaw)
    {
        double t = Time.realtimeSinceStartupAsDouble - offset - Delay;
        // On garde un seul état antérieur à l'instant affiché.
        while (snaps.Count > 2 && snaps[1].t <= t) snaps.RemoveAt(0);

        Snap a = snaps[0];
        if (snaps.Count == 1 || t <= a.t)
        {
            pos = a.pos;
            yaw = a.yaw;
            return;
        }
        Snap b = snaps[1];
        if (t <= b.t)
        {
            if ((b.pos - a.pos).sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                pos = b.pos;
                yaw = b.yaw;
                return;
            }
            float k = (float)((t - a.t) / (b.t - a.t));
            pos = Vector3.Lerp(a.pos, b.pos, k);
            yaw = Mathf.LerpAngle(a.yaw, b.yaw, k);
            return;
        }
        // Plus rien de récent : on prolonge brièvement le dernier mouvement, puis on s'arrête.
        double ahead = System.Math.Min(t - b.t, MaxExtrapolation);
        bool teleport = (b.pos - a.pos).sqrMagnitude > TeleportDistance * TeleportDistance;
        Vector3 velocity = teleport ? Vector3.zero : (b.pos - a.pos) / (float)(b.t - a.t);
        pos = b.pos + velocity * (float)ahead;
        yaw = b.yaw;
    }
}
