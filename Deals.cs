using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GilBanking;

public sealed class DealRow
{
    public uint ItemId { get; init; }
    public string Name { get; init; } = "";
    public bool Hq { get; init; }
    public string BuyWorld { get; init; } = "";
    public long BuyPrice { get; init; }
    public int BuyQty { get; init; }
    public long HomePrice { get; init; }
    public bool HomeIsAverage { get; init; }
    public double Velocity { get; init; }
    public long Profit { get; init; }
    public double Margin { get; init; }
    public DateTime? Updated { get; init; }
    public long TotalProfit => Profit * Math.Max(1, BuyQty);
}

public sealed class WatchRow
{
    public uint ItemId { get; init; }
    public string Name { get; init; } = "";
    public int Quality { get; init; }
    public long Wish { get; init; }
    public bool Found { get; init; }
    public bool Hq { get; init; }
    public string World { get; init; } = "";
    public long Price { get; init; }
    public int Qty { get; init; }
    public DateTime? Updated { get; init; }
    public bool Hit => Found && Wish > 0 && Price <= Wish;
}

public sealed class DealRequest
{
    public List<WatchItem> Watch { get; init; } = new();
    public string Scope { get; init; } = "";
    public string HomeWorld { get; init; } = "";
    public string? HomeDc { get; init; }
    public List<uint> OwnIds { get; init; } = new();
    public bool UseRecent { get; init; }
    public long MinProfit { get; init; }
    public double MinMargin { get; init; }
    public double MinVelocity { get; init; }
    public int CooldownSeconds { get; init; } = 60;
}

/// <summary>
/// Sucht Items, die auf einer anderen Welt deutlich günstiger angeboten werden als auf der Heimatwelt (Universalis).
/// Rein lesend, nur wenige Anfragen pro Durchlauf (je ein Sammelabruf für Suchbereich und Heimatwelt),
/// danach gilt eine einstellbare Wartezeit bis zum nächsten Durchlauf.
/// </summary>
public sealed class DealScanner : IDisposable
{
    private sealed class Listing
    {
        public double Price;
        public int Qty;
        public bool Hq;
        public string World = "";
    }

    private sealed class ItemMarket
    {
        public List<Listing> Listings = new();
        public double AvgNq, AvgHq, VelNq, VelHq, Vel;
        public DateTime? Upload;
    }

    private readonly IDataManager data;
    private readonly HttpClient http;
    private readonly CancellationTokenSource cts = new();

    public DealScanner(IDataManager data)
    {
        this.data = data;
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GilBanking-DalamudPlugin/0.1");
    }

    public volatile bool Running;
    public string StatusKey = "";
    public string StatusArg = "";
    public List<DealRow> Rows { get; private set; } = new();
    public List<WatchRow> WatchRows { get; private set; } = new();
    public DateTime? LastScanUtc { get; private set; }
    public DateTime NextAllowedUtc { get; private set; } = DateTime.MinValue;
    public int Version { get; private set; }

    public bool CanStart => !Running && DateTime.UtcNow >= NextAllowedUtc;
    public double SecondsUntilAllowed => Math.Max(0, (NextAllowedUtc - DateTime.UtcNow).TotalSeconds);

    public void Start(DealRequest req)
    {
        if (!CanStart) return;
        Running = true;
        SetStatus("deals.st.collect");
        _ = Task.Run(() => Run(req));
    }

    private void SetStatus(string key, string arg = "")
    {
        StatusKey = key;
        StatusArg = arg;
    }

