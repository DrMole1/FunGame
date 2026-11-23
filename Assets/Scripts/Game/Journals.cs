using UnityEngine;

// V0.7.11 (GDD 17.5) : vieux Journaux J1 à J5. Durant les 5 premières manches, un monticule simple de plus, dans la zone 1,
// contient un Journal tiré au hasard (un autre à chaque manche) ; il se vend 15 croquettes à Pogo. Sélectionné dans
// l'inventaire, le personnage le tient entre ses mains ; à la première personne, on lit le texte, écrit à la main dans un noir
// d'encre, traduit dans la langue du joueur. Le texte est coupé en lignes courtes pour ne jamais dépasser de la feuille.
public static class Journals
{
    public const int Count = 5;
    public const int Rounds = 5;
    // Caractères au plus par ligne (la taille du texte est ensuite ajustée à la plus longue ligne et au nombre de lignes).
    const int LineChars = 30;

    // Date, puis l'extrait (français, anglais, espagnol, russe : ordre de l'enum Lang = En, Fr, Es, Ru).
    static readonly string[][] Texts =
    {
        new[]
        {
            "December 1962\n\n…high-ranking officials of the world's governments, including the American Collaborative Unity Party and the German National Socialist Workers' Party, met in Novgorod, USSR, to seal an unprecedented worldwide Alliance. Indeed…",
            "Décembre 1962\n\n…hauts dignitaires des gouvernements mondiaux, dont le Parti Unitaire Collaboratif américain et le Parti National Socialiste des Travailleurs allemands, se sont retrouvés à Novgorod, URSS, pour conclure une Alliance mondiale sans précédent. En effet…",
            "Diciembre de 1962\n\n…altos dignatarios de los gobiernos del mundo, entre ellos el Partido Unitario Colaborativo estadounidense y el Partido Nacional Socialista de los Trabajadores alemán, se reunieron en Nóvgorod, URSS, para sellar una Alianza mundial sin precedentes. En efecto…",
            "Декабрь 1962\n\n…высокопоставленные представители правительств мира, в том числе Американской объединённой партии сотрудничества и Национал-социалистической рабочей партии Германии, собрались в Новгороде, СССР, чтобы заключить беспрецедентный всемирный Альянс. Действительно…",
        },
        new[]
        {
            "October 1983\n\n…W.O.O.F., of American origin, is reviving the metallurgy industry in deepest Siberia. The investment, worth several billion, should allow…",
            "Octobre 1983\n\n… W.O.O.F., d'origine américaine, relance l'industrie de la métallurgie dans la Sibérie profonde. L'investissement, à hauteur de plusieurs milliards, devrait permettre…",
            "Octubre de 1983\n\n…W.O.O.F., de origen estadounidense, relanza la industria metalúrgica en lo más profundo de Siberia. La inversión, de varios miles de millones, debería permitir…",
            "Октябрь 1983\n\n…американская компания W.O.O.F. возрождает металлургическую промышленность в глубине Сибири. Инвестиции в несколько миллиардов должны позволить…",
        },
        new[]
        {
            "May 1982\n\n…victory of the prodigy Sergei Yaroslav Makarov, who becomes the reigning chess champion! The engineer and architect snatches the win with a single pawn, earning him the nickname \"Black Pawn\". Internationally…",
            "Mai 1982\n\n…victoire du prodige Sergei Yaroslav Makarov, devenant champion d'échecs en titre ! L'ingénieur et architecte arrache la victoire, avec seulement un pion, lui valant le surnom \"Black Pawn\". A l'international…",
            "Mayo de 1982\n\n…¡victoria del prodigio Sergei Yaroslav Makarov, que se convierte en el campeón de ajedrez en título! El ingeniero y arquitecto arranca la victoria con un solo peón, lo que le vale el apodo de \"Black Pawn\". A nivel internacional…",
            "Май 1982\n\n…победа вундеркинда Сергея Ярославовича Макарова, ставшего действующим чемпионом по шахматам! Инженер и архитектор вырвал победу всего одной пешкой, за что получил прозвище «Black Pawn». На международной арене…",
        },
        new[]
        {
            "April 1978\n\n…psychologist Leonid Ignatyi Vosiliev receives the Kremlin's honours for his various works. A Party member for several years, Vosiliev gave a speech…",
            "Avril 1978\n\n…psychologue Leonid Ignatyi Vosiliev reçoit les honneurs du Kremlin pour ses différents travaux. Membre du parti depuis plusieurs années, Vosiliev a prononcé un discours…",
            "Abril de 1978\n\n…el psicólogo Leonid Ignatyi Vosiliev recibe los honores del Kremlin por sus diversos trabajos. Miembro del partido desde hace varios años, Vosiliev pronunció un discurso…",
            "Апрель 1978\n\n…психолог Леонид Игнатий Восильев удостоен почестей Кремля за свои многочисленные работы. Член партии на протяжении нескольких лет, Восильев выступил с речью…",
        },
        new[]
        {
            "November 1984\n\n…of the extremist and terrorist group \"Liberation Front\" were sent to the Kolyma site this morning. The team of researchers overseeing the W.O.O.F. branch, headed by the psychologist V…",
            "Novembre 1984\n\n… du groupuscule extrémiste et terroriste \"Front Libérateur\" ont été envoyés sur le site de Kolyma, ce matin. L'équipe de chercheurs supervisant la succursale W.O.O.F., présidée par le psychologue V…",
            "Noviembre de 1984\n\n…del grupúsculo extremista y terrorista \"Frente Liberador\" fueron enviados esta mañana al sitio de Kolymá. El equipo de investigadores que supervisa la sucursal W.O.O.F., presidido por el psicólogo V…",
            "Ноябрь 1984\n\n…экстремистской и террористической группировки «Освободительный фронт» были отправлены сегодня утром на объект в Колыме. Группа исследователей, курирующая филиал W.O.O.F., под руководством психолога В…",
        },
    };

    public static string Text(int index)
    {
        string[] row = Texts[Mathf.Clamp(index, 0, Count - 1)];
        return Wrap(row[Mathf.Clamp((int)Loc.Current, 0, row.Length - 1)]);
    }

    // Coupe chaque paragraphe en lignes d'au plus LineChars caractères, entre les mots.
    static string Wrap(string text)
    {
        var sb = new System.Text.StringBuilder();
        string[] paragraphs = text.Split('\n');
        for (int p = 0; p < paragraphs.Length; p++)
        {
            if (p > 0) sb.Append('\n');
            int lineLength = 0;
            foreach (string word in paragraphs[p].Split(' '))
            {
                if (word.Length == 0) continue;
                if (lineLength > 0 && lineLength + 1 + word.Length > LineChars)
                {
                    sb.Append('\n');
                    lineLength = 0;
                }
                else if (lineLength > 0)
                {
                    sb.Append(' ');
                    lineLength++;
                }
                sb.Append(word);
                lineLength += word.Length;
            }
        }
        return sb.ToString();
    }
}
