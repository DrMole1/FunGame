using System.Collections.Generic;
using UnityEngine;

// V0.7.11 : nouveaux objets (7.11 à 7.17) et Journaux (17.5).
// - Mégaphone (7.11) : sélectionné, il est devant la bouche ; l'aboiement porte deux fois plus loin, plus fort, et projette en
//   cône, sur 15 m, une onde de choc qui repousse les autres joueurs, les balles (ballons des Hubs, Super Balles) et les entités
//   de type "normale".
// - Métronome (7.12) : regardé à moins de 20 m, la caméra penche au rythme des battements.
// - Super Balle (7.13) : voir SuperBall.
// - Collier à Laisse (7.14) : sélectionné, les autres joueurs (à 5 m ou moins) deviennent interactifs ; un clic leur passe le
//   collier (le joueur est "soumis", celui qui tient le Collier est son "maître"), un second clic le retire. Une chaîne relie le
//   maître au soumis, qui ne peut pas s'en éloigner de plus de 5 m (il est traîné).
// - Lunettes PEACE (7.15) : dans l'inventaire, vision rose, cœurs roses et arcs-en-ciel, luminosité augmentée, musique douce.
// - Sac à Os (7.16) : un exemplaire dans le Hub II, près du Bouton Rouge ; il absorbe les Os de l'inventaire (15 au plus), un
//   autre joueur peut y glisser un Os en cliquant sur le sac porté ; vendu, il reste mais ses Os se vendent ; au-delà de 5 Os, il
//   ralentit son porteur.
// - Notice (7.17) : dans le Hub II, posée au sol ; on la lit en main ("Pour porter les os." et l'image).
// - Journaux (17.5) : durant les 5 premières manches, un monticule de plus dans la Zone 1, avec un Journal (un autre à chaque manche).
// Messages : CL|joueur -> CC|soumis|maître (maître vide : collier retiré)   Collier à Laisse
//            SB|joueur -> SG|donneur                                     Os glissé dans le Sac à Os d'un autre joueur
//            UB|balle|x|y|z|vx|vz -> UL|joueur|balle|x|y|z|vx|vz           Super Balle poussée
//            OR                                                          joueur libéré d'une cloche (Note 01)
public partial class GameSession
{
    public const float MegaphoneRangeFactor = 2f, MegaphoneWaveRange = 15f, MegaphoneWaveAngle = 35f, MegaphoneWaveSpeed = 11f;
    public const float CollarRange = 5f;
    public const int SuperBallDamage = 10;
    const float SuperBallKnockback = 13f;

    // Nombre d'Os du Sac à Os du joueur local (état envoyé aux autres joueurs).
    public static int LocalBagBones { get; private set; }

    // Case du Sac à Os dans l'inventaire (-1 : aucun) et nombre d'Os qu'il contient.
    int BoneBagSlot()
    {
        for (int i = 0; i < GameRules.InventorySize; i++) if (Items.IsBoneBag(status[i])) return i;
        return -1;
    }

    int BagBones
    {
        get
        {
            int slot = BoneBagSlot();
            return slot >= 0 ? Items.BonesIn(status[slot]) : 0;
        }
    }

    void AddBagBones(int count)
    {
        int slot = BoneBagSlot();
        if (slot < 0) return;
        status.Replace(slot, Items.BoneBagOf(Items.BonesIn(status[slot]) + count));
    }

    // Sac à Os dans l'inventaire : les Os de l'inventaire y passent (15 au plus).
    void AbsorbBones()
    {
        int slot = BoneBagSlot();
        if (slot < 0) return;
        for (int i = 0; i < GameRules.InventorySize && Items.BonesIn(status[slot]) < Items.BoneBagMax; i++)
            if (status[i] == ItemType.Os)
            {
                status.RemoveAt(i);
                AddBagBones(1);
            }
    }

    // ================= Réinitialisation, état envoyé aux nouveaux joueurs =================

    // Collier à Laisse : soumis -> maître.
    readonly Dictionary<string, string> collars = new Dictionary<string, string>();
    readonly Dictionary<string, List<Transform>> chains = new Dictionary<string, List<Transform>>();
    Transform chainsRoot;
    bool hubTwoItemsPlaced;

