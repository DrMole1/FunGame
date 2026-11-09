using System.Collections.Generic;
using UnityEngine;

// V0.6.7 (GDD 3.4, notes 05 à 07, et chapitre 8) : dégâts de chute, empreintes et traînées de sang, Tête de Cloche qui traîne le corps.
// Messages : FP|x|y|z|cap   empreintes de pattes laissées par une chute (les traînées se déduisent des PV déjà transmis).
public partial class GameSession
{
    const float TrailSpacing = 0.42f, PawSpacing = 0.22f, PawSize = 0.42f, DropSizeMin = 0.22f, DropSizeMax = 0.42f;
    const float DragDistance = 1.3f, DragFollowSpeed = 6f;

    // ================= Chutes =================

    float fallSlowEnd = -10f, fallSlowAmount;

    void OnLanded(float height)
    {
        if (height <= GameRules.FallSafeHeight) return;
        int before = status.Hp;
        DamageLocal(GameRules.FallDamage(height), "Chute");
        int lost = before - status.Hp;
        if (lost <= 0) return;
        Vector3 p = player.transform.position;
        net.ToAll("FP|" + NetworkGame.F(p.x) + "|" + NetworkGame.F(p.y) + "|" + NetworkGame.F(p.z) + "|" + NetworkGame.F(player.transform.eulerAngles.y));
        if (lost < GameRules.FallSlowMinDamage || status.Dead) return;
        float ratio = Mathf.Clamp01(lost / (float)GameRules.FallSlowFullDamage);
        fallSlowAmount = GameRules.FallSlowMax * ratio;
        fallSlowEnd = Time.time + GameRules.FallSlowDuration * ratio;
    }

    // Ralenti pendant la durée, puis retour progressif à la vitesse de base.
    float FallSpeedFactor()
    {
        if (Time.time < fallSlowEnd) return 1f - fallSlowAmount;
        return 1f - fallSlowAmount * (1f - Mathf.Clamp01((Time.time - fallSlowEnd) / GameRules.FallSlowRecovery));
    }

    static void StampPaws(Vector3 position, float yaw)
    {
        Vector3 side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right * PawSpacing;
        BloodMarks.Stamp(BloodMarks.Kind.Paw, position - side, yaw - 8f, PawSize);
        BloodMarks.Stamp(BloodMarks.Kind.Paw, position + side, yaw + 8f, PawSize);
    }

    // ================= Traînées de sang =================

    class Bleed
    {
        public int hp;
        public float until;
        public Vector3 last;
    }

    readonly Dictionary<string, Bleed> bleeds = new Dictionary<string, Bleed>();

    void UpdateBlood()
    {
        foreach (var p in PlayerPresence.All)
        {
            if (p == null || string.IsNullOrEmpty(p.PlayerId)) continue;
            int hp;
            if (p.PlayerId == net.MyId) hp = status.Hp;
            else if (net.Remotes.TryGetValue(p.PlayerId, out var rp) && rp != null) hp = rp.Dead ? 0 : rp.Hp;
            else continue;
            if (!bleeds.TryGetValue(p.PlayerId, out var b)) bleeds[p.PlayerId] = b = new Bleed { hp = hp, last = p.Position };
            if (hp < b.hp)
            {
                float t = Mathf.Clamp01((b.hp - hp - 1f) / 99f);
                b.until = Mathf.Max(b.until, Time.time + Mathf.Lerp(GameRules.BleedMin, GameRules.BleedMax, t));
            }
            b.hp = hp;

            bool dragged = !p.Alive && DraggedBy(p.PlayerId) != null;
            bool bleeding = dragged || p.Alive && Time.time < b.until && !p.InCannon && !p.Teleporting;
            Vector3 flat = p.Position - b.last;
            flat.y = 0f;
            if (!bleeding) { b.last = p.Position; continue; }
            if (flat.sqrMagnitude < TrailSpacing * TrailSpacing) continue;
            b.last = p.Position;
            BloodMarks.Stamp(BloodMarks.Kind.Drop, p.Position + new Vector3(Random.Range(-0.12f, 0.12f), 0f, Random.Range(-0.12f, 0.12f)), Random.Range(0f, 360f), Random.Range(DropSizeMin, DropSizeMax));
        }
    }

    // ================= Corps traîné par La Tête de Cloche =================

    TeteDeCloche draggedBy;
    bool dragging;
    // Le corps du joueur local a disparu avec l'entité (annoncé aux autres avec sa position).
    public static bool CorpseGone { get; private set; }

    TeteDeCloche DraggedBy(string playerId)
    {
        foreach (var e in entities.Values)
            if (e is TeteDeCloche t && t.TargetId == playerId && t.Current == TeteDeCloche.State.Leave) return t;
        return null;
    }

    void UpdateCorpseDrag()
    {
        if (!status.Dead || CorpseGone || deathFxAt >= 0f) return;
        if (!dragging)
        {
            draggedBy = DraggedBy(net.MyId);
            dragging = draggedBy != null;
            if (!dragging) return;
        }
        if (draggedBy == null || draggedBy.Current != TeteDeCloche.State.Leave)
        {
            // L'entité disparaît, le cadavre avec elle.
            dragging = false;
            draggedBy = null;
            CorpseGone = true;
            avatar.SetHidden(true);
            return;
        }
        Vector3 goal = draggedBy.transform.position - draggedBy.transform.forward * DragDistance;
        Vector3 start = goal + Vector3.up * 1.5f;
        goal.y = (Physics.Raycast(start, Vector3.down, out RaycastHit ground, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? ground.point.y : goal.y) + 0.95f;
        player.Teleport(Vector3.MoveTowards(player.transform.position, goal, DragFollowSpeed * Time.deltaTime), player.transform.eulerAngles.y);
    }

    void ResetCorpse()
    {
        dragging = false;
        draggedBy = null;
        CorpseGone = false;
        OrbitCamera.DeathSpot = null;
        if (avatar != null) avatar.SetHidden(false);
    }

    // ================= Ordres sonores des glyphes (V0.6.7) =================

    static string OrderVoice(int type)
    {
        string language;
        switch (Loc.Current)
        {
            case Lang.Fr: language = "fr"; break;
            case Lang.Es: language = "esp"; break;
            case Lang.Ru: language = "rus"; break;
            default: language = "eng"; break;
        }
        return "order_" + language + "_0" + (type + 1);
    }
}
