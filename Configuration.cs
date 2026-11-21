using System;
using Dalamud.Configuration;

namespace GilBanking;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Verkäufe/Einkäufe aus Systemnachrichten im Chat erkennen.</summary>
    public bool TrackChatSales { get; set; } = true;

    /// <summary>Schlüsselwörter (mit ; getrennt), die eine Nachricht als Verkauf markieren.</summary>
    public string SaleKeywords { get; set; } = "sold;verkauft;vendu;売却";

    /// <summary>Schlüsselwörter (mit ; getrennt), die eine Nachricht als Einkauf markieren.</summary>
    public string PurchaseKeywords { get; set; } = "purchase;bought;gekauft;acheté;購入";

    /// <summary>Käufe und Verkäufe beim NPC-Händler (Gil-Änderung bei offenem Händlerfenster) erfassen.</summary>
    public bool TrackNpcShop { get; set; } = true;

    /// <summary>Reparaturkosten (Gil-Änderung bei offenem Reparaturfenster) erfassen.</summary>
    public bool TrackRepairs { get; set; } = true;

    /// <summary>Gil-Einnahmen der Retainer (Marktbrett-Verkäufe) erfassen.</summary>
    public bool TrackRetainerSales { get; set; } = true;

    /// <summary>Schutz: Verkaufsdaten können nicht gelöscht werden (weder einzeln noch alle auf einmal).</summary>
    public bool KeepSalesForever { get; set; } = true;

    /// <summary>Alle Systemnachrichten mit "Gil" für die Fehlersuche mitschreiben.</summary>
    public bool DebugChat { get; set; } = false;

    /// <summary>Marktpreis-Tooltip (Universalis) beim Hovern über Items anzeigen.</summary>
    public bool ShowMarketTooltip { get; set; } = true;

    /// <summary>Alte Einstellung (bis 0.2): wird beim Start in TooltipScopeMode übernommen und nicht mehr benutzt.</summary>
    public bool TooltipUseDataCenter { get; set; } = false;

    /// <summary>Preisquelle fürs Tooltip: 0 = eigene Welt, 1 = eigenes Rechenzentrum, 2 = ganz Europa, 3 = eigene Eingabe.</summary>
    public int TooltipScopeMode { get; set; } = 0;

    /// <summary>Bei TooltipScopeMode 3: Welt, Rechenzentrum oder Region (z. B. "Odin", "Light", "Europe").</summary>
    public string TooltipCustomScope { get; set; } = "";

    /// <summary>Bei weiter Preisquelle zusätzlich den Preis der eigenen Welt zeigen und die Ersparnis ausrechnen.</summary>
    public bool TooltipCompareHome { get; set; } = true;

    public int TooltipDelayMs { get; set; } = 250;
    public float TooltipOffsetX { get; set; } = -16f;
    public float TooltipOffsetY { get; set; } = 16f;

    /// <summary>Sprache: 0 = automatisch (Dalamud-Sprache), 1 = Englisch, 2 = Deutsch, 3 = Französisch.</summary>
    public int Language { get; set; } = 0;

    /// <summary>Ordner für ledger.json. Leer = Standardordner von Dalamud.</summary>
    public string DataDirectory { get; set; } = "";

    /// <summary>Ordner für CSV-Exporte. Leer = Unterordner "exports" im Datenordner.</summary>
    public string ExportDirectory { get; set; } = "";

    /// <summary>Es wird höchstens alle N Minuten auf die Festplatte geschrieben (und nur bei Änderungen).</summary>
    public int SaveIntervalMinutes { get; set; } = 10;

    public int SnapshotIntervalSeconds { get; set; } = 30;

    /// <summary>Gil-Verlauf wird nach dieser Zeit ausgedünnt (ein Punkt pro Tag).</summary>
    public int ThinAfterDays { get; set; } = 30;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