    // V0.7.11 (11.3, 11.4) : leviers des remparts actionnés, portes à clé ouvertes (Blockhaus).
    readonly HashSet<int> pulledLevers = new HashSet<int>(), unlockedDoors = new HashSet<int>();

    void ClearNewItems()
    {
        pulledLevers.Clear();
        unlockedDoors.Clear();
        ClearCollars();
        hubTwoItemsPlaced = false;
        OrbitCamera.MetronomeTilt = 0f;
        GameAudio.PeaceMode = false;
    }

    // Plus aucun Collier ni chaîne (fin de partie, retour au lobby).
    void ClearCollars()
    {
        collars.Clear();
        foreach (var list in chains.Values) foreach (var l in list) if (l != null) Destroy(l.gameObject);
        chains.Clear();
        if (player != null) player.Tether = null;
        if (avatar != null) avatar.SetCollar(false);
    }

    // V0.7.13 (Note 10) : un joueur qui quitte la partie (ou en est expulsé) ne porte plus de Collier et ne tient plus personne.
    void ForgetCollarsOf(string id)
    {
        foreach (var kv in new List<KeyValuePair<string, string>>(collars))
            if (kv.Key == id || kv.Value == id) collars.Remove(kv.Key);
    }

    void SendNewItemsSnapshot(string to)
    {
        foreach (var kv in collars) net.ToPeer(to, "CC|" + kv.Key + "|" + kv.Value);
        foreach (int id in pulledLevers) net.ToPeer(to, "LO|" + id);
        foreach (int id in unlockedDoors) net.ToPeer(to, "DO|" + id);
    }

    // ================= Chaque image =================

    void UpdateNewItems()
    {
        LocalBagBones = BagBones;
        UpdateMetronomeTilt();
        UpdatePeace();
        UpdateCollars();
        if (net.IsHost) UpdateHostNewItems();
    }

    // ---------- Métronome ----------

    float metronomeWeight;

    void UpdateMetronomeTilt()
    {
        bool watched = !status.Dead && net.InGame && MetronomeDevice.Watched(Camera.main);
        metronomeWeight = Mathf.MoveTowards(metronomeWeight, watched ? 1f : 0f, Time.deltaTime * 2f);
        OrbitCamera.MetronomeTilt = metronomeWeight * MetronomeDevice.HeadTilt * Mathf.Sin(MetronomeDevice.Beats * Mathf.PI);
    }

    // ---------- Ravin des Blockhaus (V0.7.12, Note 20) ----------

    // Tomber dans le ravin (sous le dernier étage, à -10 m depuis la V0.7.14) tue le joueur. V0.7.14 (Note 04) : le corps continue
    // de tomber jusqu'au fond du ravin (le personnage mort est figé : on le fait descendre à la main).
    float ravineFallSpeed = -1f;

