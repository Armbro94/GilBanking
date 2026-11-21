using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace GilBanking;

/// <summary>
/// Liest nur den Gil-Stand des Charakters und der Retainer aus dem Spielspeicher.
/// Zusätzlich werden Gil-Änderungen beim NPC-Händler und bei der Reparatur erkannt.
/// Es wird nichts im Spiel ausgelöst oder gesteuert.
/// </summary>
public sealed unsafe class GilTracker : IDisposable
{
    private const uint GilItemId = 1;

    /// <summary>Fenster, die bei einer Gil-Änderung auf "offen" geprüft werden (für die Zuordnung und die Fehlersuche).</summary>
    private static readonly string[] CandidateAddons =
    {
        "Shop", "Repair", "InclusionShop", "FreeShop", "ShopExchangeItem", "ShopExchangeCurrency",
        "Teleport", "TelepotTown", "Bank", "Trade", "LetterViewer", "ItemSearch", "ItemSearchResult",
        "RetainerSellList", "RetainerSell", "SelectString", "SelectYesno", "MateriaAttach", "Materialize",
        "FreeCompanyChest", "RetainerList",
    };

    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly IDataManager data;
    private readonly Ledger ledger;
    private readonly Configuration config;

    private DateTime nextCheck = DateTime.MinValue;
    private DateTime lastSnapUtc = DateTime.MinValue;
    private long lastGil = -1;
    private long lastRet = -1;
    private int zeroStrikes;

    // Schnelle Erkennung von Gil-Änderungen (alle ~250 ms, nur ein Zahlenvergleich).
    private DateTime nextFast = DateTime.MinValue;
    private long fastLast = -1;
    private int fastZeroStrikes;
    private readonly List<string> recentGil = new();

    // Retainer: Gil-Stand wird jede Sekunde mit dem letzten bekannten Wert verglichen.
    private DateTime nextRetainer = DateTime.MinValue;
    private readonly List<(long Amount, DateTime At)> outflows = new();

    public string? CharacterKey { get; private set; }
    public long CurrentGil { get; private set; }
    public long SessionStartGil { get; private set; }
    public bool HasSession { get; private set; }

    public GilTracker(IFramework framework, IObjectTable objectTable, IGameGui gameGui, IDataManager data,
                      Ledger ledger, Configuration config)
    {
        this.framework = framework;
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.data = data;
        this.ledger = ledger;
        this.config = config;
        framework.Update += OnUpdate;
    }

    /// <summary>Letzte erkannte Gil-Änderungen mit offenen Fenstern (für die Fehlersuche).</summary>
    public List<string> RecentGilChanges() => recentGil.ToList();

    private string? CurrentKey()
    {
        var player = objectTable.LocalPlayer;
        if (player == null) return null;
        var world = player.HomeWorld.ValueNullable?.Name.ToString() ?? "?";
        return $"{player.Name.TextValue}@{world}";
    }

    private void OnUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;

        try
        {
            FastCheck(now);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler bei der Erkennung von Gil-Änderungen.");
        }

        if (now < nextCheck) return;
        nextCheck = now.AddSeconds(Math.Max(5, config.SnapshotIntervalSeconds));

        try
        {
            var key = CurrentKey();
            if (key == null)
            {
                if (HasSession)
                {
                    HasSession = false;
                    CharacterKey = null;
                    lastGil = lastRet = -1;
                    ledger.SaveIfDue(true);
                }
                return;
            }

            var im = InventoryManager.Instance();
            if (im == null) return;
            long gil = im->GetInventoryItemCount(GilItemId);

            // Beim Laden/Zonen kann kurz 0 gelesen werden: ein einzelner Null-Wert wird ignoriert.
            if (gil == 0 && lastGil > 0 && zeroStrikes < 1)
            {
                zeroStrikes++;
                return;
            }
            zeroStrikes = 0;

            var ret = ReadRetainers(key);

            if (!HasSession || key != CharacterKey)
            {
                CharacterKey = key;
                SessionStartGil = gil;
                HasSession = true;
                lastGil = lastRet = -1;
            }

            CurrentGil = gil;

            if (gil != lastGil || ret != lastRet || (now - lastSnapUtc).TotalMinutes >= 30)
            {
                ledger.AddSnapshot(key, gil, ret);
                lastGil = gil;
                lastRet = ret;
                lastSnapUtc = now;
            }

            ledger.SaveIfDue(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler beim Gil-Tracking.");
        }
    }

