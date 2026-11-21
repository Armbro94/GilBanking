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

    /// <summary>Alle Systemnachrichten mit "Gil" für die Fehlersuche mitschreiben.</summary>
    public bool DebugChat { get; set; } = false;

    /// <summary>Marktpreis-Tooltip (Universalis) beim Hovern über Items anzeigen.</summary>
    public bool ShowMarketTooltip { get; set; } = true;

    /// <summary>false = nur eigene Welt, true = ganzes Rechenzentrum.</summary>
    public bool TooltipUseDataCenter { get; set; } = false;

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
