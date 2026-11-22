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

    /// <summary>
    /// Fenster, die laufend beobachtet werden. Wie bei GilDelta zählt ein Fenster auch noch ca. 2 Sekunden nach dem Schließen,
    /// weil sich Gil-Änderung und Fensterwechsel zeitlich überschneiden können.
    /// </summary>
    private static readonly string[] WatchedAddons =
    {
        "Shop", "InclusionShop", "Repair", "ItemSearch", "ItemSearchResult", "Bank", "FreeCompanyChest",
    };

    private static readonly TimeSpan AddonWindow = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, DateTime> addonSeen = new();

    // Sonstige Gil-Änderungen warten kurz, ob eine Retainer-Bewegung dazu passt (Ein-/Auszahlung).
    private readonly List<(long Delta, DateTime At, string Note, string Key)> pendingOther = new();
    private readonly List<(long Signed, DateTime At)> retainerMoves = new();

    private readonly UniversalisClient market;

    // Gehilfen-Angebote: beim ersten stabilen Auslesen nach dem Rufen wird mit der gespeicherten Liste verglichen.
    private sealed class PendingSale
    {
        public string Character = "";
        public string Retainer = "";
        public List<ListedItem> Sold = new();
        public DateTime At;
    }

    private readonly List<PendingSale> pendingSales = new();
    private DateTime nextListingCheck;
    private string? activeRetainer;
    private string listingSig = "";
    private int listingStable;
    private bool listingDiffDone;

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
                      Ledger ledger, Configuration config, UniversalisClient market)
    {
        this.market = market;
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

            // Beim Login kann kurz 0 gelesen werden: Der Tagesstartwert darf nie aus so einem Wert entstehen.
            if (gil == 0 && !HasSession) return;

            var ret = ReadRetainers(key);

            if (!HasSession || key != CharacterKey)
            {
                CharacterKey = key;
                HasSession = true;
                lastGil = lastRet = -1;
            }

            // "Heute": der Startwert des Tages bleibt bei Relog erhalten und wird erst bei einem neuen Tag zurückgesetzt.
            SessionStartGil = ledger.DayStart(key, gil);

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
            // Beim Teleport (Ladebildschirm) bleiben wartende Einträge erhalten und werden danach gebucht.
            fastLast = -1;
            return;
        }

        PollAddons(now);
        ProcessPendingOther(now);

        if (now >= nextListingCheck)
        {
            nextListingCheck = now.AddSeconds(1);
            CheckRetainerListings();
            ProcessPendingSales(now);
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
        var now = DateTime.UtcNow;
        var open = OpenAddons(now);
        var hovered = NormalizeItemId(gameGui.HoveredItem);

        // Für die Fehlersuche: zusätzlich alle weiteren bekannten Fenster, die gerade offen sind.
        var visibleNow = CandidateAddons.Where(IsAddonVisible).ToList();
        var shown = open.Union(visibleNow).ToList();
        recentGil.Insert(0, $"{(delta > 0 ? "+" : "")}{delta:N0} | {(shown.Count > 0 ? string.Join(", ", shown) : "-")} | item {hovered}");
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
        if (open.Contains("Shop") || open.Contains("InclusionShop"))
        {
            if (!config.TrackNpcShop) return;

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
            return;
        }

        // Marktbrett-Kauf: Gil sinkt, während die Marktbrett-Suche offen ist.
        if (delta < 0 && (open.Contains("ItemSearch") || open.Contains("ItemSearchResult")))
        {
            if (config.TrackMarketBuys)
            {
                ledger.AddEntry(new LedgerEntry
                {
                    Kind = EntryKind.Purchase,
                    Quantity = 1,
                    TotalGil = total,
                    Character = key,
                    Source = "Market",
                });
            }
            return;
        }

        // Retainer-Gil-Fenster und FC-Kasse: reine Umbuchungen, keine Einnahme/Ausgabe.
        if (open.Contains("Bank") || open.Contains("FreeCompanyChest")) return;

        // Alles andere (Quests, Post, Handel, Gegenstände …): kurz warten, ob eine Retainer-Bewegung dazu passt, dann als "Sonstiges" erfassen.
        if (config.TrackOtherGil)
            pendingOther.Add((delta, now, shown.Count > 0 ? string.Join(", ", shown) : "", key));
    }

    private void ProcessPendingOther(DateTime now)
    {
        if (pendingOther.Count == 0) return;
        retainerMoves.RemoveAll(m => (now - m.At).TotalSeconds > 30);

        for (var i = pendingOther.Count - 1; i >= 0; i--)
        {
            var p = pendingOther[i];
            if ((now - p.At).TotalSeconds < 2.2) continue;
            pendingOther.RemoveAt(i);

            // Passende Gegenbewegung beim Retainer (Ein-/Auszahlung) = Umbuchung.
            var idx = retainerMoves.FindIndex(m => m.Signed == -p.Delta && Math.Abs((m.At - p.At).TotalSeconds) <= 6);
            if (idx >= 0)
            {
                retainerMoves.RemoveAt(idx);
                continue;
            }

            ledger.AddEntry(new LedgerEntry
            {
                Kind = p.Delta > 0 ? EntryKind.Sale : EntryKind.Purchase,
                ItemName = "@other",
                Quantity = 1,
                TotalGil = Math.Abs(p.Delta),
                Character = p.Key,
                Source = "Other",
                Note = NoteFor(p),
            });
        }
    }

    /// <summary>Beschriftung: die passende Systemmeldung (z. B. Pflichtinstanz-Belohnung), sonst die offenen Fenster.</summary>
    private static string NoteFor((long Delta, DateTime At, string Note, string Key) p)
    {
        var msgs = GilMessages.Near(p.At, 5);
        return msgs.Count > 0 ? string.Join("  |  ", msgs) : p.Note;
    }

    // ---------- Fenster-Beobachtung ----------

    private void PollAddons(DateTime now)
    {
        foreach (var name in WatchedAddons)
            if (IsAddonVisible(name)) addonSeen[name] = now;
    }

    private List<string> OpenAddons(DateTime now)
    {
        var list = new List<string>();
        foreach (var kv in addonSeen)
            if (now - kv.Value <= AddonWindow) list.Add(kv.Key);
        return list;
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
        if (!ledger.Data.Retainers.TryGetValue(key, out var previous)) return;

        var now = DateTime.UtcNow;
        var bank = IsAddonVisible("Bank") || OpenAddons(now).Contains("Bank");
        foreach (var kv in current)
        {
            if (!previous.TryGetValue(kv.Key, out var before) || kv.Value == before) continue;

            var delta = kv.Value - before;

            // Auszahlung: der Charakter bekommt kurz danach denselben Betrag (wird dort als Umbuchung erkannt).
            if (delta < 0)
            {
                retainerMoves.Add((delta, now));
                continue;
            }

            // Einzahlung (Gil-Fenster offen oder passende Ausgabe des Charakters kurz vorher): keine Einnahme.
            if (bank || TakeMatchingOutflow(delta))
            {
                retainerMoves.Add((delta, now));
                continue;
            }

            if (!config.TrackRetainerSales) continue;

            ledger.AddEntry(new LedgerEntry
            {
                Kind = EntryKind.Sale,
                ItemName = "@retainer:" + kv.Key,
                Retainer = kv.Key,
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

    // ---------- Gehilfen-Angebote ----------

    public string? HomeWorldName()
    {
        if (!string.IsNullOrWhiteSpace(config.HomeWorld)) return config.HomeWorld.Trim();
        var hw = objectTable.LocalPlayer?.HomeWorld.ValueNullable;
        return hw?.Name.ToString();
    }

    /// <summary>
    /// Liest die Marktbrett-Angebote des gerufenen Gehilfen. Beim ersten stabilen Auslesen nach dem Rufen wird mit der
    /// beim letzten Besuch gespeicherten Liste verglichen: was fehlt, wurde verkauft (was du später selbst entfernst,
    /// wird nur als neue Liste gespeichert).
    /// </summary>
    private void CheckRetainerListings()
    {
        var key = CharacterKey ?? CurrentKey();
        if (key == null) return;

        var rm = RetainerManager.Instance();
        if (rm == null) return;

        var active = rm->GetActiveRetainer();
        if (active == null || active->RetainerId == 0)
        {
            activeRetainer = null;
            listingSig = "";
            listingStable = 0;
            listingDiffDone = false;
            return;
        }

        var name = active->NameString;
        if (string.IsNullOrEmpty(name)) return;
        if (name != activeRetainer)
        {
            activeRetainer = name;
            listingSig = "";
            listingStable = 0;
            listingDiffDone = false;
        }

        var im = InventoryManager.Instance();
        if (im == null) return;
        var cont = im->GetInventoryContainer(InventoryType.RetainerMarket);
        if (cont == null) return;

        var map = new Dictionary<(uint Id, bool Hq), int>();
        for (var i = 0; i < cont->Size; i++)
        {
            var it = cont->GetInventorySlot(i);
            if (it == null || it->ItemId == 0) continue;
            var hq = it->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);
            var k = (it->ItemId, hq);
            map.TryGetValue(k, out var q);
            map[k] = q + (int)it->Quantity;
        }

        var items = map.OrderBy(kv => kv.Key.Id).ThenBy(kv => kv.Key.Hq)
                       .Select(kv => new ListedItem { ItemId = kv.Key.Id, Hq = kv.Key.Hq, Qty = kv.Value })
                       .ToList();
        var sig = string.Join(";", items.Select(x => $"{x.ItemId}/{(x.Hq ? 1 : 0)}/{x.Qty}"));

        // Erst auswerten, wenn der Inhalt einige Sekunden unverändert ist (die Liste wird nach dem Rufen nachgeladen).
        if (sig != listingSig)
        {
            listingSig = sig;
            listingStable = 1;
            return;
        }
        listingStable++;
        if (listingStable < 3) return;

        // Eine leere Liste zählt nur, wenn das Angebotsfenster des Gehilfen offen war.
        if (items.Count == 0 && !IsAddonVisible("RetainerSellList")) return;

        var saved = ledger.GetListing(key, name);
        if (!listingDiffDone)
        {
            listingDiffDone = true;
            if (saved != null)
            {
                var sold = new List<ListedItem>();
                foreach (var s in saved.Items)
                {
                    var cur = items.FirstOrDefault(x => x.ItemId == s.ItemId && x.Hq == s.Hq)?.Qty ?? 0;
                    if (cur < s.Qty) sold.Add(new ListedItem { ItemId = s.ItemId, Hq = s.Hq, Qty = s.Qty - cur });
                }
                if (sold.Count > 0)
                    pendingSales.Add(new PendingSale { Character = key, Retainer = name, Sold = sold, At = DateTime.UtcNow });
            }
        }

        var same = saved != null && saved.Items.Count == items.Count &&
                   saved.Items.Zip(items, (a, b) => a.ItemId == b.ItemId && a.Hq == b.Hq && a.Qty == b.Qty).All(x => x);
        if (!same) ledger.SetListing(key, name, items);
    }

    /// <summary>
    /// Ordnet erkannte Verkäufe den Einnahmen des Gehilfen zu: der Sammelbetrag wird durch Einträge je Item ersetzt.
    /// Bei mehreren verschiedenen Items wird der Betrag nach Marktpreis (Universalis, Heimatwelt) verteilt.
    /// </summary>
    private void ProcessPendingSales(DateTime now)
    {
        for (var n = pendingSales.Count - 1; n >= 0; n--)
        {
            var ps = pendingSales[n];
            if ((now - ps.At).TotalMinutes > 10) { pendingSales.RemoveAt(n); continue; }

            var lumps = ledger.FindRetainerLumps(ps.Character, ps.Retainer);
            if (lumps.Count == 0) continue;

            var world = HomeWorldName();
            var prices = new double[ps.Sold.Count];
            var needWait = false;
            if (ps.Sold.Count > 1 && world != null)
            {
                for (var i = 0; i < ps.Sold.Count; i++)
                {
                    var info = market.Get(ps.Sold[i].ItemId, world);
                    if (info == null) { needWait = true; continue; }
                    if (!info.Ok) continue;
                    var p = ps.Sold[i].Hq ? info.MinHq : info.MinNq;
                    prices[i] = p > 0 ? p : info.AvgSold;
                }
                if (needWait && (now - ps.At).TotalSeconds < 20) continue;
            }

            var known = prices.Where(p => p > 0).ToList();
            var fallback = known.Count > 0 ? known.Average() : 1d;
            var weights = ps.Sold.Select((s, i) => s.Qty * (prices[i] > 0 ? prices[i] : fallback)).ToList();
            var wsum = weights.Sum();

            var net = lumps.Sum(l => l.TotalGil);
            var when = lumps.Max(l => l.TimeUtc);
            var sheet = data.GetExcelSheet<Item>();
            var adds = new List<LedgerEntry>();
            long used = 0;
            for (var i = 0; i < ps.Sold.Count; i++)
            {
                var s = ps.Sold[i];
                var total = i == ps.Sold.Count - 1 ? net - used : (long)Math.Round(net * weights[i] / wsum);
                used += total;
                var itemName = sheet != null && sheet.TryGetRow(s.ItemId, out var row) ? row.Name.ToString() : "";

                adds.Add(new LedgerEntry
                {
                    Kind = EntryKind.Sale,
                    ItemId = s.ItemId,
                    ItemName = itemName,
                    IsHq = s.Hq,
                    Quantity = Math.Max(1, s.Qty),
                    TotalGil = total,
                    Character = ps.Character,
                    Source = "Retainer",
                    Retainer = ps.Retainer,
                    TimeUtc = when,
                    Note = ps.Sold.Count > 1 ? L.T("note.estimated") : "",
                });
            }

            ledger.ReplaceEntries(lumps, adds);
            pendingSales.RemoveAt(n);
        }
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
    }
}
