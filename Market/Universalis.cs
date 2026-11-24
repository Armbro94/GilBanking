using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GilBanking.Market;

public sealed class MarketInfo
{
    public uint ItemId { get; init; }
    public string Scope { get; init; } = "";
    /// <summary>Zeitpunkt des letzten Abrufversuchs.</summary>
    public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Zeitpunkt, zu dem diese Daten tatsächlich von Universalis geholt wurden.</summary>
    public DateTime DataUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Veraltete Daten aus dem Offline-Cache (Universalis war nicht erreichbar).</summary>
    public bool Stale { get; set; }

    public MarketInfo Clone() => (MarketInfo)MemberwiseClone();
    public bool Ok { get; init; }
    public string? Error { get; init; }

    public double MinNq { get; init; }
    public double MinHq { get; init; }
    public double AvgCurrent { get; init; }
    public double AvgSold { get; init; }
    public double SaleVelocity { get; init; }
    public DateTime? LastUpload { get; init; }

    /// <summary>Günstigstes Angebot (bei Rechenzentrums-Abfrage mit Weltname).</summary>
    public double CheapestPrice { get; init; }
    public string CheapestWorld { get; init; } = "";

    /// <summary>Günstigstes NQ- und HQ-Angebot im Abfragebereich (mit Welt), damit beide Qualitäten getrennt verglichen werden.</summary>
    public double CheapestNqPrice { get; init; }
    public string CheapestNqWorld { get; init; } = "";
    public double CheapestHqPrice { get; init; }
    public string CheapestHqWorld { get; init; } = "";

    public bool NoData { get; init; }
}

/// <summary>Verdichtete Preise einer Qualität aus dem Universalis-Endpunkt "aggregated".</summary>
public sealed class AggQuality
{
    public double Min;       // günstigstes Angebot
    public double Median;    // Median der aktuellen Angebote
    public double Recent;    // letzter Verkaufspreis
    public double Avg;       // durchschnittlicher Verkaufspreis
    public double Velocity;  // Verkäufe pro Tag
    public bool Has => Min > 0 || Median > 0 || Avg > 0 || Recent > 0;
}

public sealed class AggItem
{
    public AggQuality Nq { get; } = new();
    public AggQuality Hq { get; } = new();
    public DateTime? Upload { get; set; }
    public bool Has => Nq.Has || Hq.Has;
}

/// <summary>Ein Verkauf aus der Universalis-Verlaufsliste.</summary>
public readonly record struct HistorySale(bool Hq, double Price, int Qty);