    // ---------- Gil-Änderungen (NPC-Händler, Reparatur) ----------

    private void FastCheck(DateTime now)
    {
        if (now < nextFast) return;
        nextFast = now.AddMilliseconds(250);

        if (objectTable.LocalPlayer == null)
        {
            fastLast = -1;
            return;
        }

        if (now >= nextRetainer)
        {
            nextRetainer = now.AddSeconds(1);
            var rkey = CharacterKey ?? CurrentKey();
            if (rkey != null) ReadRetainers(rkey);
        }

        var im = InventoryManager.Instance();
        if (im == null) return;
        long gil = im->GetInventoryItemCount(GilItemId);

        // Beim Laden kann kurz 0 gelesen werden: Null-Werte werden einige Male ignoriert.
        if (gil == 0 && fastLast > 0 && fastZeroStrikes < 4)
        {
            fastZeroStrikes++;
            return;
        }
        fastZeroStrikes = 0;

        if (fastLast >= 0 && gil != fastLast)
            OnGilChanged(gil - fastLast);
        fastLast = gil;
    }

    private void OnGilChanged(long delta)
    {
        var open = CandidateAddons.Where(IsAddonVisible).ToList();
        var hovered = NormalizeItemId(gameGui.HoveredItem);

        recentGil.Insert(0, $"{(delta > 0 ? "+" : "")}{delta:N0} | {(open.Count > 0 ? string.Join(", ", open) : "-")} | item {hovered}");
        if (recentGil.Count > 30) recentGil.RemoveAt(recentGil.Count - 1);

        var key = CharacterKey ?? CurrentKey();
        if (key == null) return;

        var total = Math.Abs(delta);

        // Jede Ausgabe merken: Einzahlungen beim Retainer (Gil wandert vom Charakter zum Retainer) sind keine Verkäufe.
        if (delta < 0) RememberOutflow(total);

        // Reparatur: nur Ausgaben.
        if (open.Contains("Repair"))
        {
            if (delta < 0 && config.TrackRepairs)
            {
                ledger.AddEntry(new LedgerEntry
                {
                    Kind = EntryKind.Purchase,
                    ItemName = "@repair",
                    Quantity = 1,
                    TotalGil = total,
                    Character = key,
                    Source = "Repair",
                });
            }
            return;
        }

        // NPC-Händler: Kauf (Gil sinkt) oder Verkauf an den Händler (Gil steigt).
        if (open.Contains("Shop") && config.TrackNpcShop)
        {
            uint itemId = 0;
            var name = "";
            var qty = 1;

            // Das Item unter dem Mauszeiger wird nur übernommen, wenn der Betrag zum Händlerpreis passt.
            var sheet = data.GetExcelSheet<Item>();
            if (hovered != 0 && sheet != null && sheet.TryGetRow(hovered, out var row))
            {
                var unit = delta < 0 ? row.PriceMid : row.PriceLow;
                if (unit > 0 && total % unit == 0)
                {
                    itemId = hovered;
                    name = row.Name.ToString();
                    qty = (int)Math.Min(int.MaxValue, total / unit);
                }
            }

            ledger.AddEntry(new LedgerEntry
            {
                Kind = delta < 0 ? EntryKind.Purchase : EntryKind.Sale,
                ItemId = itemId,
                ItemName = name,
                Quantity = Math.Max(1, qty),
                TotalGil = total,
                Character = key,
                Source = "NPC",
            });
        }
    }

