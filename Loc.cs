using System;
using System.Collections.Generic;
using System.Globalization;

namespace GilBanking;

/// <summary>
/// Übersetzungen (Englisch, Deutsch, Französisch). Reihenfolge je Eintrag: en, de, fr.
/// Fehlt ein Schlüssel, wird der Schlüssel selbst angezeigt.
/// </summary>
internal static class L
{
    public const int English = 0, German = 1, French = 2;

    private static string[] A(string en, string de, string fr) => new[] { en, de, fr };

    private static readonly Dictionary<string, string[]> Table = new()
    {
        // ---- Allgemein ----
        ["all"] = A("All", "Alle", "Tous"),
        ["unknown.item"] = A("(unknown item)", "(unbekanntes Item)", "(objet inconnu)"),
        ["unknown.char"] = A("unknown", "unbekannt", "inconnu"),
        ["yes"] = A("yes", "ja", "oui"),
        ["no"] = A("no", "nein", "non"),
        ["cmd.help"] = A(
            "Open/close Gil-Banking. Use \"settings\" to jump straight to the settings.",
            "Gil-Banking öffnen/schließen. Mit \"settings\" direkt zu den Einstellungen.",
            "Ouvrir/fermer Gil-Banking. Avec « settings » : directement aux paramètres."),

        // ---- Kopfzeile / Filter ----
        ["header.notLoggedIn"] = A("not logged in", "nicht eingeloggt", "non connecté"),
        ["header.character"] = A("Character", "Charakter", "Personnage"),
        ["header.period"] = A("Period", "Zeitraum", "Période"),
        ["range.today"] = A("Today", "Heute", "Aujourd'hui"),
        ["range.7"] = A("7 days", "7 Tage", "7 jours"),
        ["range.30"] = A("30 days", "30 Tage", "30 jours"),
        ["range.all"] = A("All time", "Alles", "Tout"),
        ["filter.sales"] = A("Sales", "Verkäufe", "Ventes"),
        ["filter.purchases"] = A("Purchases", "Einkäufe", "Achats"),
        ["kind.sale"] = A("Sale", "Verkauf", "Vente"),
        ["kind.purchase"] = A("Purchase", "Einkauf", "Achat"),
        ["sort.revenue"] = A("Revenue", "Umsatz", "Chiffre d'affaires"),
        ["sort.qty"] = A("Quantity sold", "Verkaufte Menge", "Quantité vendue"),
        ["sort.avg"] = A("Avg. sale price", "Ø Verkaufspreis", "Prix moyen de vente"),
        ["sort.profit"] = A("Profit", "Gewinn", "Bénéfice"),

        // ---- Tabs ----
        ["tab.overview"] = A("Overview", "Übersicht", "Aperçu"),
        ["tab.sales"] = A("Sales", "Verkäufe", "Ventes"),
        ["tab.items"] = A("Items", "Items", "Objets"),
        ["tab.settings"] = A("Settings", "Einstellungen", "Paramètres"),

        // ---- Übersicht ----
        ["card.gil"] = A("Gil (character)", "Gil (Charakter)", "Gils (personnage)"),
        ["card.retainer"] = A("Retainer gil", "Retainer-Gil", "Gils des servants"),
        ["card.retainerSub"] = A("last known value", "zuletzt bekannter Stand", "dernière valeur connue"),
        ["card.wealth"] = A("Total wealth", "Gesamtvermögen", "Fortune totale"),
        ["card.session"] = A("This session", "Diese Sitzung", "Cette session"),
        ["card.sessionSub"] = A("current character", "aktueller Charakter", "personnage actuel"),
        ["card.revenue"] = A("Revenue (sales)", "Umsatz (Verkäufe)", "Revenus (ventes)"),
        ["card.salesCount"] = A("{0} sales", "{0} Verkäufe", "{0} ventes"),
        ["card.expenses"] = A("Expenses (purchases)", "Ausgaben (Einkäufe)", "Dépenses (achats)"),
        ["card.purchCount"] = A("{0} purchases", "{0} Einkäufe", "{0} achats"),
        ["card.profit"] = A("Profit", "Gewinn", "Bénéfice"),
        ["card.avg"] = A("Avg. per sale", "Ø pro Verkauf", "Moy. par vente"),
        ["chart.daily"] = A("Profit per day (last 14 days)", "Gewinn pro Tag (letzte 14 Tage)", "Bénéfice par jour (14 derniers jours)"),
        ["chart.wealth"] = A("Wealth over time (gil + retainers)", "Vermögensverlauf (Gil + Retainer)", "Évolution de la fortune (gils + servants)"),
        ["chart.notEnough"] = A(
            "Not enough data yet – the curve fills in as you play.",
            "Noch zu wenig Daten – die Kurve füllt sich beim Spielen automatisch.",
            "Pas encore assez de données – la courbe se remplit au fil du jeu."),
        ["chart.noSales"] = A("No sales in this period yet.", "Noch keine Verkäufe im Zeitraum.", "Aucune vente sur cette période."),
        ["bar.tip"] = A(
            "{0}\nSales:     {1} Gil\nExpenses: {2} Gil\nProfit:     {3} Gil",
            "{0}\nVerkäufe:  {1} Gil\nAusgaben: {2} Gil\nGewinn:    {3} Gil",
            "{0}\nVentes :     {1} Gil\nDépenses : {2} Gil\nBénéfice : {3} Gil"),
        ["abbr.b"] = A(" B", " Mrd", " Md"),
        ["abbr.m"] = A(" M", " Mio", " M"),
        ["abbr.k"] = A(" k", " k", " k"),
        ["retainers.title"] = A("Retainers", "Retainer", "Servants"),

        // ---- Verkäufe ----
        ["sales.search"] = A("Search item", "Item suchen", "Rechercher un objet"),
        ["sales.add"] = A("+ Add entry manually", "+ Eintrag manuell hinzufügen", "+ Ajouter une entrée manuellement"),
        ["man.item"] = A("Item", "Item", "Objet"),
        ["man.qty"] = A("Quantity", "Menge", "Quantité"),
        ["man.total"] = A("Total (gil)", "Gesamt (Gil)", "Total (gils)"),
        ["man.note"] = A("Note", "Notiz", "Note"),
        ["man.addBtn"] = A("Add", "Hinzufügen", "Ajouter"),
        ["man.loginOnly"] = A("(only possible while logged in)", "(nur im eingeloggten Zustand möglich)", "(uniquement possible une fois connecté)"),
        ["col.time"] = A("Time", "Zeit", "Heure"),
        ["col.type"] = A("Type", "Typ", "Type"),
        ["col.item"] = A("Item", "Item", "Objet"),
        ["col.qty"] = A("Qty", "Menge", "Qté"),
        ["col.each"] = A("Each", "Stück", "Unité"),
        ["col.total"] = A("Total", "Gesamt", "Total"),
        ["col.char"] = A("Character", "Charakter", "Personnage"),
        ["col.source"] = A("Source", "Quelle", "Source"),
        ["del.tip"] = A("Ctrl + click to delete", "Strg + Klick zum Löschen", "Ctrl + clic pour supprimer"),

        // ---- Items ----
        ["items.sortBy"] = A("Sort by", "Sortieren nach", "Trier par"),
        ["col.sales"] = A("Sales", "Verkäufe", "Ventes"),
        ["col.revenue"] = A("Revenue", "Umsatz", "CA"),
        ["col.avgSale"] = A("Avg. sale", "Ø Verkauf", "Moy. vente"),
        ["col.avgBuy"] = A("Avg. buy", "Ø Einkauf", "Moy. achat"),
        ["col.profit"] = A("Profit", "Gewinn", "Bénéfice"),

        // ---- Einstellungen ----
        ["section.language"] = A("Language", "Sprache", "Langue"),
        ["lang.label"] = A("Language", "Sprache", "Langue"),
        ["lang.auto"] = A("Automatic (Dalamud/game language)", "Automatisch (Dalamud-/Spielsprache)", "Automatique (langue Dalamud/jeu)"),
        ["section.detection"] = A("Detection", "Erkennung", "Détection"),
        ["chk.chat"] = A("Detect sales/purchases from chat", "Verkäufe/Einkäufe aus dem Chat erkennen", "Détecter les ventes/achats dans le chat"),
        ["kw.sale"] = A("Words for a sale (separate with ;)", "Wörter für Verkauf (mit ; trennen)", "Mots pour une vente (séparés par ;)"),
        ["kw.buy"] = A("Words for a purchase (separate with ;)", "Wörter für Einkauf (mit ; trennen)", "Mots pour un achat (séparés par ;)"),
        ["section.tracking"] = A("Tracking", "Tracking", "Suivi"),
        ["slider.gilInterval"] = A("Gil check every (seconds)", "Gil-Abfrage alle (Sekunden)", "Lecture des gils toutes les (secondes)"),
        ["slider.thin"] = A("Thin out history after (days)", "Verlauf ausdünnen nach (Tagen)", "Alléger l'historique après (jours)"),
        ["section.paths"] = A("Storage locations", "Speicherorte", "Emplacements de stockage"),
        ["paths.help"] = A(
            "Leave empty for the default folder. When you pick a new data folder, your current data is copied there; if a ledger.json already exists there, it is loaded instead. Dalamud's small settings file always stays in the Dalamud folder.",
            "Leer lassen = Standardordner. Bei einem neuen Datenordner werden die aktuellen Daten dorthin kopiert; liegt dort schon eine ledger.json, wird diese stattdessen geladen. Die kleine Einstellungsdatei von Dalamud bleibt immer im Dalamud-Ordner.",
            "Laissez vide pour utiliser le dossier par défaut. Avec un nouveau dossier de données, vos données actuelles y sont copiées ; si un ledger.json s'y trouve déjà, il est chargé à la place. Le petit fichier de paramètres de Dalamud reste toujours dans le dossier de Dalamud."),
        ["paths.data"] = A("Data folder (ledger.json)", "Datenordner (ledger.json)", "Dossier de données (ledger.json)"),
        ["paths.export"] = A("Export folder (CSV)", "Exportordner (CSV)", "Dossier d'export (CSV)"),
        ["paths.apply"] = A("Apply", "Übernehmen", "Appliquer"),
        ["paths.reset"] = A("Reset to default", "Auf Standard zurücksetzen", "Rétablir par défaut"),
        ["paths.current"] = A("Current: {0}", "Aktuell: {0}", "Actuel : {0}"),
        ["paths.exports"] = A("Exports: {0}", "Exporte: {0}", "Exports : {0}"),
        ["slider.save"] = A("Save at most every N minutes", "Höchstens alle N Minuten speichern", "Enregistrer au maximum toutes les N minutes"),
        ["save.help"] = A(
            "Data is only written when something changed, in the background, and always on logout/exit. After a game crash at most the last N minutes are lost.",
            "Es wird nur bei Änderungen geschrieben, im Hintergrund und beim Ausloggen/Beenden sowieso. Bei einem Spielabsturz gehen höchstens die Daten der letzten N Minuten verloren.",
            "Les données ne sont écrites qu'en cas de changement, en arrière-plan, et toujours à la déconnexion/fermeture. En cas de plantage du jeu, seules les N dernières minutes sont perdues."),
        ["section.tooltip"] = A("Market price tooltip (Universalis)", "Marktpreis-Tooltip (Universalis)", "Infobulle du prix du marché (Universalis)"),
        ["chk.tooltip"] = A("Show market value when hovering items", "Marktwert beim Hovern über Items anzeigen", "Afficher la valeur marchande au survol des objets"),
        ["chk.dc"] = A("Query the whole data center instead of just your world", "Ganzes Rechenzentrum statt nur eigene Welt abfragen", "Interroger tout le centre de données et pas seulement votre monde"),
        ["slider.delay"] = A("Delay (ms)", "Verzögerung (ms)", "Délai (ms)"),
        ["slider.offx"] = A("Horizontal offset", "Abstand horizontal", "Décalage horizontal"),
        ["slider.offy"] = A("Vertical offset", "Abstand vertikal", "Décalage vertical"),
        ["tip.help"] = A(
            "The tooltip appears left of the cursor so it doesn't cover the game's own tooltip.",
            "Das Tooltip erscheint links vom Mauszeiger, damit es das Spiel-Tooltip nicht verdeckt.",
            "L'infobulle apparaît à gauche du curseur pour ne pas masquer celle du jeu."),
        ["section.data"] = A("Data", "Daten", "Données"),
        ["btn.export"] = A("Export as CSV (current character filter)", "Als CSV exportieren (aktueller Charakterfilter)", "Exporter en CSV (filtre de personnage actuel)"),
        ["export.saved"] = A("Saved: {0}", "Gespeichert: {0}", "Enregistré : {0}"),
        ["export.failed"] = A("Export failed: {0}", "Export fehlgeschlagen: {0}", "Échec de l'export : {0}"),
        ["btn.deleteAll"] = A("Delete all data", "Alle Daten löschen", "Supprimer toutes les données"),
        ["deleteAll.tip"] = A(
            "Ctrl + click. Permanently deletes sales, gil history and retainer data.",
            "Strg + Klick. Löscht Verkäufe, Gil-Verlauf und Retainer-Daten unwiderruflich.",
            "Ctrl + clic. Supprime définitivement les ventes, l'historique des gils et les données des servants."),
        ["section.debug"] = A("Troubleshooting", "Fehlersuche", "Dépannage"),
        ["chk.debug"] = A("Log system messages containing \"gil\"", "Systemnachrichten mit \"Gil\" mitschreiben", "Enregistrer les messages système contenant « gil »"),
        ["debug.help"] = A(
            "If sales aren't detected: enable this, wait for a sale and check the exact message text. Then enter the matching words above.",
            "Wenn Verkäufe nicht erkannt werden: Option einschalten, einen Verkauf abwarten und prüfen, wie die Meldung genau lautet. Dann die passenden Wörter oben eintragen.",
            "Si les ventes ne sont pas détectées : activez cette option, attendez une vente et vérifiez le texte exact du message. Saisissez ensuite les mots correspondants ci-dessus."),
        ["chat.status"] = A("Chat connection: {0}", "Chat-Anbindung: {0}", "Connexion au chat : {0}"),
        ["chat.notConnected"] = A("not connected", "nicht verbunden", "non connecté"),
        ["chat.connected"] = A("connected ({0})", "verbunden ({0})", "connecté ({0})"),
        ["chat.noEvent"] = A("no matching chat event found", "kein passendes Chat-Event gefunden", "aucun événement de chat compatible trouvé"),
        ["chat.error"] = A("error while connecting: {0}", "Fehler beim Verbinden: {0}", "erreur de connexion : {0}"),

        // ---- Meldungen ----
        ["err.pathRooted"] = A(
            "Please enter a full path (e.g. D:\\Data\\GilBanking).",
            "Bitte einen vollständigen Pfad angeben (z. B. D:\\Daten\\GilBanking).",
            "Veuillez saisir un chemin complet (p. ex. D:\\Donnees\\GilBanking)."),
        ["err.dataFolder"] = A("Data folder not usable: {0}", "Datenordner nicht nutzbar: {0}", "Dossier de données inutilisable : {0}"),
        ["err.exportRooted"] = A(
            "Export folder: please enter a full path.",
            "Exportordner: Bitte einen vollständigen Pfad angeben.",
            "Dossier d'export : veuillez saisir un chemin complet."),
        ["msg.loaded"] = A("Existing data in the new folder was loaded.", "Bestehende Daten im neuen Ordner wurden geladen.", "Les données existantes du nouveau dossier ont été chargées."),
        ["msg.written"] = A("Data was written to the new folder.", "Daten wurden in den neuen Ordner geschrieben.", "Les données ont été écrites dans le nouveau dossier."),
        ["msg.applied"] = A("Applied.", "Übernommen.", "Appliqué."),
        ["csv.header"] = A(
            "Time;Type;Item;HQ;Qty;UnitPrice;Total;Character;Source;Note",
            "Zeit;Typ;Item;HQ;Menge;Stueckpreis;Gesamt;Charakter;Quelle;Notiz",
            "Heure;Type;Objet;HQ;Qte;PrixUnitaire;Total;Personnage;Source;Note"),

        // ---- Item-Tooltip ----
        ["tip.notTradable"] = A("Not tradable", "Nicht handelbar", "Non échangeable"),
        ["tip.notTradableSub"] = A(
            "Cannot be sold on the market board.",
            "Kann nicht auf dem Marktbrett verkauft werden.",
            "Ne peut pas être vendu sur le tableau du marché."),
        ["tip.npc"] = A("NPC sell price: {0} Gil", "NPC-Verkaufspreis: {0} Gil", "Prix de vente PNJ : {0} Gil"),
        ["tip.notLoggedIn"] = A("Not logged in – no price available.", "Nicht eingeloggt – kein Preis abrufbar.", "Non connecté – prix indisponible."),
        ["tip.loading"] = A("Loading market prices ({0}) …", "Lade Marktpreise ({0}) …", "Chargement des prix ({0}) …"),
        ["tip.unreachable"] = A("Universalis unreachable", "Universalis nicht erreichbar", "Universalis injoignable"),
        ["tip.noData"] = A("No market data for {0}.", "Keine Marktdaten für {0}.", "Aucune donnée de marché pour {0}."),
        ["tip.noData2"] = A(
            "Nobody has listed or uploaded this item recently.",
            "Niemand hat dieses Item aktuell gelistet oder hochgeladen.",
            "Personne n'a listé cet objet ni envoyé de données récemment."),
        ["tip.board"] = A("Market board · {0}", "Marktbrett · {0}", "Tableau du marché · {0}"),
        ["tip.minNq"] = A("Lowest price (NQ)", "Niedrigster Preis (NQ)", "Prix le plus bas (NQ)"),
        ["tip.minHq"] = A("Lowest price (HQ)", "Niedrigster Preis (HQ)", "Prix le plus bas (HQ)"),
        ["tip.cheapest"] = A("Cheapest listing", "Günstigstes Angebot", "Offre la moins chère"),
        ["tip.avgCur"] = A("Avg. currently listed", "Ø aktuell gelistet", "Moy. actuellement en vente"),
        ["tip.avgSold"] = A("Avg. sale price", "Ø Verkaufspreis", "Prix moyen de vente"),
        ["tip.velocity"] = A("Sales per day", "Verkäufe pro Tag", "Ventes par jour"),
        ["tip.updated"] = A("Last updated: {0}", "Zuletzt aktualisiert: {0}", "Dernière mise à jour : {0}"),
        ["tip.ownAvg"] = A("Your avg. sale", "Dein Ø Verkauf", "Votre moy. de vente"),
        ["tip.source"] = A("Data: universalis.app", "Daten: universalis.app", "Données : universalis.app"),
        ["age.now"] = A("just now", "gerade eben", "à l'instant"),
        ["age.min"] = A("{0} min ago", "vor {0} Min.", "il y a {0} min"),
        ["age.hour"] = A("{0} h ago", "vor {0} Std.", "il y a {0} h"),
        ["age.day"] = A("{0} days ago", "vor {0} Tagen", "il y a {0} j"),
    };

