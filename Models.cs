using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GilBanking;

public enum EntryKind
{
    Sale = 0,
    Purchase = 1,
}

/// <summary>Ein einzelner Verkauf oder Einkauf.</summary>
public sealed class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    public EntryKind Kind { get; set; }
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public bool IsHq { get; set; }
    public int Quantity { get; set; } = 1;
    public long TotalGil { get; set; }
    public string Character { get; set; } = "";
    public string Source { get; set; } = "Chat";
    public string Note { get; set; } = "";

    /// <summary>Name des Gehilfen bei Retainer-Verkäufen (leer bei allem anderen).</summary>
    public string Retainer { get; set; } = "";

    [JsonIgnore]
    public long UnitGil => Quantity > 0 ? TotalGil / Quantity : TotalGil;

    /// <summary>Nur zur Laufzeit: Eintrag wurde bereits mit einer zweiten Erkennung (Chat/Händler/Marktbrett) zusammengeführt.</summary>
    [JsonIgnore]
    public bool Paired { get; set; }

    /// <summary>Reparaturen und sonstige Gil-Änderungen: gehören in den Tab "Ausgaben", nicht zu den Item-Verkäufen.</summary>
    [JsonIgnore]
    public bool IsExpenseEntry => Source == "Repair" || Source == "Other" || ItemName == "@repair";
}

/// <summary>Gil-Stand eines Charakters (plus zuletzt bekannte Retainer-Gil) zu einem Zeitpunkt.</summary>
public sealed class GilSnapshot
{
    public DateTime TimeUtc { get; set; }
    public long Gil { get; set; }
    public long RetainerGil { get; set; }
}

/// <summary>Ein Eintrag in der Angebotsliste eines Gehilfen.</summary>
public sealed class ListedItem
{
    public uint ItemId { get; set; }
    public bool Hq { get; set; }
    public int Qty { get; set; }
}

/// <summary>Zuletzt gesehene Marktbrett-Angebote eines Gehilfen (zum Erkennen von Verkäufen beim nächsten Besuch).</summary>
public sealed class RetainerListing
{
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    public List<ListedItem> Items { get; set; } = new();
}

/// <summary>Gil-Stand zu Beginn des Tages (für "Heute"), bleibt bei Relog erhalten.</summary>
public sealed class DaySession
{
    public string Day { get; set; } = "";
    public long StartGil { get; set; }
}

public sealed class LedgerData
{
    public Dictionary<string, DaySession> Sessions { get; set; } = new();

    /// <summary>Angebotslisten je Charakter und Gehilfe.</summary>
    public Dictionary<string, Dictionary<string, RetainerListing>> Listings { get; set; } = new();
    public List<LedgerEntry> Entries { get; set; } = new();
    public Dictionary<string, List<GilSnapshot>> Snapshots { get; set; } = new();
    public Dictionary<string, Dictionary<string, long>> Retainers { get; set; } = new();
}

public readonly record struct Totals(long Sales, long Purchases, int SaleCount, int PurchaseCount)
{
    public long Profit => Sales - Purchases;
}

public sealed class ItemStat
{
    public string Key { get; set; } = "";
    public uint ItemId { get; set; }
    public string Name { get; set; } = "";
    public long SoldQty { get; set; }
    public long SoldGil { get; set; }
    public long BoughtQty { get; set; }
    public long BoughtGil { get; set; }
    public int SaleCount { get; set; }

    public long AvgSale => SoldQty > 0 ? SoldGil / SoldQty : 0;
    public long AvgBuy => BoughtQty > 0 ? BoughtGil / BoughtQty : 0;
    public long Profit => SoldGil - BoughtGil;
}
