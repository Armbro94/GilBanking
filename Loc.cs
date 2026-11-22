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
        ["tab.sales"] = A("Sales & purchases", "Verkäufe & Einkäufe", "Ventes & achats"),
        ["tab.items"] = A("Items", "Items", "Objets"),
        ["tab.settings"] = A("Settings", "Einstellungen", "Paramètres"),

        // ---- Übersicht ----
        ["card.gil"] = A("Gil (character)", "Gil (Charakter)", "Gils (personnage)"),
        ["card.retainer"] = A("Retainer gil", "Retainer-Gil", "Gils des servants"),
        ["card.retainerSub"] = A("last known value", "zuletzt bekannter Stand", "dernière valeur connue"),
        ["card.wealth"] = A("Total wealth", "Gesamtvermögen", "Fortune totale"),
        ["card.session"] = A("Today", "Heute", "Aujourd'hui"),
        ["card.sessionSub"] = A("gil change, current character", "Gil-Änderung, aktueller Charakter", "variation des gils, personnage actuel"),
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
        ["chk.npc"] = A(
            "Track NPC shop purchases and sales",
            "Käufe und Verkäufe beim NPC-Händler erfassen",
            "Suivre les achats et ventes chez les PNJ marchands"),
        ["chk.repair"] = A("Track repair costs", "Reparaturkosten erfassen", "Suivre les frais de réparation"),
        ["src.Chat"] = A("Chat", "Chat", "Chat"),
        ["src.Manual"] = A("Manual", "Manuell", "Manuel"),
        ["src.NPC"] = A("NPC shop", "NPC-Händler", "Marchand PNJ"),
        ["src.Repair"] = A("Repair", "Reparatur", "Réparation"),
        ["section.chars"] = A("Characters", "Charaktere", "Personnages"),
        ["chars.help"] = A(
            "Remove all stored data of a character (sales, gil history, retainers). The character you are logged in with cannot be removed.",
            "Alle gespeicherten Daten eines Charakters entfernen (Verkäufe, Gil-Verlauf, Retainer). Der Charakter, mit dem du eingeloggt bist, kann nicht entfernt werden.",
            "Supprimer toutes les données enregistrées d'un personnage (ventes, historique des gils, servants). Le personnage connecté ne peut pas être supprimé."),
        ["btn.removeChar"] = A("Remove", "Entfernen", "Supprimer"),
        ["chars.tip"] = A(
            "Ctrl + Shift + click: permanently deletes all data of this character (also with data protection on)",
            "Strg + Umschalt + Klick: löscht alle Daten dieses Charakters endgültig (auch bei aktivem Datenschutz)",
            "Ctrl + Maj + clic : supprime définitivement toutes les données de ce personnage (même avec la protection active)"),
        ["chars.current"] = A("This is the character you are logged in with.", "Das ist der Charakter, mit dem du eingeloggt bist.", "C'est le personnage connecté."),
        ["chk.avoid"] = A(
            "Don't cover the game's item tooltip (move aside)",
            "Tooltip des Spiels nicht überdecken (ausweichen)",
            "Ne pas recouvrir l'info-bulle du jeu (se décaler)"),
        ["tip.hideKey"] = A("Hide tooltip while holding", "Tooltip ausblenden, solange gehalten", "Masquer l'info-bulle en maintenant"),
        ["tip.hideKeyHelp"] = A(
            "Hold the chosen key while hovering an item to hide the Gil-Banking tooltip. Command: /gilbanking tooltip switches it on/off.",
            "Beim Überfahren eines Items die gewählte Taste halten, um das Gil-Banking-Tooltip zu verbergen. Befehl: /gilbanking tooltip schaltet es ein/aus.",
            "Maintenez la touche choisie en survolant un objet pour masquer l'info-bulle Gil-Banking. Commande : /gilbanking tooltip l'active/désactive."),
        ["hide.off"] = A("Off", "Aus", "Désactivé"),
        ["hide.ctrl"] = A("Ctrl", "Strg", "Ctrl"),
        ["hide.shift"] = A("Shift", "Umschalt", "Maj"),
        ["hide.ctrlShift"] = A("Ctrl + Shift", "Strg + Umschalt", "Ctrl + Maj"),
        ["debug.gameTip"] = A("Game tooltip:", "Spiel-Tooltip:", "Info-bulle du jeu :"),
        ["slider.tipScale"] = A("Tooltip size", "Tooltip-Größe", "Taille de l'info-bulle"),
        ["chk.hideUntradable"] = A(
            "No tooltip for items that can't be sold on the market board",
            "Kein Tooltip bei Items, die nicht auf dem Marktbrett verkäuflich sind",
            "Pas d'info-bulle pour les objets non vendables à l'hôtel des ventes"),
        ["col.details"] = A("Details", "Details", "Détails"),
        ["src.Market"] = A("Market board", "Marktbrett", "Hôtel des ventes"),
        ["src.Other"] = A("Other", "Sonstiges", "Divers"),
        ["tab.expenses"] = A("Expenses & other", "Ausgaben & Sonstiges", "Dépenses & divers"),
        ["exp.otherIn"] = A("Other income", "Sonstige Einnahme", "Autre revenu"),
        ["exp.otherOut"] = A("Other expense", "Sonstige Ausgabe", "Autre dépense"),
        ["exp.cardRepair"] = A("Repairs", "Reparaturen", "Réparations"),
        ["exp.cardOut"] = A("Other expenses", "Sonstige Ausgaben", "Autres dépenses"),
        ["exp.cardIn"] = A("Other income", "Sonstige Einnahmen", "Autres revenus"),
        ["exp.count"] = A("{0} entries", "{0} Einträge", "{0} entrées"),
        ["exp.empty"] = A(
            "Nothing recorded in this period yet.",
            "In diesem Zeitraum wurde noch nichts erfasst.",
            "Rien d'enregistré pour cette période."),
        ["exp.hint"] = A(
            "Repairs and all other gil changes that are not item sales or purchases (quests, mail, trades, items, teleport …). Moves to and from your retainers are not counted. Hover a row to see which windows were open. Other gil is not part of revenue/profit.",
            "Reparaturen und alle übrigen Gil-Änderungen, die keine Item-Käufe oder -Verkäufe sind (Quests, Post, Handel, Gegenstände, Teleport …). Ein- und Auszahlungen bei Retainern zählen nicht. Mit der Maus über eine Zeile sieht man, welche Fenster offen waren. Sonstige Gil zählen nicht zu Umsatz/Gewinn.",
            "Réparations et toutes les autres variations de gils qui ne sont pas des ventes/achats d'objets (quêtes, courrier, échanges, objets, téléportation …). Les dépôts et retraits chez les servants ne comptent pas. Survolez une ligne pour voir les fenêtres ouvertes. Les autres gils ne comptent pas dans le chiffre d'affaires/bénéfice."),
        ["chk.market"] = A("Track market board purchases", "Marktbrett-Käufe erfassen", "Suivre les achats à l'hôtel des ventes"),
        ["chk.other"] = A(
            "Track other gil changes (quests, mail, trades …)",
            "Sonstige Gil-Änderungen erfassen (Quests, Post, Handel …)",
            "Suivre les autres variations de gils (quêtes, courrier, échanges …)"),
        ["home.label"] = A("Home world", "Heimatwelt", "Monde d'origine"),
        ["home.auto"] = A("Automatic (my character's home world)", "Automatisch (Heimatwelt meines Charakters)", "Automatique (monde d'origine de mon personnage)"),
        ["home.help"] = A(
            "Fixed world for the tooltip price comparison. Visiting another world does not change it.",
            "Feste Welt für den Preisvergleich im Tooltip. Ein Besuch auf einer anderen Welt ändert sie nicht.",
            "Monde fixe pour la comparaison des prix. Une visite sur un autre monde ne la modifie pas."),
        ["src.Retainer"] = A("Retainer", "Retainer", "Servant"),
        ["name.retainer"] = A("Retainer sales", "Retainer-Verkäufe", "Ventes du servant"),
        ["chk.retainer"] = A(
            "Track retainer sales income",
            "Einnahmen der Retainer-Verkäufe erfassen",
            "Suivre les revenus des ventes des servants"),

        // ---- Retainer-Tab ----
        ["tab.retainers"] = A("Retainers", "Retainer", "Servants"),
        ["ret.empty"] = A(
            "No retainer data yet – open your summoning bell once so the game loads your retainers.",
            "Noch keine Retainer-Daten – öffne einmal die Rufglocke, damit das Spiel deine Retainer lädt.",
            "Pas encore de données – ouvrez une fois la sonnette pour que le jeu charge vos servants."),
        ["ret.sumIncome"] = A("Income (period)", "Einnahmen (Zeitraum)", "Revenus (période)"),
        ["ret.avg"] = A("Avg. per retainer", "Ø pro Retainer", "Moy. par servant"),
        ["ret.count"] = A("{0} retainers", "{0} Retainer", "{0} servants"),
        ["ret.income"] = A("Income in period: +{0} Gil", "Einnahmen im Zeitraum: +{0} Gil", "Revenus sur la période : +{0} Gil"),
        ["ret.last"] = A("Last income: {0}", "Letzte Einnahme: {0}", "Dernier revenu : {0}"),
        ["ret.none"] = A("No income recorded yet", "Noch keine Einnahmen erfasst", "Aucun revenu enregistré"),
        ["ret.hint"] = A(
            "Income is recorded when the game loads your retainers (open the summoning bell). The game doesn't tell which items were sold, so sales show up as one amount per retainer and visit.",
            "Einnahmen werden erfasst, wenn das Spiel deine Retainer lädt (Rufglocke öffnen). Welche Items verkauft wurden, verrät das Spiel nicht, daher erscheint pro Retainer und Besuch ein Gesamtbetrag.",
            "Les revenus sont enregistrés lorsque le jeu charge vos servants (ouvrir la sonnette). Le jeu n'indique pas quels objets ont été vendus : chaque servant et chaque visite donne un montant global."),

        // ---- Tooltip: Preisquelle ----
        ["scope.label"] = A("Price source", "Preisquelle", "Source des prix"),
        ["scope.world"] = A("My home world", "Meine Heimatwelt", "Mon monde d'origine"),
        ["scope.dc"] = A("My data center", "Mein Rechenzentrum", "Mon centre de données"),
        ["scope.eu"] = A("All of Europe", "Ganz Europa", "Toute l'Europe"),
        ["scope.custom"] = A("Custom …", "Eigene Eingabe …", "Personnalisé …"),
        ["scope.customHelp"] = A(
            "World, data center or region, e.g. Odin, Light or Europe",
            "Welt, Rechenzentrum oder Region, z. B. Odin, Light oder Europe",
            "Monde, centre de données ou région, p. ex. Odin, Light ou Europe"),
        ["chk.compare"] = A(
            "Also show prices on my home world (NQ/HQ) and compare",
            "Zusätzlich Preise auf meiner Heimatwelt (NQ/HQ) zeigen und vergleichen",
            "Afficher aussi les prix sur mon monde d'origine (NQ/HQ) et comparer"),
        ["tip.home"] = A("Your world ({0})", "Deine Welt ({0})", "Votre monde ({0})"),
        ["tip.homeWorld"] = A("Your home world ({0})", "Deine Heimatwelt ({0})", "Votre monde d'origine ({0})"),
        ["tip.compareFor"] = A("Comparison for {0}", "Vergleich für {0}", "Comparaison pour {0}"),
        ["tip.noneOnHome"] = A(
            "No {0} listings on your home world – you'd be the only seller.",
            "Keine {0}-Angebote auf deiner Heimatwelt – du wärst der Einzige.",
            "Aucune offre {0} sur votre monde d'origine – vous seriez le seul vendeur."),
        ["tip.noneOnHomeAny"] = A(
            "No listings on your home world – you'd be the only seller.",
            "Keine Angebote auf deiner Heimatwelt – du wärst der Einzige.",
            "Aucune offre sur votre monde d'origine – vous seriez le seul vendeur."),
        ["tip.cheaperElsewhere"] = A(
            "Cheaper on {2}: {0} Gil less (-{1}%)",
            "Günstiger auf {2}: {0} Gil weniger (-{1} %)",
            "Moins cher sur {2} : {0} Gil de moins (-{1} %)"),
        ["tip.margin"] = A(
            "Reselling on your home world: about +{0} Gil per unit (before tax)",
            "Weiterverkauf auf deiner Heimatwelt: ca. +{0} Gil pro Stück (vor Steuer)",
            "Revente sur votre monde d'origine : environ +{0} Gil par unité (avant taxe)"),
        ["tip.homeCheapest"] = A(
            "Your home world already has the lowest price.",
            "Deine Heimatwelt hat bereits den niedrigsten Preis.",
            "Votre monde d'origine a déjà le prix le plus bas."),
        ["tip.save"] = A(
            "Save {0} Gil (-{1}%) on {2}",
            "Du sparst {0} Gil (-{1} %) auf {2}",
            "Économisez {0} Gil (-{1} %) sur {2}"),
        ["tip.homeBest"] = A(
            "Your world is already the cheapest.",
            "Deine Welt ist bereits am günstigsten.",
            "Votre monde est déjà le moins cher."),

        // ---- Schutz der Verkaufsdaten ----
        ["chk.protect"] = A(
            "Never delete sales data (protection)",
            "Verkaufsdaten nie löschen (Schutz)",
            "Ne jamais supprimer les données de vente (protection)"),
        ["protect.help"] = A(
            "While on, single entries and \"Delete all data\" are disabled, so your sales stay forever. A daily backup (ledger.json.bak) is kept as well.",
            "Solange aktiv, sind Einzellöschen und \"Alle Daten löschen\" gesperrt, deine Verkäufe bleiben dauerhaft erhalten. Zusätzlich gibt es ein tägliches Backup (ledger.json.bak).",
            "Tant que l'option est active, la suppression d'entrées et « Supprimer toutes les données » sont désactivées : vos ventes restent conservées. Une sauvegarde quotidienne (ledger.json.bak) est aussi conservée."),
        ["debug.gil"] = A(
            "Latest gil changes (amount | open windows | hovered item id):",
            "Letzte Gil-Änderungen (Betrag | offene Fenster | Item-ID unter der Maus):",
            "Dernières variations de gils (montant | fenêtres ouvertes | id de l'objet survolé) :"),
        ["slider.gilInterval"] = A("Gil check every (seconds)", "Gil-Abfrage alle (Sekunden)", "Lecture des gils toutes les (secondes)"),
        ["chk.thin"] = A(
            "Thin out old gil history",
            "Alten Gil-Verlauf ausdünnen",
            "Alléger l'ancien historique des gils"),
        ["thin.help"] = A(
            "Off = the complete gil history is kept forever (the file grows a bit over time). On = after the chosen number of days only one point per day is kept. Sales are never thinned.",
            "Aus = der komplette Gil-Verlauf bleibt für immer erhalten (die Datei wächst dann etwas). An = nach der gewählten Anzahl Tage bleibt nur ein Punkt pro Tag. Verkäufe werden nie ausgedünnt.",
            "Désactivé = tout l'historique des gils est conservé pour toujours (le fichier grossit un peu). Activé = après le nombre de jours choisi, un seul point par jour est conservé. Les ventes ne sont jamais allégées."),
        ["slider.thin"] =A("Thin out history after (days)", "Verlauf ausdünnen nach (Tagen)", "Alléger l'historique après (jours)"),
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

    /// <summary>Anzeigename eines Eintrags; "@repair" ist die Markierung für Reparaturkosten.</summary>
    public static string ItemName(string? name) =>
        string.IsNullOrEmpty(name) ? T("unknown.item")
        : name == "@repair" ? T("src.Repair")
        : name == "@other" ? T("src.Other")
        : name.StartsWith("@retainer:") ? $"{T("name.retainer")}: {name.Substring("@retainer:".Length)}"
        : name;

    /// <summary>Wie lange etwas her ist, z. B. "vor 5 Min.".</summary>
    public static string Age(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 1) return T("age.now");
        if (d.TotalHours < 1) return F("age.min", (int)d.TotalMinutes);
        if (d.TotalDays < 1) return F("age.hour", (int)d.TotalHours);
        return F("age.day", (int)d.TotalDays);
    }

    /// <summary>Anzeigename der Quelle eines Eintrags (Chat, Manual, NPC, Repair); Unbekanntes wird unverändert gezeigt.</summary>
    public static string Source(string? source) =>
        !string.IsNullOrEmpty(source) && Table.ContainsKey("src." + source) ? T("src." + source) : source ?? "";
}
