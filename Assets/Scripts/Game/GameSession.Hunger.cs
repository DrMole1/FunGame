using UnityEngine;

// V0.7.2 (chapitre 3.5) : la Faim. Chaque joueur commence avec 100 de faim ; dès que le Bouton Rouge est appuyé, il en perd
// 1 toutes les 8 secondes (pas pendant qu'il est mort). À 0, il perd 15 PV par seconde (non lissé, avec l'animation de
// dégâts), même dans un Hub, jusqu'à ce qu'il mange. Mort, il revient à la vie avec la faim qu'il avait (au moins 1).
// À 99 de faim ou moins, la gamelle d'un Hub devient interactive : un clic rend 20 de faim (moins s'il n'a pas si faim)
// contre autant de croquettes prises dans la gamelle commune (rien n'est écrit sur l'Écran des Logs). Il mange 0,8 seconde,
// baissé vers la gamelle, après un aboiement ; il ne peut ni bouger, ni sauter, ni danser, ni poser ou ramasser d'objet.
// Messages : EA|faim -> EK|faim (0 : refusé, gamelle vide)    joueur -> hôte -> joueur : se nourrir à la gamelle
public partial class GameSession
{
    float hungerTimer, starveTimer, eatUntil = -1f;
    bool eatPending;

    public bool Eating => Time.time < eatUntil;
    // La gamelle n'est interactive qu'à 99 de faim ou moins (et pas pendant un repas).
    public bool CanEat => net != null && net.InGame && !status.Dead && status.Hunger < GameRules.MaxHunger && !Eating && !eatPending && phase != Phase.GameOver;

    void UpdateHunger()
    {
        if (phase == Phase.Running && !status.Dead && !WipedOut)
        {
            hungerTimer += Time.deltaTime;
            if (hungerTimer >= GameRules.HungerTick)
            {
                hungerTimer -= GameRules.HungerTick;
                status.SetHunger(status.Hunger - 1);
            }
        }
        // Affamé : -15 PV par seconde, d'un bloc, comme à 0 de sérénité.
        if (status.Dead || status.Hunger > 0 || Teleporting || CannonFlying || Sliding || InKennel)
        {
            starveTimer = 0f;
            return;
        }
        starveTimer += Time.deltaTime;
        if (starveTimer < 1f) return;
        starveTimer -= 1f;
        // La faim blesse partout, Hub compris.
        DamageLocal(GameRules.StarveDamagePerSecond, "la Faim", null, true);
    }

    // Nouvelle partie : faim pleine.
    void ResetHunger()
    {
        hungerTimer = starveTimer = 0f;
        eatUntil = -1f;
        eatPending = false;
        status.SetHunger(GameRules.MaxHunger);
        // Nouvelle partie : les sons d'ambiance uniques (statue, entrée dans le labyrinthe) pourront se rejouer.
        ResetAmbienceCues();
    }

    // Clic sur la gamelle d'un Hub.
    public void Eat()
    {
        if (!CanEat) return;
        int wanted = Mathf.Min(GameRules.MealHunger, GameRules.MaxHunger - status.Hunger);
        if (wanted <= 0) return;
        eatPending = true;
        net.ToHost("EA|" + wanted);
    }

    // Repas accordé : 0,8 seconde baissé vers la gamelle, un aboiement au début.
    void StartEating(int gained)
    {
        eatPending = false;
        if (gained <= 0)
        {
            hud.Toast(Loc.T("La gamelle est vide !"));
            return;
        }
        status.SetHunger(status.Hunger + gained);
        if (status.Dead) return;
        eatUntil = Time.time + GameRules.EatDuration;
        Bark();
        Gesture(PlayerGesture.Eat);
    }

    bool HandleHungerMessage(string from, string[] f, bool host)
    {
        switch (f[0])
        {
            case "EA" when host && f.Length >= 2:
            {
                // Autant de croquettes que de points de faim regagnés (20 au plus) ; moins s'il en reste moins dans la gamelle.
                int wanted = Mathf.Clamp(NetworkGame.I(f[1]), 0, GameRules.MealHunger);
                int given = phase == Phase.GameOver ? 0 : Mathf.Min(wanted, cash);
                if (given > 0)
                {
                    cash -= given;
                    BroadcastState();
                }
                net.ToPeer(from, "EK|" + given);
                return true;
            }
            case "EK" when f.Length >= 2:
                StartEating(NetworkGame.I(f[1]));
                return true;
        }
        return false;
    }
}