    /// <summary>0 = en, 1 = de, 2 = fr.</summary>
    public static int Index { get; private set; }

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>
    /// setting: 0 = automatisch (Dalamud-Sprache), 1 = Englisch, 2 = Deutsch, 3 = Französisch.
    /// uiLanguage: Sprachcode von Dalamud (z. B. "de", "fr", "en").
    /// </summary>
    public static void Update(int setting, string? uiLanguage)
    {
        int idx;
        if (setting >= 1 && setting <= 3)
        {
            idx = setting - 1;
        }
        else
        {
            var code = (uiLanguage ?? "en").Trim().ToLowerInvariant();
            idx = code.StartsWith("de") ? German : code.StartsWith("fr") ? French : English;
        }

        if (idx == Index && Culture.Name != "") return;
        Index = idx;
        try
        {
            Culture = CultureInfo.GetCultureInfo(idx switch { German => "de-DE", French => "fr-FR", _ => "en-GB" });
        }
        catch
        {
            Culture = CultureInfo.InvariantCulture;
        }
    }

    public static string T(string key) =>
        Table.TryGetValue(key, out var a) ? a[Index] : key;

    public static string F(string key, params object[] args) =>
        string.Format(Culture, T(key), args);

    public static string ItemName(string? name) =>
        string.IsNullOrEmpty(name) ? T("unknown.item") : name;
}
