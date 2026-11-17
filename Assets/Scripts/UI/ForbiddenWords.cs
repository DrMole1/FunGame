using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Mots interdits (GDD 15.2, V0.7.10) : la saisie d'un Post-It ou d'un pseudo est refusée si elle contient un mot offensant, en
// français ou dans sa traduction anglaise, russe ou espagnole. Le test ne tient compte ni des majuscules ni des accents, et bloque
// aussi un mot entouré d'autres caractères (recherche dans tout le texte).
public static class ForbiddenWords
{
    // Liste du GDD (français).
    static readonly string[] French =
    {
        "putain", "merde", "connard", "enculé", "salope", "bâtard", "fdp", "foutre", "bite", "couille", "chatte", "queue", "niquer",
        "baiser", "branleur", "branler", "sucer", "suce", "nique", "bordel", "con", "connasse", "trouduc", "enfoiré", "gueule",
        "crétin", "imbécile", "débile", "nazi", "nègre", "bougnoule", "youpin", "pédé", "tarlouze", "goudou", "shemale",
        "négro", "salopard", "fumier", "ordure", "chiant", "emmerdeur", "cul", "couillon", "pisser", "chier", "suicide", "pute",
        "pouffiasse", "garce", "enflure", "abruti", "gogol", "taré", "salaud", "merdeux", "pétasse", "morue", "bâtarde",
        "tapette", "lopette", "sperme", "crevard", "pourriture", "encule", "sodomie", "fellation", "masturbation", "porno",
        "raciste", "xénophobe", "pédophile", "viol", "hitler", "vagin", "mussolini", "staline", "kkk",
    };

    // Traductions anglaises (sens vulgaire ou injurieux du mot français).
    static readonly string[] English =
    {
        "whore", "fuck", "shit", "asshole", "fucker", "motherfucker", "slut", "bitch", "bastard", "son of a bitch", "cum", "dick",
        "cock", "bollocks", "pussy", "screw", "wanker", "jerk off", "suck", "brothel", "dumbass", "shut up", "cretin",
        "imbecile", "moron", "idiot", "nigger", "nigga", "negro", "raghead", "kike", "faggot", "fag", "dyke", "scumbag", "scum",
        "pain in the ass", "ass", "piss", "suicide", "skank", "retard", "little shit", "sissy", "sperm", "deadbeat", "sodomy",
        "blowjob", "fellatio", "masturbation", "porn", "racist", "xenophobe", "pedophile", "paedophile", "rape", "vagina", "stalin",
    };

    // Traductions espagnoles.
    static readonly string[] Spanish =
    {
        "puta", "mierda", "gilipollas", "cabrón", "hijo de puta", "zorra", "bastardo", "bastarda", "semen", "polla", "cojón", "coño",
        "verga", "follar", "joder", "pajero", "pajear", "chupar", "chupa", "burdel", "pendejo", "culero", "cállate", "cretino",
        "imbécil", "subnormal", "nazi", "negrata", "moraco", "judío de mierda", "maricón", "marica", "tortillera", "travelo",
        "canalla", "escoria", "coñazo", "tocapelotas", "culo", "mear", "cagar", "suicidio", "guarra", "idiota", "retrasado",
        "tarado", "mierdoso", "furcia", "nenaza", "esperma", "muerto de hambre", "sodomía", "mamada", "felación",
        "masturbación", "racista", "xenófobo", "pedófilo", "violación", "vagina", "stalin",
    };

    // Traductions russes.
    static readonly string[] Russian =
    {
        "блядь", "бля", "дерьмо", "говно", "мудак", "пидорас", "шлюха", "сука", "ублюдок", "сперма", "хуй", "яйца", "пизда",
        "ебать", "трахать", "дрочила", "дрочить", "сосать", "соси", "бордель", "дурак", "засранец", "заткнись", "кретин",
        "имбецил", "дебил", "нацист", "негр", "чурка", "жид", "пидор", "педик", "лесбуха", "трансуха", "сволочь", "мразь",
        "отброс", "зануда", "жопа", "придурок", "ссать", "срать", "самоубийство", "суицид", "проститутка", "шалава", "стерва",
        "мерзавец", "идиот", "даун", "псих", "говнюк", "тряпка", "слабак", "нищеброд", "гнида", "содомия", "минет",
        "фелляция", "мастурбация", "порно", "расист", "ксенофоб", "педофил", "изнасилование", "гитлер", "вагина", "муссолини",
        "сталин", "ккк",
    };

    static List<string> normalized;

    // Vrai si le texte contient un mot interdit (insensible à la casse et aux accents, même au milieu d'autres caractères).
    public static bool Contains(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (normalized == null)
        {
            normalized = new List<string>();
            foreach (var list in new[] { French, English, Spanish, Russian })
                foreach (string w in list)
                {
                    string n = Normalize(w);
                    if (n.Length > 0 && !normalized.Contains(n)) normalized.Add(n);
                }
        }
        string t = Normalize(text);
        foreach (string w in normalized)
            if (t.Contains(w)) return true;
        return false;
    }

    // Minuscules, sans accents (é -> e, ñ -> n...), espaces multiples réduits.
    static string Normalize(string s)
    {
        string d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        bool space = false;
        foreach (char c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!space && sb.Length > 0) sb.Append(' ');
                space = true;
                continue;
            }
            space = false;
            sb.Append(c);
        }
        return sb.ToString().Trim().Normalize(NormalizationForm.FormC);
    }
}