    private static uint NormalizeItemId(ulong raw)
    {
        if (raw == 0 || raw >= 2_000_000) return 0;
        if (raw >= 1_000_000) raw -= 1_000_000;
        else if (raw >= 500_000) raw -= 500_000;
        return (uint)raw;
    }

    /// <summary>
    /// Prüft, ob ein Spielfenster geladen und sichtbar ist. Die Rückgabeart von GetAddonByName
    /// unterscheidet sich je nach Dalamud-Version, daher wird sie allgemein ausgewertet.
    /// </summary>
    private bool IsAddonVisible(string name)
    {
        try
        {
            object? r = gameGui.GetAddonByName(name);
            if (r == null) return false;
            if (r is nint p) return p != 0;

            var t = r.GetType();
            if (t.GetProperty("IsNull")?.GetValue(r) is true) return false;
            var vis = t.GetProperty("IsVisible")?.GetValue(r);
            return vis is bool b ? b : true;
        }
        catch
        {
            return false;
        }
    }

    // ---------- Retainer-Einnahmen ----------

    private void RememberOutflow(long amount)
    {
        var now = DateTime.UtcNow;
        outflows.RemoveAll(o => (now - o.At).TotalMinutes > 3);
        outflows.Add((amount, now));
    }

    private bool TakeMatchingOutflow(long amount)
    {
        var now = DateTime.UtcNow;
        var i = outflows.FindIndex(o => o.Amount == amount && (now - o.At).TotalMinutes <= 3);
        if (i < 0) return false;
        outflows.RemoveAt(i);
        return true;
    }

    /// <summary>
    /// Steigt der Gil-Stand eines Retainers gegenüber dem zuletzt bekannten Wert, hat er etwas verkauft.
    /// Einzahlungen werden ausgeschlossen (Gil-Fenster des Retainers offen oder passende Ausgabe des Charakters kurz vorher).
    /// Der erste bekannte Stand eines Retainers ist nur der Ausgangswert.
    /// </summary>
    private void CheckRetainerIncome(string key, Dictionary<string, long> current)
    {
        if (!config.TrackRetainerSales) return;
        if (!ledger.Data.Retainers.TryGetValue(key, out var previous)) return;

        var bank = IsAddonVisible("Bank");
        foreach (var kv in current)
        {
            if (!previous.TryGetValue(kv.Key, out var before) || kv.Value <= before) continue;

            var delta = kv.Value - before;
            if (bank || TakeMatchingOutflow(delta)) continue;

            ledger.AddEntry(new LedgerEntry
            {
                Kind = EntryKind.Sale,
                ItemName = "@retainer:" + kv.Key,
                Quantity = 1,
                TotalGil = delta,
                Character = key,
                Source = "Retainer",
            });
        }
    }

    // ---------- Retainer ----------

    /// <summary>
    /// Retainer-Gil ist nur aktuell, wenn das Spiel die Retainer-Liste geladen hat (z. B. nach Öffnen der Rufglocke).
    /// Sind keine gültigen Retainer geladen, wird der zuletzt bekannte Wert verwendet.
    /// </summary>
    private long ReadRetainers(string key)
    {
        var rm = RetainerManager.Instance();
        if (rm != null)
        {
            var count = rm->GetRetainerCount();
            var dict = new Dictionary<string, long>();
            for (uint i = 0; i < count; i++)
            {
                var r = rm->GetRetainerBySortedIndex(i);
                if (r == null || r->RetainerId == 0) continue;
                var name = r->NameString;
                if (string.IsNullOrEmpty(name)) continue;
                dict[name] = r->Gil;
            }
            if (dict.Count > 0)
            {
                CheckRetainerIncome(key, dict);
                ledger.SetRetainers(key, dict);
            }
        }
        return ledger.RetainerTotal(key);
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
    }
}