    private async Task Run(DealRequest req)
    {
        try
        {
            var ids = req.Watch.Select(w => w.ItemId).Distinct().ToList();
            foreach (var id in req.OwnIds)
                if (!ids.Contains(id)) ids.Add(id);
            if (req.UseRecent)
            {
                foreach (var id in await RecentIds(req))
                    if (!ids.Contains(id)) ids.Add(id);
            }
            ids = ids.Take(100).ToList();
            if (ids.Count == 0)
            {
                SetStatus("deals.st.none");
                return;
            }

            SetStatus("deals.st.load", ids.Count.ToString());
            var scopeItems = await FetchItems(req.Scope, ids);
            var homeItems = await FetchItems(req.HomeWorld, ids);

            var sheet = data.GetExcelSheet<Item>();

            // Watchlist: günstigstes passendes Angebot je beobachtetem Item
            var watchRows = new List<WatchRow>();
            foreach (var w in req.Watch)
            {
                scopeItems.TryGetValue(w.ItemId, out var wsc);
                Listing? best = wsc?.Listings
                    .Where(l => l.Price > 0 && (w.Quality == 0 || (w.Quality == 1 && !l.Hq) || (w.Quality == 2 && l.Hq)))
                    .OrderBy(l => l.Price)
                    .FirstOrDefault();
                var wname = sheet != null && sheet.TryGetRow(w.ItemId, out var wrow) ? wrow.Name.ToString() : $"#{w.ItemId}";
                watchRows.Add(new WatchRow
                {
                    ItemId = w.ItemId,
                    Name = wname,
                    Quality = w.Quality,
                    Wish = w.MaxPrice,
                    Found = best != null,
                    Hq = best?.Hq ?? false,
                    World = best?.World ?? "",
                    Price = (long)(best?.Price ?? 0),
                    Qty = best?.Qty ?? 0,
                    Updated = wsc?.Upload,
                });
            }
            WatchRows = watchRows;

            var rows = new List<DealRow>();
            foreach (var id in ids)
            {
                if (!scopeItems.TryGetValue(id, out var sc)) continue;
                homeItems.TryGetValue(id, out var home);

                var name = sheet != null && sheet.TryGetRow(id, out var row) ? row.Name.ToString() : $"#{id}";
                var canHq = sheet != null && sheet.TryGetRow(id, out var row2) && row2.CanBeHq;

                foreach (var hq in canHq ? new[] { false, true } : new[] { false })
                {
                    var other = sc.Listings
                        .Where(l => l.Hq == hq && !string.Equals(l.World, req.HomeWorld, StringComparison.OrdinalIgnoreCase) && l.Price > 0)
                        .OrderBy(l => l.Price)
                        .FirstOrDefault();
                    if (other == null) continue;

                    double homeMin = 0, homeAvg = 0, vel = 0;
                    if (home != null)
                    {
                        var hl = home.Listings.Where(l => l.Hq == hq && l.Price > 0).Select(l => l.Price).ToList();
                        homeMin = hl.Count > 0 ? hl.Min() : 0;
                        homeAvg = hq ? home.AvgHq : home.AvgNq;
                        vel = hq ? home.VelHq : home.VelNq;
                        if (vel <= 0) vel = home.Vel;
                    }

                    // Zielpreis: günstigstes Angebot auf der Heimatwelt, aber nie weit über dem üblichen Verkaufspreis
                    // (einzelne überteuerte Angebote würden sonst Gewinne vortäuschen).
                    double target;
                    bool isAvg;
                    if (homeMin > 0)
                    {
                        target = homeAvg > 0 ? Math.Min(homeMin, homeAvg * 1.5) : homeMin;
                        isAvg = false;
                    }
                    else if (homeAvg > 0)
                    {
                        target = homeAvg;
                        isAvg = true;
                    }
                    else continue;

                    var profit = (long)Math.Floor(target * 0.95 - other.Price); // 5 % Marktsteuer
                    var margin = other.Price > 0 ? profit / other.Price * 100d : 0;
                    if (profit < req.MinProfit || margin < req.MinMargin || vel < req.MinVelocity) continue;

                    rows.Add(new DealRow
                    {
                        ItemId = id,
                        Name = name,
                        Hq = hq,
                        BuyWorld = other.World,
                        BuyPrice = (long)other.Price,
                        BuyQty = other.Qty,
                        HomePrice = (long)target,
                        HomeIsAverage = isAvg,
                        Velocity = vel,
                        Profit = profit,
                        Margin = margin,
                        Updated = sc.Upload,
                    });
                }
            }

            Rows = rows.OrderByDescending(r => r.TotalProfit).Take(200).ToList();
            LastScanUtc = DateTime.UtcNow;
            Version++;
            SetStatus(rows.Count > 0 ? "deals.st.done" : "deals.st.noDeals", rows.Count.ToString());
        }
        catch (OperationCanceledException) { /* Plugin wird entladen */ }
        catch (Exception ex)
        {
            SetStatus("deals.st.error", ex.Message);
        }
        finally
        {
            NextAllowedUtc = DateTime.UtcNow.AddSeconds(Math.Max(15, req.CooldownSeconds));
            Running = false;
        }
    }

    /// <summary>Zuletzt aktualisierte Items im Rechenzentrum: dort hat sich gerade etwas an den Angeboten getan.</summary>
    private async Task<List<uint>> RecentIds(DealRequest req)
    {
        var result = new List<uint>();
        try
        {
            var q = !string.IsNullOrEmpty(req.HomeDc)
                ? $"dcName={Uri.EscapeDataString(req.HomeDc)}"
                : $"world={Uri.EscapeDataString(req.HomeWorld)}";
            var url = $"https://universalis.app/api/v2/extra/stats/most-recently-updated?{q}&entries=60";
            using var resp = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return result;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
            if (doc.RootElement.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in arr.EnumerateArray())
                    if (it.TryGetProperty("itemID", out var v) && v.ValueKind == JsonValueKind.Number)
                        result.Add(v.GetUInt32());
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
        catch { /* optionale Quelle */ }
        return result;
    }

    private async Task<Dictionary<uint, ItemMarket>> FetchItems(string scope, List<uint> ids)
    {
        var result = new Dictionary<uint, ItemMarket>();
        var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(scope)}/{string.Join(",", ids)}?listings=10&entries=0";
        using var resp = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
        var root = doc.RootElement;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in items.EnumerateObject())
                if (uint.TryParse(p.Name, out var id)) result[id] = Parse(p.Value);
        }
        else if (ids.Count == 1)
        {
            result[ids[0]] = Parse(root); // Einzelabruf hat eine andere Form
        }
        return result;
    }

    private static ItemMarket Parse(JsonElement e)
    {
        var m = new ItemMarket
        {
            AvgNq = D(e, "averagePriceNQ"),
            AvgHq = D(e, "averagePriceHQ"),
            VelNq = D(e, "nqSaleVelocity"),
            VelHq = D(e, "hqSaleVelocity"),
            Vel = D(e, "regularSaleVelocity"),
        };
        var up = D(e, "lastUploadTime");
        if (up > 0) m.Upload = DateTimeOffset.FromUnixTimeMilliseconds((long)up).UtcDateTime;

        if (e.TryGetProperty("listings", out var ls) && ls.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in ls.EnumerateArray())
            {
                m.Listings.Add(new Listing
                {
                    Price = D(l, "pricePerUnit"),
                    Qty = (int)D(l, "quantity"),
                    Hq = l.TryGetProperty("hq", out var hq) && hq.ValueKind == JsonValueKind.True,
                    World = l.TryGetProperty("worldName", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() ?? "" : "",
                });
            }
        }
        return m;
    }

    private static double D(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    public void Dispose()
    {
        cts.Cancel();
        http.Dispose();
        cts.Dispose();
    }
}