    void UpdateRavineFall()
    {
        if (ravineFallSpeed >= 0f)
        {
            if (!status.Dead || !net.InGame) { ravineFallSpeed = -1f; return; }
            Vector3 at = player.transform.position;
            ravineFallSpeed = Mathf.Min(ravineFallSpeed + Physics.gravity.magnitude / (GameRules.JumpTimeScale * GameRules.JumpTimeScale) * Time.deltaTime, RavineMaxFallSpeed);
            float step = ravineFallSpeed * Time.deltaTime;
            if (Physics.Raycast(at + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, step + 1.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                player.transform.position = new Vector3(at.x, hit.point.y + 0.95f, at.z);
                ravineFallSpeed = -1f;
                return;
            }
            player.transform.position = at + Vector3.down * step;
            return;
        }
        if (status.Dead || !net.InGame || Teleporting || phase == Phase.GameOver) return;
        Vector3 p = player.transform.position;
        if (p.y > -WorldBuilder.BlockhausDepth - RavineKillDrop) return;
        int zone = WorldBuilder.ZoneAt(p);
        if (zone < 1 || zone > zones.Count || zones[zone - 1].ravineAt == null || !zones[zone - 1].ravineAt(p)) return;
        var body = player.GetComponent<Rigidbody>();
        float speed = body != null ? Mathf.Max(0f, -body.velocity.y) : 10f;
        KillLocal("le Ravin");
        ravineFallSpeed = speed;
    }

    const float RavineKillDrop = 3f, RavineMaxFallSpeed = 40f;

    // ---------- Mégaphone ----------

    // Onde de choc (chez chaque joueur) : le joueur local est repoussé s'il est dans le cône, les balles aussi.
    void MegaphoneWave(Transform barker, string barkerId)
    {
        Vector3 origin = barker.position, forward = barker.forward;
        forward.y = 0f;
        forward.Normalize();
        // V0.7.12 (Note 11) : l'onde de choc se voit, elle part du Mégaphone et file dans la direction de la voix.
        var who = barker.GetComponentInChildren<CharacterAvatar>();
        MegaphoneShockwave.Spawn(who != null ? who.MegaphoneMouth : origin + Vector3.up * 0.6f + forward * 0.5f, forward);
        bool InCone(Vector3 p)
        {
            Vector3 d = p - origin;
            d.y = 0f;
            return d.magnitude <= MegaphoneWaveRange && d.sqrMagnitude > 0.01f && Vector3.Angle(forward, d) <= MegaphoneWaveAngle;
        }
        float Strength(Vector3 p) => Mathf.Lerp(1f, 0.4f, Vector3.Distance(origin, p) / MegaphoneWaveRange);
        if (barkerId != net.MyId && !status.Dead && !Invincible && InCone(player.transform.position))
            player.Knockback(player.transform.position - origin, MegaphoneWaveSpeed * Strength(player.transform.position));
        foreach (var ball in SuperBall.All)
            if (ball != null && InCone(ball.transform.position))
            {
                Vector3 d = ball.transform.position - origin;
                d.y = 0f;
                ball.Push(d.normalized * MegaphoneWaveSpeed * Strength(ball.transform.position));
            }
        foreach (var ball in HubBall.All.Values)
            if (ball != null && InCone(ball.transform.position))
            {
                Vector3 d = ball.transform.position - origin;
                d.y = 0f;
                Vector3 v = d.normalized * MegaphoneWaveSpeed * Strength(ball.transform.position);
                ball.Apply(ball.transform.position.x, ball.transform.position.z, v.x, v.z);
            }
    }

    // Hôte : les entités de type "normale" dans le cône sont repoussées.
    void ShoveEntitiesFrom(string barkerId)
    {
        Transform barker = null;
        if (barkerId == net.MyId) barker = player.transform;
        else if (net.Remotes.TryGetValue(barkerId, out var rp) && rp != null) barker = rp.transform;
        if (barker == null) return;
        Vector3 forward = barker.forward;
        forward.y = 0f;
        foreach (var e in entities.Values)
        {
            if (e == null || e.Nightmare || e is TeteDeCloche) continue;
            Vector3 d = e.transform.position - barker.position;
            d.y = 0f;
            if (d.magnitude > MegaphoneWaveRange || d.sqrMagnitude < 0.01f || Vector3.Angle(forward, d) > MegaphoneWaveAngle) continue;
            e.Shove(d.normalized * MegaphoneWaveSpeed * Mathf.Lerp(1f, 0.4f, d.magnitude / MegaphoneWaveRange));
        }
    }

    // ---------- Super Balle ----------

    public void SendSuperBallKick(SuperBall ball) => net.ToHost("UB|" + ball.State);

    // La balle lancée vite percute le joueur local : 10 de dégâts et un violent recul.
    public void HitBySuperBall(SuperBall ball, Vector3 velocity)
    {
        if (status.Dead || Invincible || WorldBuilder.IsInHub(player.transform.position)) return;
        player.Knockback(velocity, SuperBallKnockback);
        DamageLocal(SuperBallDamage, "la Super Balle");
    }

    // Hôte : une balle lancée vite repousse violemment les entités de type "normale" qu'elle percute.
    readonly Dictionary<int, float> ballHits = new Dictionary<int, float>();

    void UpdateHostNewItems()
    {
        foreach (var ball in SuperBall.All)
        {
            if (ball == null || ball.Speed < SuperBall.FastSpeed) continue;
            foreach (var e in entities.Values)
            {
                if (e == null || e.Nightmare || e is TeteDeCloche) continue;
                Vector3 d = e.transform.position - ball.transform.position;
                d.y = 0f;
                if (d.magnitude > Beacon.SuperBallSize * 0.5f + 0.5f) continue;
                if (ballHits.TryGetValue(e.Id, out float at) && Time.time - at < SuperBall.HitDamageCooldown) continue;
                ballHits[e.Id] = Time.time;
                e.Shove((d.sqrMagnitude > 0.01f ? d.normalized : ball.Velocity.normalized) * SuperBallKnockback);
            }
        }
        PlaceHubTwoItems();
    }

    // ---------- Sac à Os et Notice du Hub II ----------

    // Hôte : à la génération du Hub II, la Notice et le Sac à Os sont posés au sol (une fois par partie).
    void PlaceHubTwoItems()
    {
        if (hubTwoItemsPlaced || worldLoading || hubs.Count < 2 || hubs[1] == null || hubs[1].buttonAnchor == null) return;
        hubTwoItemsPlaced = true;
        Vector3 center = hubs[1].center;
        // Près du Bouton Rouge (au centre), sur la piste ; la Notice un peu plus loin.
        PlaceDevice(Beacon.KindOf(Items.BoneBagOf(0)), center + new Vector3(1.6f, 0f, -0.8f), 200f, "");
        PlaceDevice(DeviceKind.Notice, center + new Vector3(-1.9f, 0f, -1.2f), 160f, "");
    }

    // ---------- Collier à Laisse et Sac à Os porté : cibles "joueur" ----------

    string MySub()
    {
        foreach (var kv in collars) if (kv.Value == net.MyId) return kv.Key;
        return null;
    }

    // Un autre joueur peut-il être visé (Collier à Laisse en main, ou un Os à glisser dans son Sac à Os) ?
    public bool CanTargetPlayer(RemotePlayer rp, out bool bag)
    {
        bag = false;
        if (rp == null || rp.Dead || status.Dead || !net.InGame) return false;
        ItemType selected = SelectedItem;
        if (selected == ItemType.Collier)
        {
            string sub = MySub();
            if (sub != null) return sub == rp.PlayerId;
            return !collars.ContainsKey(rp.PlayerId) && !collars.ContainsKey(net.MyId);
        }
        if (selected == ItemType.Os && rp.HasBoneBag && rp.BagBones < Items.BoneBagMax)
        {
            bag = true;
            return true;
        }
        return false;
    }

    public void TargetPlayer(RemotePlayer rp)
    {
        if (!CanTargetPlayer(rp, out bool bag)) return;
        Gesture(PlayerGesture.Use);
        if (bag)
        {
            int slot = status.Selected;
            if (slot < 0 || status[slot] != ItemType.Os) return;
            status.RemoveAt(slot);
            net.ToHost("SB|" + rp.PlayerId);
            return;
        }
        net.ToHost("CL|" + rp.PlayerId);
    }

    // Le maître qui n'a plus le Collier (vendu, posé, mort) libère son soumis.
    float collarCheck;

    void UpdateCollars()
    {
        string sub = MySub();
        collarCheck -= Time.deltaTime;
        if (sub != null && collarCheck <= 0f && (status.Dead || !status.Has(ItemType.Collier)))
        {
            collarCheck = 1f;
            net.ToHost("CL|" + sub);
        }
        // Soumis : laisse de 5 m vers le maître.
        player.Tether = null;
        if (collars.TryGetValue(net.MyId, out string master) && !status.Dead && net.Remotes.TryGetValue(master, out var mp) && mp != null && !mp.Dead)
        {
            player.Tether = mp.transform.position;
            player.TetherLength = CollarRange;
        }
        // Collier autour du cou des soumis, et chaînes du maître au soumis.
        if (avatar != null) avatar.SetCollar(collars.ContainsKey(net.MyId) && !status.Dead);
        foreach (var rp in net.Remotes.Values)
            if (rp != null && rp.Avatar != null) rp.Avatar.SetCollar(collars.ContainsKey(rp.PlayerId) && !rp.Dead);
        foreach (string id in new List<string>(chains.Keys))
            if (!collars.ContainsKey(id))
            {
                foreach (var l in chains[id]) if (l != null) Destroy(l.gameObject);
                chains.Remove(id);
            }
        foreach (var kv in collars) DrawChain(kv.Key, kv.Value);
    }

    CharacterAvatar AvatarOf(string id)
    {
        if (id == net.MyId) return avatar;
        return net.Remotes.TryGetValue(id, out var rp) && rp != null && !rp.Dead ? rp.Avatar : null;
    }

    // Chaîne de métal (maillons du Roi) de la main du maître au cou du soumis, qui pend un peu quand elle n'est pas tendue.
    void DrawChain(string subId, string masterId)
    {
        CharacterAvatar sub = AvatarOf(subId), master = AvatarOf(masterId);
        if (!chains.TryGetValue(subId, out var links)) chains[subId] = links = new List<Transform>();
        if (sub == null || master == null)
        {
            foreach (var l in links) if (l != null) l.gameObject.SetActive(false);
            return;
        }
        if (chainsRoot == null) chainsRoot = new GameObject("ChainesDeLaisse").transform;
        Vector3 a = master.HandPoint, b = sub.CollarPoint;
        float length = Vector3.Distance(a, b);
        int count = Mathf.Clamp(Mathf.CeilToInt(length / 0.16f), 2, 60);
        while (links.Count < count) links.Add(ChainLink.Create(chainsRoot));
        float sag = Mathf.Max(0f, CollarRange + 0.5f - length) * 0.18f;
        for (int k = 0; k < links.Count; k++)
        {
            bool on = k < count;
            if (links[k].gameObject.activeSelf != on) links[k].gameObject.SetActive(on);
            if (!on) continue;
            float t0 = (float)k / count, t1 = (float)(k + 1) / count;
            Vector3 p0 = Vector3.Lerp(a, b, t0) + Vector3.down * sag * 4f * t0 * (1f - t0);
            Vector3 p1 = Vector3.Lerp(a, b, t1) + Vector3.down * sag * 4f * t1 * (1f - t1);
            links[k].position = (p0 + p1) * 0.5f;
            Vector3 d = p1 - p0;
            links[k].rotation = (d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : Quaternion.identity) * Quaternion.Euler(0f, 0f, k % 2 == 0 ? 0f : 90f);
            links[k].localScale = Vector3.one * 0.6f;
        }
    }

    // ---------- Lunettes PEACE ----------

    ParticleSystem hearts;

    void UpdatePeace()
    {
        bool on = WearingGlasses && net.InGame;
        GameAudio.PeaceMode = on;
        if (hud != null) hud.SetPeace(on);
        if (!on) return;
        Vector3 me = player.transform.position;
        if (hearts == null) hearts = BuildPeaceParticles("CoeursRoses", PeaceTextures.Heart, 0.25f, 0.5f);
        // Des cœurs roses sortent régulièrement du sol, autour du joueur, et tourbillonnent légèrement.
        if (Random.value < Time.deltaTime * 6f)
        {
            Vector2 o = Random.insideUnitCircle * 7f;
            Vector3 p = me + new Vector3(o.x, 1.5f, o.y);
            if (Physics.Raycast(p, Vector3.down, out RaycastHit hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) p = hit.point;
            hearts.Emit(new ParticleSystem.EmitParams
            {
                position = p + Vector3.up * 0.05f,
                velocity = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.6f, 1.1f), Random.Range(-0.2f, 0.2f)),
                startSize = Random.Range(0.2f, 0.4f), startLifetime = Random.Range(2.2f, 3.2f), startColor = new Color(1f, 0.55f, 0.78f, 0.9f),
            }, 1);
        }
        // V0.7.13 (Note 08) : plus d'arcs-en-ciel dans le décor ; des bordures arc-en-ciel en haut et en bas de l'écran (GameHud).
    }

