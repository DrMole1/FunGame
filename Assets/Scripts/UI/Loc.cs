using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum Lang { En, Fr, Es, Ru }

// Traduction du jeu : le texte français sert de clé, la table donne l'anglais, l'espagnol et le russe.
// Langue anglaise par défaut, choisie avec les drapeaux du lobby et du menu pause (retenue d'une partie à l'autre).
// Ne sont pas traduits : noms propres (entités, Pogo, Abjuration…), skins, titre du jeu et commandes du Terminal.
// Pluriels : formes séparées par '|' (français, anglais, espagnol : 2 formes ; russe : 3 formes).
public static class Loc
{
    const string Key = "lang";
    // Journal (logs) envoyé par l'hôte : modèle français + arguments, traduits chez chaque joueur.
    public const char LineStart = '\u0001', Sep = '\u0002';

    static Lang current = (Lang)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Lang.En), 0, 3);
    public static Lang Current => current;
    public static event System.Action Changed;
    static readonly List<LocText> bound = new List<LocText>();

    public static void Set(Lang lang)
    {
        if (lang == current) return;
        current = lang;
        PlayerPrefs.SetInt(Key, (int)lang);
        PlayerPrefs.Save();
        bound.RemoveAll(t => t == null);
        foreach (var t in bound) t.Refresh();
        Changed?.Invoke();
    }

    public static string T(string fr)
    {
        if (string.IsNullOrEmpty(fr) || current == Lang.Fr) return fr;
        return table.TryGetValue(fr, out var row) ? row[(int)current] : fr;
    }

    public static string F(string fr, params object[] args) => string.Format(T(fr), args);

    // Forme plurielle selon n, puis mise en forme ({0} vaut en général n).
    public static string N(int n, string fr, params object[] args)
    {
        string[] forms = T(fr).Split('|');
        int i;
        switch (current)
        {
            case Lang.Fr: i = n > 1 ? 1 : 0; break;
            case Lang.Ru:
                int m10 = Mathf.Abs(n) % 10, m100 = Mathf.Abs(n) % 100;
                i = m10 == 1 && m100 != 11 ? 0 : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? 1 : 2;
                break;
            default: i = n == 1 ? 0 : 1; break;
        }
        return string.Format(forms[Mathf.Min(i, forms.Length - 1)], args);
    }

    public static string Price(int amount) => N(amount, "{0} croquette|{0} croquettes", amount);

    // Texte d'interface fixe : retraduit à chaque changement de langue.
    public static void Bind(Text text, string fr)
    {
        if (text == null) return;
        var lt = text.GetComponent<LocText>();
        if (lt == null)
        {
            lt = text.gameObject.AddComponent<LocText>();
            lt.text = text;
            bound.Add(lt);
        }
        lt.key = fr;
        lt.Refresh();
    }

    // Arguments d'une ligne de journal : "§" + clé à traduire, "¤" + prix, sinon texte tel quel (pseudo, nombre…).
    public static string Tr(string fr) => "§" + fr;
    public static string Pr(int amount) => "¤" + amount;

    // n < 0 : pas de pluriel.
    public static string Encode(string fr, int n, params string[] args) =>
        LineStart + fr + Sep + n + (args.Length > 0 ? Sep + string.Join(Sep.ToString(), args) : "");

    public static string Line(string line)
    {
        int start = line.IndexOf(LineStart);
        if (start < 0) return line;
        string[] p = line.Substring(start + 1).Split(Sep);
        if (p.Length < 2) return line;
        var args = new object[p.Length - 2];
        for (int i = 2; i < p.Length; i++)
        {
            string a = p[i];
            args[i - 2] = a.StartsWith("§") ? T(a.Substring(1))
                : a.StartsWith("¤") && int.TryParse(a.Substring(1), out int price) ? Price(price) : a;
        }
        int.TryParse(p[1], out int n);
        return line.Substring(0, start) + (n >= 0 ? N(n, p[0], args) : F(p[0], args));
    }

    // Lignes R(français, anglais, espagnol, russe), rangées dans l'ordre de l'enum Lang.
    static readonly Dictionary<string, string[]> table = Build(new[]
    {
        // ---------- Lobby et menu pause ----------
        R("Pseudo", "Nickname", "Apodo", "Никнейм"),
        R("Votre pseudo", "Your nickname", "Tu apodo", "Ваш никнейм"),
        R("Partie Solo", "Solo Game", "Partida en solitario", "Одиночная игра"),
        R("Héberger une partie", "Host a game", "Crear una partida", "Создать игру"),
        R("Skin", "Skin", "Aspecto", "Скин"),
        R("Rejoindre une partie", "Join a game", "Unirse a una partida", "Присоединиться к игре"),
        R("Code (ex. K7QXP)", "Code (e.g. K7QXP)", "Código (ej. K7QXP)", "Код (напр. K7QXP)"),
        R("Rejoindre", "Join", "Unirse", "Войти"),
        R("Quitter", "Quit", "Salir", "Выход"),
        R("Musique", "Music", "Música", "Музыка"),
        R("Effets sonores", "Sound effects", "Efectos sonoros", "Эффекты"),
        R("Luminosité", "Brightness", "Brillo", "Яркость"),
        R("Sensibilité", "Sensitivity", "Sensibilidad", "Чувствит."),
        R("Grain", "Grain", "Grano", "Зерно"),
        R("Changer de skin", "Change skin", "Cambiar de aspecto", "Сменить скин"),
        R("Quitter la partie", "Leave game", "Abandonar la partida", "Покинуть игру"),
        R("Copier le lien d'invitation", "Copy invite link", "Copiar enlace de invitación", "Копировать ссылку-приглашение"),
        R("Lien copié !", "Link copied!", "¡Enlace copiado!", "Ссылка скопирована!"),
        R("<b>Partie solo</b>", "<b>Solo game</b>", "<b>Partida en solitario</b>", "<b>Одиночная игра</b>"),
        R("Vous hébergez la partie  <b>{0}</b>", "You are hosting game  <b>{0}</b>", "Eres el anfitrión de la partida  <b>{0}</b>", "Вы создали игру  <b>{0}</b>"),
        R("Partie  <b>{0}</b>", "Game  <b>{0}</b>", "Partida  <b>{0}</b>", "Игра  <b>{0}</b>"),
        R("{0} joueur connecté (max {1})|{0} joueurs connectés (max {1})", "{0} player connected (max {1})|{0} players connected (max {1})",
            "{0} jugador conectado (máx. {1})|{0} jugadores conectados (máx. {1})", "{0} игрок в сети (макс. {1})|{0} игрока в сети (макс. {1})|{0} игроков в сети (макс. {1})"),
        R("Invitation reçue : choisissez un pseudo puis cliquez sur Rejoindre.", "Invitation received: choose a nickname, then click Join.",
            "Invitación recibida: elige un apodo y haz clic en Unirse.", "Получено приглашение: выберите никнейм и нажмите «Войти»."),
        R("Échap pour reprendre le jeu", "Esc to resume the game", "Esc para reanudar el juego", "Esc — вернуться в игру"),
        R("Contrôles", "Controls", "Controles", "Управление"),
        R("{0} : se déplacer", "{0}: move", "{0}: moverse", "{0}: движение"),
        R("{0} : courir", "{0}: run", "{0}: correr", "{0}: бег"),
        R("{0} : marcher discrètement", "{0}: sneak", "{0}: caminar con sigilo", "{0}: красться"),
        R("{0} : sauter", "{0}: jump", "{0}: saltar", "{0}: прыжок"),
        R("{0} : aboyer", "{0}: bark", "{0}: ladrar", "{0}: лаять"),
        R("Souris : orienter la caméra", "Mouse: turn the camera", "Ratón: girar la cámara", "Мышь: поворот камеры"),
        R("Molette : zoom (1re personne au plus près)", "Wheel: zoom (first person when closest)", "Rueda: zoom (primera persona al máximo)", "Колесо: зум (вид от первого лица вплотную)"),
        R("{0} : interagir, ramasser, utiliser l'objet", "{0}: interact, pick up, use item", "{0}: interactuar, recoger, usar objeto", "{0}: взаимодействие, подбор, предмет"),
        R("{0} : sélectionner un objet", "{0}: select an item", "{0}: seleccionar un objeto", "{0}: выбрать предмет"),
        R("{0} : danser", "{0}: dance", "{0}: bailar", "{0}: танцевать"),
        R("{0} à {1}", "{0} to {1}", "{0} a {1}", "{0}–{1}"),
        // Fenêtre "Modifier Contrôles".
        R("Modifier Contrôles", "Edit Controls", "Editar controles", "Настроить управление"),
        R("Cliquez sur une touche pour la modifier, puis appuyez sur la nouvelle touche (Échap : annuler).",
            "Click a key to change it, then press the new key (Esc: cancel).",
            "Haz clic en una tecla para cambiarla y pulsa la nueva tecla (Esc: cancelar).",
            "Нажмите на клавишу, чтобы изменить её, затем нажмите новую (Esc: отмена)."),
        R("Appuyez sur une touche…", "Press a key…", "Pulsa una tecla…", "Нажмите клавишу…"),
        R("Réinitialiser", "Reset", "Restablecer", "Сбросить"),
        R("Fermer", "Close", "Cerrar", "Закрыть"),
        R("Avancer", "Forward", "Avanzar", "Вперёд"),
        R("Reculer", "Back", "Retroceder", "Назад"),
        R("Gauche", "Left", "Izquierda", "Влево"),
        R("Droite", "Right", "Derecha", "Вправо"),
        R("Courir", "Run", "Correr", "Бег"),
        R("Marcher discrètement", "Sneak", "Caminar con sigilo", "Красться"),
        R("Sauter", "Jump", "Saltar", "Прыжок"),
        R("Aboyer", "Bark", "Ladrar", "Лай"),
        R("Interagir / utiliser l'objet", "Interact / use item", "Interactuar / usar objeto", "Взаимодействие / предмет"),
        R("Objet 1", "Item 1", "Objeto 1", "Предмет 1"),
        R("Objet 2", "Item 2", "Objeto 2", "Предмет 2"),
        R("Objet 3", "Item 3", "Objeto 3", "Предмет 3"),
        R("Objet 4", "Item 4", "Objeto 4", "Предмет 4"),
        R("Objet 5", "Item 5", "Objeto 5", "Предмет 5"),
        R("Danse 1", "Dance 1", "Baile 1", "Танец 1"),
        R("Danse 2", "Dance 2", "Baile 2", "Танец 2"),
        R("Danse 3", "Dance 3", "Baile 3", "Танец 3"),
        // Noms de touches.
        R("Espace", "Space", "Espacio", "Пробел"),
        R("Clic gauche", "Left click", "Clic izquierdo", "ЛКМ"),
        R("Clic droit", "Right click", "Clic derecho", "ПКМ"),
        R("Clic molette", "Middle click", "Clic central", "СКМ"),
        R("Entrée", "Enter", "Intro", "Enter"),
        R("Retour", "Backspace", "Retroceso", "Backspace"),
        R("Shift D", "Right Shift", "Shift der.", "Правый Shift"),
        R("Ctrl D", "Right Ctrl", "Ctrl der.", "Правый Ctrl"),
        R("Échap : menu pause", "Esc: pause menu", "Esc: menú de pausa", "Esc: меню паузы"),

        // ---------- Réseau ----------
        R("Délai de connexion dépassé. Réessayez.", "Connection timed out. Please try again.", "Tiempo de conexión agotado. Inténtalo de nuevo.", "Время подключения истекло. Попробуйте ещё раз."),
        R("Votre onglet était en arrière-plan : la partie était figée pour les autres joueurs. Gardez-le au premier plan.",
            "Your tab was in the background: the game was frozen for the other players. Keep it in the foreground.",
            "Tu pestaña estaba en segundo plano: la partida se quedó congelada para los demás jugadores. Mantenla en primer plano.",
            "Ваша вкладка была в фоне: у других игроков игра зависла. Держите её на переднем плане."),
        R("Création de la partie…", "Creating the game…", "Creando la partida…", "Создание игры…"),
        R("Entrez le code de la partie à rejoindre.", "Enter the code of the game to join.", "Introduce el código de la partida a la que quieres unirte.", "Введите код игры, к которой хотите присоединиться."),
        R("Connexion à la partie {0}…", "Connecting to game {0}…", "Conectando a la partida {0}…", "Подключение к игре {0}…"),
        R("L'hôte a quitté la partie.", "The host left the game.", "El anfitrión ha abandonado la partida.", "Хост покинул игру."),
        R("Vous avez été expulsé de la partie par l'hôte.", "You were kicked from the game by the host.", "El anfitrión te ha expulsado de la partida.", "Хост исключил вас из игры."),
        R("La partie est complète ({0} joueurs maximum).", "The game is full ({0} players max).", "La partida está completa ({0} jugadores como máximo).", "Игра заполнена (максимум игроков: {0})."),
        R("Partie introuvable. Vérifiez le code.", "Game not found. Check the code.", "Partida no encontrada. Comprueba el código.", "Игра не найдена. Проверьте код."),
        R("Rejoindre une partie ne fonctionne que dans la version navigateur.", "Joining a game only works in the browser version.",
            "Unirse a una partida solo funciona en la versión de navegador.", "Присоединиться к игре можно только в браузерной версии."),
        R("Bibliothèque réseau non chargée. Vérifiez votre connexion internet.", "Network library not loaded. Check your internet connection.",
            "Biblioteca de red no cargada. Comprueba tu conexión a internet.", "Сетевая библиотека не загружена. Проверьте подключение к интернету."),
        R("Votre navigateur ne supporte pas WebRTC.", "Your browser does not support WebRTC.", "Tu navegador no es compatible con WebRTC.", "Ваш браузер не поддерживает WebRTC."),
        R("Impossible de joindre le serveur de connexion.", "Unable to reach the connection server.", "No se puede contactar con el servidor de conexión.", "Не удаётся связаться с сервером подключения."),
        R("Erreur réseau ({0}).", "Network error ({0}).", "Error de red ({0}).", "Сетевая ошибка ({0})."),

        // ---------- HUD ----------
        R("Nouvelle partie", "New game", "Nueva partida", "Новая игра"),
        R("Aucun sujet de test en vie. L'expérience peut continuer.", "No test subjects left alive. The experiment can continue.",
            "Ningún sujeto de prueba sigue con vida. El experimento puede continuar.", "Ни одного живого подопытного. Эксперимент может продолжаться."),
        R("Revenir au Lobby", "Back to the Lobby", "Volver al lobby", "Вернуться в лобби"),
        R("MORT", "DEAD", "MUERTO", "МЁРТВ"),
        R("EN VIE", "ALIVE", "VIVO", "ЖИВ"),
        R("PV", "HP", "PV", "ОЗ"),
        R("Sérénité", "Serenity", "Serenidad", "Спокойствие"),
        R("ESPACE", "SPACE", "ESPACIO", "ПРОБЕЛ"),
        R("N", "N", "N", "С"),
        R("E", "E", "E", "В"),
        R("S", "S", "S", "Ю"),
        R("O", "W", "O", "З"),

        // ---------- Post-It et Télévision ----------
        R("Écrivez votre message ({0} caractères max).\nIl ne pourra plus être modifié.", "Write your message ({0} characters max).\nIt cannot be changed afterwards.",
            "Escribe tu mensaje ({0} caracteres máx.).\nNo se podrá modificar.", "Напишите сообщение (не более {0} символов).\nИзменить его будет нельзя."),
        R("Votre message…", "Your message…", "Tu mensaje…", "Ваше сообщение…"),
        R("Entrée : accrocher · Échap : annuler", "Enter: stick · Esc: cancel", "Intro: pegar · Esc: cancelar", "Enter: прикрепить · Esc: отмена"),
        R("La Télévision capte le signal d'une Balise.\nCollez le lien d'une vidéo YouTube.", "The Television picks up a Beacon's signal.\nPaste a YouTube video link.",
            "La Televisión capta la señal de una Baliza.\nPega el enlace de un vídeo de YouTube.", "Телевизор ловит сигнал маяка.\nВставьте ссылку на видео YouTube."),
        R("Entrée : diffuser · Échap : annuler", "Enter: play · Esc: cancel", "Intro: reproducir · Esc: cancelar", "Enter: показать · Esc: отмена"),
        R("Ce lien ne mène pas à une vidéo YouTube.", "This link does not lead to a YouTube video.", "Este enlace no lleva a un vídeo de YouTube.", "Эта ссылка не ведёт на видео YouTube."),

        // ---------- Terminal ----------
        R("Bienvenue sur le Terminal !\nTaper une commande pour continuer :", "Welcome to the Terminal!\nType a command to continue:",
            "¡Bienvenido al Terminal!\nEscribe un comando para continuar:", "Добро пожаловать в Терминал!\nВведите команду, чтобы продолжить:"),
        R("Faire ECHAP pour quitter.", "Press ESC to exit.", "Pulsa ESC para salir.", "Нажмите ESC, чтобы выйти."),
        R("Taper une commande puis Entrée…", "Type a command, then Enter…", "Escribe un comando y pulsa Intro…", "Введите команду и нажмите Enter…"),
        R("Écrire le numéro de l'objet à vendre (1-2-3-4-5).", "Type the number of the item to sell (1-2-3-4-5).",
            "Escribe el número del objeto que quieres vender (1-2-3-4-5).", "Введите номер предмета для продажи (1-2-3-4-5)."),
        R("Objets achetables :", "Items for sale:", "Objetos a la venta:", "Предметы для покупки:"),
        R("Écrire l'ID de l'objet à acheter.", "Type the ID of the item to buy.", "Escribe el ID del objeto que quieres comprar.", "Введите ID предмета для покупки."),
        R("Commande invalide.", "Invalid command.", "Comando no válido.", "Неверная команда."),
        R("Commande Incorrecte.", "Incorrect command.", "Comando incorrecto.", "Некорректная команда."),
        R("Le joueur {0} a été expulsé de la partie.", "Player {0} was kicked from the game.", "El jugador {0} ha sido expulsado de la partida.", "Игрок {0} исключён из игры."),
        R("Aucun autre joueur nommé {0}.", "No other player named {0}.", "Ningún otro jugador se llama {0}.", "Нет другого игрока с именем {0}."),
        R("Vous n'avez pas d'objet {0} en inventaire !", "You have no item {0} in your inventory!", "¡No tienes ningún objeto {0} en el inventario!", "В ячейке {0} инвентаря нет предмета!"),
        R("Vous avez vendu {0} pour {1} !", "You sold {0} for {1}!", "¡Has vendido {0} por {1}!", "Вы продали «{0}» за {1}!"),
        R("Votre inventaire est plein ! Impossible d'acheter {0}.", "Your inventory is full! Cannot buy {0}.", "¡Tu inventario está lleno! No puedes comprar {0}.", "Инвентарь полон! Невозможно купить «{0}»."),
        R("Cagnotte insuffisante pour acheter {0} ({1}).", "Not enough in the bank to buy {0} ({1}).", "No hay suficiente en el bote para comprar {0} ({1}).", "В копилке не хватает на «{0}» ({1})."),
        R("Vous avez acheté la Boussole pour {0} !\nElle s'affiche désormais en haut de l'écran de tous les joueurs.",
            "You bought the Compass for {0}!\nIt now appears at the top of every player's screen.",
            "¡Has comprado la Brújula por {0}!\nAhora aparece en la parte superior de la pantalla de todos los jugadores.",
            "Вы купили компас за {0}!\nТеперь он виден вверху экрана у всех игроков."),
        R("Vous avez acheté {0} pour {1} !", "You bought {0} for {1}!", "¡Has comprado {0} por {1}!", "Вы купили «{0}» за {1}!"),
        R("La Boussole a déjà été achetée.", "The Compass has already been bought.", "La Brújula ya ha sido comprada.", "Компас уже куплен."),
        R("Le quota de la manche n'est pas encore atteint.", "The round quota has not been reached yet.", "Todavía no se ha alcanzado la cuota de la ronda.", "Квота раунда ещё не выполнена."),
        R("Manche passée ! {0} ajoutée à la cagnotte pour le temps restant.|Manche passée ! {0} ajoutées à la cagnotte pour le temps restant.",
            "Round skipped! {0} added to the bank for the remaining time.",
            "¡Ronda superada! {0} añadida al bote por el tiempo restante.|¡Ronda superada! {0} añadidas al bote por el tiempo restante.",
            "Раунд пропущен! В копилку за оставшееся время добавлено: {0}."),
        R("Sous-menu /info :", "/info submenu:", "Submenú /info:", "Подменю /info:"),
        R("Nouveaux sujets de test. Enregistrement des comportements étudiés.", "New test subjects. Recording the behaviors under study.",
            "Nuevos sujetos de prueba. Registro de los comportamientos estudiados.", "Новые подопытные. Запись изучаемого поведения."),
        R("Réaction au stimuli de récompense.", "Response to reward stimuli.", "Reacción a los estímulos de recompensa.", "Реакция на стимулы вознаграждения."),
        R("Réaction au stimuli de dangers létals en milieu hostile.", "Response to lethal danger stimuli in a hostile environment.",
            "Reacción a los estímulos de peligro letal en un entorno hostil.", "Реакция на стимулы смертельной опасности во враждебной среде."),
        R("Compréhension du balisage neuronal.", "Understanding of neural beaconing.", "Comprensión del balizamiento neuronal.", "Понимание нейронной разметки."),
        R("Sujets de test confirmés. Démarrage de la procédure d'acquisition.", "Test subjects confirmed. Starting the acquisition procedure.",
            "Sujetos de prueba confirmados. Inicio del procedimiento de adquisición.", "Подопытные подтверждены. Запуск процедуры усвоения."),
        R("Assimilation de la récompense par exploration proactive.", "Assimilation of reward through proactive exploration.",
            "Asimilación de la recompensa mediante exploración proactiva.", "Усвоение вознаграждения через активное исследование."),
        R("Assimilation du châtiment à l'approche du danger.", "Assimilation of punishment when approaching danger.",
            "Asimilación del castigo al acercarse al peligro.", "Усвоение наказания при приближении к опасности."),
        R("Survie au premier cycle ternaire.", "Survival of the first ternary cycle.", "Supervivencia al primer ciclo ternario.", "Выживание в первом тройном цикле."),
        R("OK.", "OK.", "OK.", "OK."),
        R("En attente.", "Pending.", "En espera.", "В ожидании."),

        // ---------- Commandes de l'hôte ----------
        R("Aucun objet n'a l'ID {0}.", "No item has the ID {0}.", "Ningún objeto tiene el ID {0}.", "Нет предмета с ID {0}."),
        R("La Boussole s'affiche désormais chez tous les joueurs.", "The Compass now appears for every player.", "La Brújula aparece ahora para todos los jugadores.", "Компас теперь виден у всех игроков."),
        R("Votre inventaire est plein ! Impossible de recevoir {0}.", "Your inventory is full! Cannot receive {0}.", "¡Tu inventario está lleno! No puedes recibir {0}.", "Инвентарь полон! Невозможно получить «{0}»."),
        R("Vous avez reçu : {0}.", "You received: {0}.", "Has recibido: {0}.", "Вы получили: {0}."),
        R("Impossible pour le moment.", "Not possible right now.", "Imposible por el momento.", "Сейчас это невозможно."),
        R("Un Réveil sonne quelque part dans la Zone 1...", "A Réveil is ringing somewhere in Zone 1...", "Un Réveil suena en algún lugar de la Zona 1...", "Где-то в Зоне 1 звенит Réveil..."),
        R("Les Lasers ne peuvent plus apparaître une fois le terrain généré.", "Lasers can no longer appear once the terrain is generated.",
            "Los Láseres ya no pueden aparecer una vez generado el terreno.", "Лазеры не могут появиться после генерации местности."),
        R("La Dame rôde désormais dans la Zone 1...", "La Dame now prowls Zone 1...", "La Dame merodea ahora por la Zona 1...", "La Dame теперь бродит по Зоне 1..."),
        R("La Tour arpente désormais la Zone 1...", "La Tour now paces Zone 1...", "La Tour recorre ahora la Zona 1...", "La Tour теперь расхаживает по Зоне 1..."),
        R("Des Pions s'installent dans la Zone 1...", "Pions are settling into Zone 1...", "Unos Pions se instalan en la Zona 1...", "В Зоне 1 обосновываются Pions..."),
        R("La Cloche attend dans la Zone 1...", "La Cloche is waiting in Zone 1...", "La Cloche espera en la Zona 1...", "La Cloche ждёт в Зоне 1..."),
        R("Une Araignée rôde dans la Zone 1...", "An Araignée prowls Zone 1...", "Una Araignée merodea por la Zona 1...", "Araignée бродит по Зоне 1..."),
        R("Le Fou sourit désormais dans la Zone 1...", "Le Fou now smiles in Zone 1...", "Le Fou sonríe ahora en la Zona 1...", "Le Fou теперь улыбается в Зоне 1..."),
        R("Le Roi arpente désormais la Zone 1...", "Le Roi now paces Zone 1...", "Le Roi recorre ahora la Zona 1...", "Le Roi теперь расхаживает по Зоне 1..."),
        R("Le Cavalier galope désormais dans la Zone 1...", "Le Cavalier now gallops through Zone 1...", "Le Cavalier galopa ahora por la Zona 1...", "Le Cavalier теперь скачет по Зоне 1..."),
        R("Aucune entité n'a l'ID {0}.", "No entity has the ID {0}.", "Ninguna entidad tiene el ID {0}.", "Нет сущности с ID {0}."),
        R("Godmod activé : achats gratuits, invincible, sérénité intacte, jamais immobilisé.", "Godmode on: free purchases, invincible, serenity intact, never immobilized.",
            "Godmode activado: compras gratis, invencible, serenidad intacta, nunca inmovilizado.", "Режим бога включён: бесплатные покупки, неуязвимость, спокойствие не тратится, без обездвиживания."),
        R("Godmod désactivé.", "Godmode off.", "Godmode desactivado.", "Режим бога выключен."),
        R("Téléporté près de {0}.", "Teleported near {0}.", "Teletransportado cerca de {0}.", "Вы телепортировались к {0}."),
        R("Téléporté devant la porte de la Zone 2.", "Teleported in front of the Zone 2 door.", "Teletransportado delante de la puerta de la Zona 2.", "Вы телепортировались к двери Зоны 2."),

        // ---------- Partie ----------
        R("Le Canon n'est plus chargé : il n'est plus relié à une Balise chargée.", "The Cannon is no longer charged: it is no longer linked to a charged Beacon.",
            "El Cañón ya no está cargado: ya no está conectado a una Baliza cargada.", "Пушка разряжена: она больше не связана с заряженным маяком."),
        R("Quelqu'un est déjà dans ce Canon.", "Someone is already in this Cannon.", "Ya hay alguien en este Cañón.", "В этой пушке уже кто-то есть."),
        R("Un autre joueur peut vous réanimer depuis votre Chenil, dans le Hub.", "Another player can revive you from your Kennel, in the Hub.",
            "Otro jugador puede reanimarte desde tu Caseta, en el Hub.", "Другой игрок может оживить вас у вашей будки в Хабе."),
        R("Vous êtes mort. Vous regardez {0} (clic : changer de joueur).", "You are dead. You are watching {0} (click: switch player).",
            "Estás muerto. Estás viendo a {0} (clic: cambiar de jugador).", "Вы мертвы. Вы наблюдаете: {0} (клик: сменить игрока)."),
        R("Vous êtes mort.", "You are dead.", "Estás muerto.", "Вы мертвы."),
        R("Quota : {0} / {1}", "Quota: {0} / {1}", "Cuota: {0} / {1}", "Квота: {0} / {1}"),
        R("Cagnotte : {0}", "Bank: {0}", "Bote: {0}", "Копилка: {0}"),
        R("Manche {0}", "Round {0}", "Ronda {0}", "Раунд {0}"),
        R("{0} mort|{0} morts", "{0} death|{0} deaths", "{0} muerte|{0} muertes", "{0} смерть|{0} смерти|{0} смертей"),
        R("{0} croquette|{0} croquettes", "{0} kibble|{0} kibbles", "{0} croqueta|{0} croquetas", "{0} сухарик|{0} сухарика|{0} сухариков"),
        R("Votre vie et votre sérénité sont déjà au maximum.", "Your health and serenity are already full.", "Tu vida y tu serenidad ya están al máximo.", "Здоровье и спокойствие уже на максимуме."),
        R("Miam ! +{0} PV, +{1} de sérénité", "Yum! +{0} HP, +{1} serenity", "¡Ñam! +{0} PV, +{1} de serenidad", "Ням! +{0} ОЗ, +{1} спокойствия"),
        R("Visez la Balise du Hub, près de la porte, pour la recharger.", "Aim at the Hub Beacon, near the door, to recharge it.",
            "Apunta a la Baliza del Hub, cerca de la puerta, para recargarla.", "Наведитесь на маяк Хаба у двери, чтобы перезарядить его."),
        R("Visez un orbe activé, devant la porte de la Zone 2, pour le briser avec la Clé.", "Aim at an active orb, in front of the Zone 2 door, to break it with the Key.",
            "Apunta a un orbe activado, delante de la puerta de la Zona 2, para romperlo con la Llave.", "Наведитесь на активную сферу у двери Зоны 2, чтобы разбить её ключом."),
        R("Visez un mur tout proche pour y accrocher le Post-It.", "Aim at a nearby wall to stick the Post-It on it.", "Apunta a una pared cercana para pegar el Post-It.", "Наведитесь на ближайшую стену, чтобы прикрепить Post-It."),
        R("Visez le sol, tout près de vous, pour y poser : {0}.", "Aim at the ground right next to you to place: {0}.",
            "Apunta al suelo, justo a tu lado, para colocar: {0}.", "Наведитесь на землю рядом с собой, чтобы поставить: {0}."),
        R("{0} : à poser hors du Hub.", "{0}: must be placed outside the Hub.", "{0}: debe colocarse fuera del Hub.", "{0}: ставится только вне Хаба."),
        R("{0} : à poser à plus d'1 mètre d'un mur.", "{0}: must be placed more than 1 meter from a wall.", "{0}: debe colocarse a más de 1 metro de una pared.", "{0}: ставится дальше 1 метра от стены."),
        R("La Balise du Hub est chargée. Un Chargeur prolonge sa charge.", "The Hub Beacon is charged. A Charger extends its charge.",
            "La Baliza del Hub está cargada. Un Cargador prolonga su carga.", "Маяк Хаба заряжен. Зарядник продлевает заряд."),
        R("La Balise du Hub est déchargée : sélectionnez un Chargeur pour la recharger.", "The Hub Beacon is out of charge: select a Charger to recharge it.",
            "La Baliza del Hub está descargada: selecciona un Cargador para recargarla.", "Маяк Хаба разряжен: выберите зарядник, чтобы перезарядить его."),
        R("Votre inventaire est plein !", "Your inventory is full!", "¡Tu inventario está lleno!", "Инвентарь полон!"),
        R("Seul l'hôte de la partie peut appuyer sur le Bouton Rouge.", "Only the host can press the Red Button.", "Solo el anfitrión puede pulsar el Botón Rojo.", "Только хост может нажать Красную кнопку."),
        R("Aucune manche n'est en cours.", "No round is in progress.", "No hay ninguna ronda en curso.", "Раунд сейчас не идёт."),
        R("Le quota n'a pas été atteint.", "The quota was not reached.", "No se alcanzó la cuota.", "Квота не выполнена."),
        R("En attente de l'hôte pour une nouvelle partie…", "Waiting for the host to start a new game…", "Esperando a que el anfitrión inicie una nueva partida…", "Ожидание новой игры от хоста…"),
        R("Un autre joueur utilise déjà le Terminal", "Another player is already using the Terminal", "Otro jugador ya está usando el Terminal", "Терминалом уже пользуется другой игрок"),
        R("{0} vous a réanimé !", "{0} revived you!", "¡{0} te ha reanimado!", "{0} оживил(а) вас!"),
        R("Cet orbe ne peut pas être brisé.", "This orb cannot be broken.", "Este orbe no se puede romper.", "Эту сферу нельзя разбить."),

        // ---------- Journal (écran du chapiteau) ----------
        R("L'Abjuration se répand ... Le désespoir attend les plus faibles.", "The Abjuration is spreading ... Despair awaits the weakest.",
            "La Abjuration se extiende ... La desesperación aguarda a los más débiles.", "Abjuration распространяется ... Отчаяние ждёт слабейших."),
        R("L'Abjuration se retire ... Pour le moment.", "The Abjuration recedes ... For now.", "La Abjuration se retira ... Por ahora.", "Abjuration отступает ... Пока что."),
        R("Le joueur {0} est connecté.", "Player {0} has connected.", "El jugador {0} se ha conectado.", "Игрок {0} подключился."),
        R("Le joueur {0} est déconnecté.", "Player {0} has disconnected.", "El jugador {0} se ha desconectado.", "Игрок {0} отключился."),
        R("Le quota de la manche {0} est validé ! Nouveau quota : {1}", "Round {0} quota met! New quota: {1}", "¡Cuota de la ronda {0} validada! Nueva cuota: {1}", "Квота раунда {0} выполнена! Новая квота: {1}"),
        R("Le joueur {0} a vendu {1} pour {2}.", "Player {0} sold {1} for {2}.", "El jugador {0} ha vendido {1} por {2}.", "Игрок {0} продал «{1}» за {2}."),
        R("Le joueur {0} a acheté {1} pour {2}.", "Player {0} bought {1} for {2}.", "El jugador {0} ha comprado {1} por {2}.", "Игрок {0} купил «{1}» за {2}."),
        R("Le joueur {0} a été réanimé par {1}.", "Player {0} was revived by {1}.", "El jugador {0} ha sido reanimado por {1}.", "Игрок {0} оживлён. Спаситель: {1}."),
        R("Le joueur {0} est mort par {1}.", "Player {0} was killed by {1}.", "El jugador {0} ha muerto por {1}.", "Игрок {0} погиб. Причина: {1}."),
        R("Le joueur {0} a brisé l'orbe n°{1}.", "Player {0} broke orb #{1}.", "El jugador {0} ha roto el orbe n.º {1}.", "Игрок {0} разбил сферу №{1}."),
        R("Les trois orbes sont brisés : la porte de la Zone 2 s'ouvre !", "All three orbs are broken: the Zone 2 door opens!",
            "Los tres orbes están rotos: ¡la puerta de la Zona 2 se abre!", "Все три сферы разбиты: дверь Зоны 2 открывается!"),
        R("Le joueur {0} passe la manche {1} : {2} ajoutée à la cagnotte.|Le joueur {0} passe la manche {1} : {2} ajoutées à la cagnotte.",
            "Player {0} skips round {1}: {2} added to the bank.",
            "El jugador {0} supera la ronda {1}: {2} añadida al bote.|El jugador {0} supera la ronda {1}: {2} añadidas al bote.",
            "Игрок {0} пропускает раунд {1}. В копилку добавлено: {2}."),
        // Causes de mort.
        R("l'Abjuration", "the Abjuration", "la Abjuration", "Abjuration"),
        R("Laser", "a Laser", "un Láser", "лазер"),

        // ---------- Interactions ----------
        R("Ramasser {0}", "Pick up {0}", "Recoger {0}", "Подобрать: {0}"),
        R("Recharger la Balise du Hub", "Recharge the Hub Beacon", "Recargar la Baliza del Hub", "Перезарядить маяк Хаба"),
        R("Appuyer sur le Bouton Rouge", "Press the Red Button", "Pulsar el Botón Rojo", "Нажать Красную кнопку"),
        R("Passer à la manche suivante", "Skip to the next round", "Pasar a la siguiente ronda", "Перейти к следующему раунду"),
        R("Utiliser le Terminal", "Use the Terminal", "Usar el Terminal", "Использовать Терминал"),
        R("PV : {0} / {1}", "HP: {0} / {1}", "PV: {0} / {1}", "ОЗ: {0} / {1}"),
        R("Distance : {0} m", "Distance: {0} m", "Distancia: {0} m", "Расстояние: {0} м"),
        R("Réanimer {0}", "Revive {0}", "Reanimar a {0}", "Оживить: {0}"),
        R("Retirer le Post-It", "Remove the Post-It", "Quitar el Post-It", "Снять Post-It"),
        R("Creuser le monticule", "Dig the mound", "Cavar el montículo", "Раскопать холмик"),
        R("Ouvrir la boîte de Pogo", "Open Pogo's box", "Abrir la caja de Pogo", "Открыть коробку Pogo"),
        R("Vendre {0} à Pogo ({1})", "Sell {0} to Pogo ({1})", "Vender {0} a Pogo ({1})", "Продать «{0}» Pogo ({1})"),
        R("Pogo attend un objet", "Pogo is waiting for an item", "Pogo espera un objeto", "Pogo ждёт предмет"),

        // ---------- Objets (nom complet, puis nom court de la case d'inventaire) ----------
        R("Cuisse de poulet", "Chicken Leg", "Muslo de pollo", "Куриная ножка"),
        R("Cuisse", "Leg", "Muslo", "Ножка"),
        R("Balise", "Beacon", "Baliza", "Маяк"),
        R("Clé", "Key", "Llave", "Ключ"),
        R("Boussole", "Compass", "Brújula", "Компас"),
        R("Chargeur", "Charger", "Cargador", "Зарядник"),
        R("Téléporteur", "Teleporter", "Teletransportador", "Телепорт"),
        R("Téléport.", "Teleport.", "Teletr.", "Телепорт"),
        R("Balise Améliorée", "Enhanced Beacon", "Baliza Mejorada", "Улучшенный маяк"),
        R("Balise +", "Beacon +", "Baliza +", "Маяк +"),
        R("Station Curative", "Healing Station", "Estación Curativa", "Лечебная станция"),
        R("Station", "Station", "Estación", "Станция"),
        R("Canon", "Cannon", "Cañón", "Пушка"),
        R("Canard en Plastique", "Rubber Duck", "Pato de Goma", "Резиновая уточка"),
        R("Canard", "Duck", "Pato", "Уточка"),
        R("Os", "Bone", "Hueso", "Кость"),
        R("Télévision", "Television", "Televisión", "Телевизор"),
        R("Télé", "TV", "Tele", "ТВ"),
        R("Trampoline", "Trampoline", "Trampolín", "Батут"),
        R("Trampo.", "Trampo.", "Trampol.", "Батут"),
        R("Cloche Décorative", "Decorative Bell", "Campana Decorativa", "Декоративный колокол"),
        R("Cloche", "Bell", "Campana", "Колокол"),
    });

    static string[] R(string fr, string en, string es, string ru) => new[] { en, fr, es, ru };

    static Dictionary<string, string[]> Build(string[][] rows)
    {
        var d = new Dictionary<string, string[]>();
        foreach (var r in rows) d[r[1]] = r;
        return d;
    }
}
