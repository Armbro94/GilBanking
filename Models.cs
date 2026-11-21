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

    [JsonIgnore]
    public long UnitGil => Quantity > 0 ? TotalGil / Quantity : TotalGil;
}

/// <summary>Gil-Stand eines Charakters (plus zuletzt bekannte Retainer-Gil) zu einem Zeitpunkt.</summary>
public sealed class GilSnapshot
{
    public DateTime TimeUtc { get; set; }
    public long Gil { get; set; }
    public long RetainerGil { get; set; }
}

public sealed class LedgerData
{
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