    ParticleSystem BuildPeaceParticles(string name, Texture2D texture, float minSize, float maxSize)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        color.color = g;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f));
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        // Les trois courbes orbitales doivent être dans le même mode (deux constantes).
        velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { name = name, mainTexture = texture };
        ps.Play();
        return ps;
    }

    // ---------- Journaux ----------

    // Journal de la manche (Zone 1, manches 1 à 5) : un autre à chaque manche, dans un ordre tiré d'après la graine.
    public ItemType RoundJournal()
    {
        if (round < 1 || round > Journals.Rounds) return ItemType.None;
        var order = new List<int> { 0, 1, 2, 3, 4 };
        var rnd = new System.Random(seed * 131 + 977);
        for (int k = order.Count - 1; k > 0; k--) { int r = rnd.Next(k + 1); (order[k], order[r]) = (order[r], order[k]); }
        return Items.JournalOf(order[(round - 1) % order.Count]);
    }

    // ================= Réception =================

    // Note 01 : obnubilé par une Cloche (entité) ou une Tête de Cloche.
    bool ObsessedByEntity()
    {
        foreach (var e in entities.Values)
            if (e is Cloche c && c.Obsesses(net.MyId) || e is TeteDeCloche t && t.Obsesses(net.MyId)) return true;
        return false;
    }

    bool HandleNewItemsMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            // Note 01 : ce joueur n'est plus obnubilé (deux aboiements d'un autre joueur).
            case "OR" when host:
                foreach (var e in entities.Values)
                {
                    if (e is Cloche c) c.Release(from);
                    else if (e is TeteDeCloche t) t.Release(from);
                }
                return true;
            // Collier à Laisse : passer (ou retirer) le collier.
            case "CL" when host && f.Length >= 2:
            {
                string target = f[1];
                if (collars.TryGetValue(target, out string master))
                {
                    if (master == from) net.ToAll("CC|" + target + "|");
                    return true;
                }
                bool busy = collars.ContainsKey(from);
                foreach (var kv in collars) if (kv.Value == from) busy = true;
                if (!busy && target != from && net.Roster.ContainsKey(target)) net.ToAll("CC|" + target + "|" + from);
                return true;
            }
            case "LV" when host && f.Length >= 2:
                if (pulledLevers.Add(NetworkGame.I(f[1]))) net.ToAll("LO|" + f[1]);
                return true;
            case "LO" when f.Length >= 2:
                pulledLevers.Add(NetworkGame.I(f[1]));
                UpdateDoors(true);
                return true;
            case "DU" when host && f.Length >= 2:
                if (unlockedDoors.Add(NetworkGame.I(f[1]))) net.ToAll("DO|" + f[1]);
                return true;
            case "DO" when f.Length >= 2:
                unlockedDoors.Add(NetworkGame.I(f[1]));
                UpdateDoors(true);
                return true;
            case "CC" when f.Length >= 3:
                if (f[2].Length == 0) collars.Remove(f[1]);
                else collars[f[1]] = f[2];
                return true;
            // Os glissé dans le Sac à Os d'un autre joueur.
            case "SB" when host && f.Length >= 2:
                if (f[1] == net.MyId) GiveBagBone();
                else net.ToPeer(f[1], "SG|" + from);
                return true;
            case "SG":
                GiveBagBone();
                return true;
            // Super Balle poussée par un joueur : chacun rejoue la même trajectoire.
            case "UB" when host && f.Length >= 7:
                net.ToAll("UL|" + from + "|" + string.Join("|", f, 1, 6));
                return true;
            case "UL" when f.Length >= 8:
                if (f[1] == net.MyId) return true;
                foreach (var ball in SuperBall.All)
                    if (ball != null && ball.Id == NetworkGame.I(f[2]))
                        ball.Apply(new Vector3(NetworkGame.P(f[3]), NetworkGame.P(f[4]), NetworkGame.P(f[5])), new Vector3(NetworkGame.P(f[6]), 0f, NetworkGame.P(f[7])), f[1]);
                return true;
        }
        return false;
    }

    // ---------- Leviers du Château, portes à clé des Blockhaus ----------

    public void PullLever(CastleLever lever)
    {
        if (lever == null || lever.Pulled || status.Dead) return;
        Gesture(PlayerGesture.Use);
        net.ToHost("LV|" + lever.Id);
    }

    // La Clé en main ouvre le cadenas et disparaît ; en godmod (hôte), un clic suffit, sans Clé.
    public void UnlockDoor(ApartmentDoor door)
    {
        if (door == null || !door.Locked || status.Dead) return;
        int slot = status.Selected;
        bool key = slot >= 0 && status[slot] == ItemType.Cle;
        if (!key && !Godmode) return;
        if (key) status.RemoveAt(slot);
        Gesture(PlayerGesture.Use);
        net.ToHost("DU|" + door.Id);
    }

    // Herses et portes selon les leviers actionnés et les cadenas ouverts (UpdateDoors).
    void UpdateCastleDoors(WorldBuilder.ZoneRefs z, bool animate)
    {
        foreach (var lever in z.levers)
        {
            if (lever == null) continue;
            bool on = pulledLevers.Contains(lever.Id);
            if (on != lever.Pulled) lever.SetPulled(on, animate);
            if (z.leverGates.TryGetValue(lever.Id, out var gates))
                foreach (var g in gates) if (g != null && g.IsOpen != on) g.SetOpen(on, !animate);
        }
        foreach (var door in z.lockedDoors)
            if (door != null && door.Locked && unlockedDoors.Contains(door.Id)) door.Unlock(animate);
    }

    void GiveBagBone()
    {
        if (status.Dead || BoneBagSlot() < 0 || BagBones >= Items.BoneBagMax) return;
        AddBagBones(1);
        GameAudio.Play2D("pop", 0.5f, 0.05f, 1.2f);
    }
}

