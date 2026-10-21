using UnityEngine;

// Monticule de terre (V0.4.6, note 02 du chapitre 7) : les objets de valeur n'apparaissent plus à l'air libre. Un joueur
// qui le vise de près et clique (ou E) le creuse pendant 1,2 seconde, en projetant de la terre autour de lui ; le monticule
// libère alors l'objet tiré au sort à la génération (Os 90 %, Canard en Plastique 10 %), à ramasser.
public class Mound : Interactable
{
    public int Id { get; private set; }
    public ItemType Loot { get; private set; }
    public bool Digging { get; private set; }

    float debrisTimer;

    public static Mound Create(Transform parent, int id, ItemType loot, Vector3 pos, float yaw)
    {
        var go = new GameObject("Monticule_" + id);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        if (GameArt.SpawnModel("Monticule", go.transform, 0.4f) == null)
        {
            Material dirt = GameArt.Lit("Monticule", new Color(0.26f, 0.17f, 0.1f), GameArt.Noise("concrete", 128, 0.75f, 0.2f, 11), 0.05f);
            GameArt.Prim(PrimitiveType.Sphere, go.transform, new Vector3(0f, 0.05f, 0f), new Vector3(1.1f, 0.4f, 0.9f), dirt);
        }
        var mound = go.AddComponent<Mound>();
        mound.Id = id;
        mound.Loot = loot;
        mound.FitTrigger(new Vector3(0.9f, 0.5f, 0.9f));
        return mound;
    }

    public override string Prompt => Digging ? "" : "Creuser le monticule";
    public override bool CanInteract => !Digging;
    public override void Interact() => GameSession.Instance.RequestDig(this);

    // Quelqu'un creuse : de la terre gicle autour pendant 1,2 seconde.
    public void StartDigging()
    {
        if (Digging) return;
        Digging = true;
        debrisTimer = 0f;
        GameAudio.Play3D("dig", transform.position, 0.8f, 18f, 0.1f);
    }

    void Update()
    {
        if (!Digging) return;
        debrisTimer -= Time.deltaTime;
        if (debrisTimer > 0f) return;
        debrisTimer = 0.28f;
        Fx.Burst(transform.position + Vector3.up * 0.2f, Fx.Dirt, 10, 3.2f, 0.08f, 0.9f);
    }

    // Creusé : une dernière gerbe de terre, et le monticule disparaît.
    public void Open()
    {
        Fx.Burst(transform.position + Vector3.up * 0.2f, Fx.Dirt, 24, 4.2f, 0.1f);
        Destroy(gameObject);
    }
}