/// <summary>
/// Der gemeinsame Zugriff auf die öffentliche Universalis-API (https://universalis.app) für Tooltip, Schnäppchen-Suche
/// und Gehilfen-Schätzung. Alles läuft im Hintergrund mit Cache; der Spiel-Thread wird nie blockiert.
///
/// Gemeinsame Drosselung: höchstens 2 Anfragen gleichzeitig, danach je 150 ms Pause (ca. 13 Anfragen/s bei einer
/// Obergrenze von etwa 25/s). Bei HTTP 429 wird für die Dauer von Retry-After (mindestens 30 s) gar nicht mehr gefragt.
/// Es wird nichts hochgeladen.
/// </summary>
public sealed class UniversalisClient : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ErrorTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HistoryTtl = TimeSpan.FromMinutes(30);

    private static readonly HashSet<string> Regions = new(StringComparer.OrdinalIgnoreCase)
        { "Europe", "North-America", "Japan", "Oceania" };

    private readonly HttpClient http;
    private readonly ConcurrentDictionary<string, MarketInfo> cache = new();
    private readonly ConcurrentDictionary<string, byte> inflight = new();
    private readonly ConcurrentDictionary<string, (DateTime At, List<HistorySale> Sales)> history = new();
    private readonly SemaphoreSlim gate = new(2);
    private readonly CancellationTokenSource cts = new();
    private readonly PriceCache? prices;
    private DateTime backoffUntil = DateTime.MinValue;
    private volatile bool disposed;

    public UniversalisClient(PriceCache? prices = null)
    {
        this.prices = prices;
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GilBanking-DalamudPlugin/0.1");
    }

    // ---------- Gemeinsamer Zugriff mit Drosselung ----------

    /// <summary>Ruft eine URL ab (gedrosselt). Status 429, solange wegen der Ratenbegrenzung pausiert wird. Netzwerkfehler werfen.</summary>
    public async Task<(int Status, string Body)> SendAsync(string url, CancellationToken ct = default)
    {
        // Nach dem Entladen des Plugins laufen noch Hintergrundaufgaben aus: nichts mehr abfragen, nichts werfen.
        if (disposed) return (0, "");
        if (DateTime.UtcNow < backoffUntil) return (429, "");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow < backoffUntil) return (429, "");

            using var resp = await http.GetAsync(url, linked.Token).ConfigureAwait(false);
            var status = (int)resp.StatusCode;
            if (status == 429)
            {
                var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                if (wait < TimeSpan.FromSeconds(30)) wait = TimeSpan.FromSeconds(30);
                backoffUntil = DateTime.UtcNow + wait;
                return (429, "");
            }

            var body = resp.IsSuccessStatusCode
                ? await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false)
                : "";
            await Task.Delay(150, linked.Token).ConfigureAwait(false); // sanfte Drosselung
            return (status, body);
        }
        finally { gate.Release(); }
    }

    // ---------- Tooltip: Werte mit Cache ----------

    /// <summary>
    /// Liefert den gecachten Wert (evtl. veraltet) und stößt bei Bedarf eine Aktualisierung an.
    /// lite = nur Preise/Verkäufe der Welt aus dem leichten "aggregated"-Endpunkt (für Heimatwelt-Block),
    /// sonst mit Angeboten (Welt des günstigsten Angebots, Zeitpunkt des letzten Uploads).
    /// </summary>
    public MarketInfo? Get(uint itemId, string scope, bool lite = false)
    {
        var key = (lite ? "agg|" : "") + $"{scope}/{itemId}";
        if (!cache.TryGetValue(key, out var info))
        {
            // Nach einem Neustart: zuerst die gespeicherten Werte zeigen, parallel wird neu geladen.
            var old = prices?.Get(key);
            if (old != null)
            {
                info = Copy(old, (DateTime.UtcNow - old.FetchedUtc) > Ttl, null);
                cache[key] = info;
            }
        }

        if (info != null)
        {
            var age = DateTime.UtcNow - info.FetchedUtc;
            if (age > (info.Ok && !info.Stale ? Ttl : ErrorTtl)) Request(key, itemId, scope, lite);
            return info;
        }
        Request(key, itemId, scope, lite);
        return null;
    }

    private static MarketInfo Copy(MarketInfo old, bool stale, DateTime? attempt)
    {
        var c = old.Clone();
        c.Stale = stale;
        if (attempt != null) c.FetchedUtc = attempt.Value;
        return c;
    }

    /// <summary>Ergebnis ablegen; bei Fehlern auf den Offline-Cache zurückgreifen.</summary>
    private void Store(string key, MarketInfo result)
    {
        if (result.Ok)
        {
            prices?.Put(key, result);
            cache[key] = result;
            return;
        }
        var old = prices?.Get(key);
        cache[key] = old != null ? Copy(old, true, DateTime.UtcNow) : result;
    }

    private void Request(string key, uint itemId, string scope, bool lite)
    {
        if (!inflight.TryAdd(key, 0)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                Store(key, lite
                    ? await FetchLite(itemId, scope).ConfigureAwait(false)
                    : await Fetch(itemId, scope).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { /* Plugin wird entladen */ }
            catch (Exception ex)
            {
                Store(key, new MarketInfo { ItemId = itemId, Scope = scope, Ok = false, Error = ex.Message });
            }
            finally
            {
                inflight.TryRemove(key, out _);
            }
        });
    }

    // ---------- aggregated ----------

    /// <summary>Welche Ebene der Antwort gehört zum Abfragebereich: "world", "dc" oder "region".</summary>
    private static string LevelFor(string scope) =>
        Worlds.DataCenterOf(scope) != null ? "world" : Regions.Contains(scope) ? "region" : "dc";

    private static double Lv(JsonElement q, string prop, string level, string field) =>
        q.ValueKind == JsonValueKind.Object && q.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Object
        && p.TryGetProperty(level, out var l) && l.ValueKind == JsonValueKind.Object
            ? D(l, field)
            : 0;

    private static void FillQuality(AggQuality t, JsonElement q, string level)
    {
        t.Min = Lv(q, "minListing", level, "price");
        t.Median = Lv(q, "medianListing", level, "price");
        t.Recent = Lv(q, "recentPurchase", level, "price");
        t.Avg = Lv(q, "averageSalePrice", level, "price");
        t.Velocity = Lv(q, "dailySaleVelocity", level, "quantity");
    }

    /// <summary>
    /// Leichter Sammelabruf (bis 100 Items) für Preise und Verkaufsgeschwindigkeit ohne Angebotslisten.
    /// Gibt null zurück, wenn die Anfrage oder die Antwort nicht brauchbar war (Aufrufer weichen dann auf die
    /// ausführlichere Abfrage aus).
    /// </summary>
    public async Task<Dictionary<uint, AggItem>?> GetAggregatedAsync(string scope, IReadOnlyList<uint> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new Dictionary<uint, AggItem>();
        try
        {
            var url = $"https://universalis.app/api/v2/aggregated/{Uri.EscapeDataString(scope)}/{string.Join(",", ids.Take(100))}";
            var (status, body) = await SendAsync(url, ct).ConfigureAwait(false);
            if (status != 200 || body.Length == 0) return null;

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return null;

            var level = LevelFor(scope);
            var map = new Dictionary<uint, AggItem>();
            foreach (var r in results.EnumerateArray())
            {
                if (!r.TryGetProperty("itemId", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                var item = new AggItem();
                if (r.TryGetProperty("nq", out var nq)) FillQuality(item.Nq, nq, level);
                if (r.TryGetProperty("hq", out var hq)) FillQuality(item.Hq, hq, level);

                if (r.TryGetProperty("worldUploadTimes", out var wut) && wut.ValueKind == JsonValueKind.Array)
                {
                    double best = 0;
                    foreach (var w in wut.EnumerateArray()) best = Math.Max(best, D(w, "timestamp"));
                    if (best > 0)
                    {
                        // Millisekunden oder Sekunden: beides abfangen.
                        item.Upload = best > 1e11
                            ? DateTimeOffset.FromUnixTimeMilliseconds((long)best).UtcDateTime
                            : DateTimeOffset.FromUnixTimeSeconds((long)best).UtcDateTime;
                    }
                }
                map[idEl.GetUInt32()] = item;
            }
            return map;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private async Task<MarketInfo> FetchLite(uint itemId, string scope)
    {
        var agg = await GetAggregatedAsync(scope, new[] { itemId }, cts.Token).ConfigureAwait(false);
        if (agg != null && agg.TryGetValue(itemId, out var a) && a.Has)
        {
            var avgs = new[] { a.Nq.Avg, a.Hq.Avg }.Where(x => x > 0).ToList();
            var mins = new[] { a.Nq.Min, a.Hq.Min }.Where(x => x > 0).ToList();
            return new MarketInfo
            {
                ItemId = itemId,
                Scope = scope,
                Ok = true,
                MinNq = a.Nq.Min,
                MinHq = a.Hq.Min,
                AvgSold = avgs.Count > 0 ? avgs.Average() : 0,
                SaleVelocity = a.Nq.Velocity + a.Hq.Velocity,
                LastUpload = a.Upload,
                CheapestPrice = mins.Count > 0 ? mins.Min() : 0,
                CheapestWorld = mins.Count > 0 ? scope : "",
                CheapestNqPrice = a.Nq.Min,
                CheapestNqWorld = a.Nq.Min > 0 ? scope : "",
                CheapestHqPrice = a.Hq.Min,
                CheapestHqWorld = a.Hq.Min > 0 ? scope : "",
            };
        }

        // Keine brauchbare Antwort (oder "keine Daten"): mit der ausführlichen Abfrage bestätigen.
        return await Fetch(itemId, scope).ConfigureAwait(false);
    }

    // ---------- Verlauf (nur auf Anfrage) ----------

    /// <summary>
    /// Letzte Verkäufe je Item (bis 20) für eine Welt. Nur für die Aufteilung von Gehilfen-Einnahmen gedacht,
    /// 30 Minuten gecacht, nie beim Hovern. Null bei Fehlern.
    /// </summary>
    public async Task<Dictionary<uint, List<HistorySale>>?> GetHistoryAsync(string scope, IReadOnlyList<uint> ids, CancellationToken ct = default)
    {
        var result = new Dictionary<uint, List<HistorySale>>();
        var missing = new List<uint>();
        foreach (var id in ids.Distinct())
        {
            if (history.TryGetValue($"{scope}/{id}", out var h) && DateTime.UtcNow - h.At < HistoryTtl) result[id] = h.Sales;
            else missing.Add(id);
        }
        if (missing.Count == 0) return result;

        try
        {
            var url = $"https://universalis.app/api/v2/history/{Uri.EscapeDataString(scope)}/{string.Join(",", missing.Take(100))}?entriesToReturn=20";
            var (status, body) = await SendAsync(url, ct).ConfigureAwait(false);
            if (status != 200 || body.Length == 0) return result.Count > 0 ? result : null;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var parsed = new Dictionary<uint, List<HistorySale>>();
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in items.EnumerateObject())
                    if (uint.TryParse(p.Name, out var id)) parsed[id] = ParseSales(p.Value);
            }
            else if (missing.Count == 1)
            {
                parsed[missing[0]] = ParseSales(root); // Einzelabruf hat eine andere Form
            }

            foreach (var kv in parsed)
            {
                history[$"{scope}/{kv.Key}"] = (DateTime.UtcNow, kv.Value);
                result[kv.Key] = kv.Value;
            }
            return result;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
        catch { return result.Count > 0 ? result : null; }
    }

    private static List<HistorySale> ParseSales(JsonElement e)
    {
        var list = new List<HistorySale>();
        if (e.TryGetProperty("entries", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in arr.EnumerateArray())
            {
                var price = D(x, "pricePerUnit");
                if (price <= 0) continue;
                list.Add(new HistorySale(
                    x.TryGetProperty("hq", out var hq) && hq.ValueKind == JsonValueKind.True,
                    price,
                    Math.Max(1, (int)D(x, "quantity"))));
            }
        }
        return list;
    }

    // ---------- Ausführliche Abfrage (mit Angeboten) ----------

    private async Task<MarketInfo> Fetch(uint itemId, string scope)
    {
        // Mehrere Angebote (statt nur einem): so ist das günstigste NQ- und HQ-Angebot meist schon enthalten
        // und die zusätzlichen Abfragen je Qualität entfallen.
        var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(scope)}/{itemId}?listings=10&entries=0";
        try
        {
            var (status, json) = await SendAsync(url, cts.Token).ConfigureAwait(false);
            if (status == 404)
                return new MarketInfo { ItemId = itemId, Scope = scope, Ok = true, NoData = true };
            if (status != 200)
                return new MarketInfo { ItemId = itemId, Scope = scope, Ok = false, Error = $"HTTP {status}" };

            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            double cheapest = 0;
            var cheapestWorld = "";
            double nqPrice = 0, hqPrice = 0;
            var nqWorld = "";
            var hqWorld = "";
            if (r.TryGetProperty("listings", out var ls) && ls.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in ls.EnumerateArray())
                {
                    var price = D(l, "pricePerUnit");
                    if (price <= 0) continue;
                    var isHq = l.TryGetProperty("hq", out var hqEl) && hqEl.ValueKind == JsonValueKind.True;
                    var world = l.TryGetProperty("worldName", out var wn) && wn.ValueKind == JsonValueKind.String ? wn.GetString() ?? "" : "";

                    if (cheapest <= 0) { cheapest = price; cheapestWorld = world; }
                    if (isHq && hqPrice <= 0) { hqPrice = price; hqWorld = world; }
                    if (!isHq && nqPrice <= 0) { nqPrice = price; nqWorld = world; }
                }
            }

            DateTime? last = null;
            var up = D(r, "lastUploadTime");
            if (up > 0) last = DateTimeOffset.FromUnixTimeMilliseconds((long)up).UtcDateTime;

            var minNq = D(r, "minPriceNQ");
            var minHq = D(r, "minPriceHQ");
            var avgSold = D(r, "averagePrice");
            var noData = minNq <= 0 && minHq <= 0 && cheapest <= 0 && avgSold <= 0;

            // Einzelne Welt: das Angebot der anderen Qualität liegt auf derselben Welt.
            // Rechenzentrum/Region: nur wenn es in den ersten Angeboten fehlt, wird gezielt nachgefragt.
            var singleWorld = Worlds.DataCenterOf(scope) != null;
            if (nqPrice <= 0 && minNq > 0)
            {
                if (singleWorld) { nqPrice = minNq; nqWorld = scope; }
                else { (nqPrice, nqWorld) = await CheapestOfQuality(itemId, scope, false).ConfigureAwait(false); }
            }
            if (hqPrice <= 0 && minHq > 0)
            {
                if (singleWorld) { hqPrice = minHq; hqWorld = scope; }
                else { (hqPrice, hqWorld) = await CheapestOfQuality(itemId, scope, true).ConfigureAwait(false); }
            }

            return new MarketInfo
            {
                ItemId = itemId,
                Scope = scope,
                Ok = true,
                NoData = noData,
                MinNq = minNq,
                MinHq = minHq,
                AvgCurrent = D(r, "currentAveragePrice"),
                AvgSold = avgSold,
                SaleVelocity = D(r, "regularSaleVelocity"),
                LastUpload = last,
                CheapestPrice = cheapest,
                CheapestWorld = cheapestWorld,
                CheapestNqPrice = nqPrice,
                CheapestNqWorld = nqWorld,
                CheapestHqPrice = hqPrice,
                CheapestHqWorld = hqWorld,
            };
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new MarketInfo { ItemId = itemId, Scope = scope, Ok = false, Error = ex.Message };
        }
    }

    /// <summary>Günstigstes Angebot einer bestimmten Qualität im Abfragebereich (Preis und Welt).</summary>
    private async Task<(double Price, string World)> CheapestOfQuality(uint itemId, string scope, bool hq)
    {
        try
        {
            var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(scope)}/{itemId}?listings=1&entries=0&hq={(hq ? "true" : "false")}";
            var (status, json) = await SendAsync(url, cts.Token).ConfigureAwait(false);
            if (status != 200) return (0, "");

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("listings", out var ls) && ls.ValueKind == JsonValueKind.Array && ls.GetArrayLength() > 0)
            {
                var l = ls[0];
                var world = l.TryGetProperty("worldName", out var wn) && wn.ValueKind == JsonValueKind.String
                    ? wn.GetString() ?? ""
                    : "";
                return (D(l, "pricePerUnit"), world);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
        catch { /* die zweite Abfrage ist optional */ }

        return (0, "");
    }

    private static double D(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    public void Dispose()
    {
        disposed = true;
        cts.Cancel();
        // cts und http werden bewusst nicht freigegeben: laufende Aufgaben greifen noch kurz darauf zu
        // (Freigabe würde dort ObjectDisposedException auslösen). Der Garbage Collector räumt beides auf.
    }
}