// Images des Lunettes PEACE : cœur rose et arc-en-ciel, générés une fois.
public static class PeaceTextures
{
    static Texture2D heart, rainbow;

    public static Texture2D Heart
    {
        get
        {
            if (heart != null) return heart;
            const int n = 64;
            heart = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Coeur", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2.4f - 1.2f, v = (y + 0.5f) / n * 2.4f - 1.0f;
                    // Courbe du cœur : (u² + v² - 1)³ - u² v³ <= 0.
                    float a = u * u + v * v - 1f;
                    bool inside = a * a * a - u * u * v * v * v <= 0f;
                    px[y * n + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            heart.SetPixels32(px);
            heart.Apply();
            return heart;
        }
    }

    public static Texture2D Rainbow
    {
        get
        {
            if (rainbow != null) return rainbow;
            const int w = 128, h = 64;
            rainbow = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "ArcEnCiel", wrapMode = TextureWrapMode.Clamp };
            Color[] bands = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.65f, 0.2f), new Color(1f, 0.95f, 0.35f), new Color(0.4f, 1f, 0.45f), new Color(0.35f, 0.65f, 1f), new Color(0.75f, 0.4f, 1f) };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                    float r = Mathf.Sqrt(u * u + v * v);
                    if (r > 0.95f || r < 0.55f) { px[y * w + x] = Color.clear; continue; }
                    float k = Mathf.InverseLerp(0.95f, 0.55f, r);
                    Color c = bands[Mathf.Clamp((int)(k * bands.Length), 0, bands.Length - 1)];
                    c.a = 0.8f;
                    px[y * w + x] = c;
                }
            rainbow.SetPixels(px);
            rainbow.Apply();
            return rainbow;
        }
    }
}

