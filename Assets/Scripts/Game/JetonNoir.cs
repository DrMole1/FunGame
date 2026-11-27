using System.Collections.Generic;
using UnityEngine;

// Jeton Noir (GDD 8.11, V0.7.22 ; ID 11, type Piège, 50 de dégâts) : à partir de la Zone 2, hors des structures et des ravins, les tours,
// fous et dames noirs décoratifs de l'Échiquier (et des salles uniques) sont des Jetons Noirs, de même apparence que les pièces de la
// Zone 1, jamais grossis ni accrochés au plafond. Posés au centre d'une case du damier.
// Une Balise posée sur un Jeton Noir : il gonfle un instant et la Balise éclate. Une Balise posée dans son axe (tour : lignes droites
// X et Z ; fou : diagonales ; dame : les deux), au même niveau, sur un sol plat et libre jusqu'à elle (hors joueurs et objets posés) :
// il fonce dessus (vitesse du joueur x 3,2) et la fait éclater à l'arrivée. Ce composant ne fait que montrer le Jeton (déplacement,
// gonflement) ; les règles sont tenues par l'hôte (GameSession.Jetons).
public class JetonNoir : MonoBehaviour
{
    public enum Moves { Straight, Diagonal, Both }

    public static readonly Dictionary<int, JetonNoir> All = new Dictionary<int, JetonNoir>();

    public int Id { get; private set; }
    public int Zone { get; private set; }
    public Moves Rule { get; private set; }
    public bool Moving { get; private set; }
    // Numéro du déplacement en cours (chaque joueur n'est touché qu'une fois par déplacement).
    public int MoveSerial { get; private set; }
    public Vector3 Target { get; private set; }
    public int TargetBeacon { get; private set; } = -1;
    // Déjà déplacé depuis la génération (position envoyée aux joueurs qui arrivent).
    public bool Displaced { get; private set; }
    // Rayon et hauteur de la pièce (taille d'un personnage).
    public const float Radius = 0.34f, Height = 1.8f;
    public Vector3 Base => transform.position;

    float bumpClock = -1f;
    Vector3 baseScale;

    public void Init(int id, int zone, string model)
    {
        Id = id;
        Zone = zone;
        Rule = model == "Rook" ? Moves.Straight : model == "Bishop" ? Moves.Diagonal : Moves.Both;
        baseScale = transform.localScale;
        All[id] = this;
    }

    void OnDestroy()
    {
        if (All.TryGetValue(Id, out var j) && j == this) All.Remove(Id);
    }

    public static bool AnyMoving()
    {
        foreach (var j in All.Values) if (j != null && j.Moving) return true;
        return false;
    }

    // Vitesse : celle du joueur x 3,2.
    public static float Speed => GameRules.BaseSpeed * GameRules.JetonNoirSpeedFactor;

    public void StartMove(Vector3 target, int beacon)
    {
        Target = new Vector3(target.x, transform.position.y, target.z);
        TargetBeacon = beacon;
        Moving = true;
        Displaced = true;
        MoveSerial++;
        GameAudio.Play3D("slide_rub", transform.position, 0.9f, 25f, 0.1f, 0.7f);
    }

    public void StopAt(Vector3 at)
    {
        Moving = false;
        TargetBeacon = -1;
        Displaced = true;
        transform.position = at;
    }

    // Une Balise posée dessus : il grossit légèrement et retrouve son volume, très vite.
    public void Bump()
    {
        bumpClock = 0f;
        GameAudio.Play3D("tour_impact", transform.position + Vector3.up, 0.5f, 18f, 0.1f, 1.4f);
    }

    // Arrivé au bout de son déplacement (chez chacun) ; l'hôte confirme la position finale (message QS).
    public bool Arrived { get; private set; }

    void Update()
    {
        if (bumpClock >= 0f)
        {
            bumpClock += Time.deltaTime;
            float k = Mathf.Clamp01(bumpClock / BumpTime);
            transform.localScale = baseScale * (1f + BumpGrow * Mathf.Sin(k * Mathf.PI));
            if (k >= 1f) { bumpClock = -1f; transform.localScale = baseScale; }
        }
        Arrived = false;
        if (!Moving) return;
        Vector3 p = Vector3.MoveTowards(transform.position, Target, Speed * Time.deltaTime);
        transform.position = p;
        if ((p - Target).sqrMagnitude < 1e-4f)
        {
            Moving = false;
            Arrived = true;
            GameAudio.Play3D("tour_impact", p + Vector3.up * 0.5f, 0.7f, 25f, 0.1f, 0.9f);
        }
    }

    const float BumpTime = 0.22f, BumpGrow = 0.12f;

    // ---------- Éclats ----------

    // La Balise éclate : ses morceaux (blancs, comme le pion) tombent à proximité et disparaissent progressivement. 'dark' : éclats
    // sombres (objet qui explose).
    public static void Shatter(Vector3 at, bool dark)
    {
        Material mat = dark ? GameArt.Lit("EclatsSombres", new Color(0.12f, 0.12f, 0.12f), null, 0.3f)
                            : GameArt.Lit("EclatsBalise", new Color(0.93f, 0.93f, 0.9f), null, 0.8f);
        int count = dark ? 14 : 11;
        for (int k = 0; k < count; k++)
        {
            float size = Random.Range(0.05f, 0.13f);
            var piece = GameArt.Prim(PrimitiveType.Cube, null, at + Vector3.up * Random.Range(0.1f, 0.5f) + Random.insideUnitSphere * 0.15f,
                new Vector3(size, size * Random.Range(0.5f, 1.2f), size * Random.Range(0.6f, 1.3f)), mat, true);
            piece.name = "Eclat";
            piece.transform.rotation = Random.rotation;
            var rb = piece.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.velocity = new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(1.2f, 3f), Random.Range(-1.6f, 1.6f));
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            // Les éclats ne gênent personne.
            piece.layer = 2;
            piece.AddComponent<FadeAway>().Init(Random.Range(2.2f, 3.4f));
        }
    }

    // Rapetisse puis disparaît.
    class FadeAway : MonoBehaviour
    {
        float life, clock;
        Vector3 size;

        public void Init(float seconds)
        {
            life = seconds;
            size = transform.localScale;
        }

        void Update()
        {
            clock += Time.deltaTime;
            float fade = Mathf.Clamp01((clock - life * 0.5f) / (life * 0.5f));
            transform.localScale = size * (1f - fade);
            if (clock >= life) Destroy(gameObject);
        }
    }
}