// Cible d'interaction portée par chaque autre joueur (V0.7.11) : visée avec le Collier à Laisse en main (à 5 m ou moins), ou avec
// un Os en main quand il porte un Sac à Os (le contour ne prend alors que le sac). Note 10 des Notes Diverses : pas de message.
public class PlayerTarget : Interactable
{
    RemotePlayer owner;
    Collider zone;
    bool bagMode;

    public static void Attach(RemotePlayer rp, float height)
    {
        var t = rp.gameObject.AddComponent<PlayerTarget>();
        t.owner = rp;
        // Le personnage est sur le calque Ignore Raycast : la zone visée est un enfant sur le calque par défaut, un déclencheur,
        // actif seulement quand le joueur local peut viser ce personnage.
        var go = new GameObject("CibleJoueur");
        go.transform.SetParent(rp.transform, false);
        go.layer = 0;
        var c = go.AddComponent<CapsuleCollider>();
        c.isTrigger = true;
        c.height = height;
        c.radius = 0.45f;
        c.enabled = false;
        t.zone = c;
    }

    void Update()
    {
        var session = GameSession.Instance;
        bool on = session != null && session.CanTargetPlayer(owner, out bagMode);
        if (zone != null && zone.enabled != on) zone.enabled = on;
    }

    public override string Prompt => "";
    public override float Range => bagMode ? GameRules.InteractRange : GameSession.CollarRange;
    public override bool CanInteract => GameSession.Instance != null && GameSession.Instance.CanTargetPlayer(owner, out bagMode);
    public override void Interact() => GameSession.Instance.TargetPlayer(owner);

    protected override bool HasOutline(Renderer r)
    {
        if (r.GetComponent<TextMesh>() != null) return false;
        if (!bagMode) return true;
        for (Transform t = r.transform; t != null && t != transform; t = t.parent) if (t.name == "SacOs") return true;
        return false;
    }
}

// V0.7.12 (Note 11) : anneaux translucides qui sortent du pavillon du Mégaphone et s'élargissent en avançant (le cône de l'onde de
// choc), puis s'effacent.
public class MegaphoneShockwave : MonoBehaviour
{
    const float Duration = 0.55f, Rings = 3, RingDelay = 0.09f;
    static Mesh ring;
    static Material material;

    Vector3 origin, direction;
    readonly List<Transform> rings = new List<Transform>();
    readonly List<Material> mats = new List<Material>();
    float age;

    public static void Spawn(Vector3 origin, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;
        if (ring == null) ring = ItemPickup.Torus(1f, 0.06f, 32, 6);
        if (material == null) material = new Material(Shader.Find("Sprites/Default")) { name = "OndeDeChoc" };
        var go = new GameObject("OndeDeChoc");
        var w = go.AddComponent<MegaphoneShockwave>();
        w.origin = origin;
        w.direction = direction.normalized;
        for (int k = 0; k < Rings; k++)
        {
            var r = GameArt.MeshObject("Anneau", go.transform, ring, material);
            var mat = new Material(material);
            r.GetComponent<Renderer>().sharedMaterial = mat;
            r.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var c in r.GetComponentsInChildren<Collider>()) Destroy(c);
            r.SetActive(false);
            w.rings.Add(r.transform);
            w.mats.Add(mat);
        }
        Destroy(go, Duration + Rings * RingDelay + 0.1f);
    }

    void Update()
    {
        age += Time.deltaTime;
        float range = GameSession.MegaphoneWaveRange * 0.6f, spread = Mathf.Tan(GameSession.MegaphoneWaveAngle * Mathf.Deg2Rad);
        for (int k = 0; k < rings.Count; k++)
        {
            float t = (age - k * RingDelay) / Duration;
            bool on = t > 0f && t < 1f;
            if (rings[k].gameObject.activeSelf != on) rings[k].gameObject.SetActive(on);
            if (!on) continue;
            float e = 1f - (1f - t) * (1f - t);
            float d = e * range;
            float radius = 0.15f + d * spread;
            // Le tore est dans le plan XY (axe Z) : face à la direction de la voix.
            rings[k].SetPositionAndRotation(origin + direction * d, Quaternion.LookRotation(direction));
            rings[k].localScale = new Vector3(radius, radius, 1f);
            // V0.7.13 (Note 06) : onde moins visible (opacité 0,55 -> 0,25).
            mats[k].color = new Color(1f, 1f, 1f, 0.25f * (1f - t));
        }
    }

    void OnDestroy()
    {
        foreach (var m in mats) if (m != null) Destroy(m);
    }
}
