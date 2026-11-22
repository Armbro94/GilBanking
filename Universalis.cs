using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GilBanking;

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

/// <summary>
/// Fragt die öffentliche Universalis-API (https://universalis.app) ab.
/// Alles läuft im Hintergrund mit Cache und Drosselung; der Spiel-Thread wird nie blockiert.
/// </summary>
public sealed class UniversalisClient : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ErrorTtl = TimeSpan.FromSeconds(60);

    private readonly HttpClient http;
    private readonly ConcurrentDictionary<string, MarketInfo> cache = new();
    private readonly ConcurrentDictionary<string, byte> inflight = new();
    private readonly SemaphoreSlim gate = new(3);
    private readonly CancellationTokenSource cts = new();

    private readonly PriceCache? prices;

    public UniversalisClient(PriceCache? prices = null)
    {
        this.prices = prices;
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GilBanking-DalamudPlugin/0.1");
    }

    /// <summary>Liefert den gecachten Wert (evtl. veraltet) und stößt bei Bedarf eine Aktualisierung an.</summary>
    public MarketInfo? Get(uint itemId, string scope)
    {
        var key = $"{scope}/{itemId}";
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
            if (age > (info.Ok && !info.Stale ? Ttl : ErrorTtl)) Request(key, itemId, scope);
            return info;
        }
        Request(key, itemId, scope);
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

    private void Request(string key, uint itemId, string scope)
    {
        if (!inflight.TryAdd(key, 0)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await gate.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    Store(key, await Fetch(itemId, scope).ConfigureAwait(false));
                    await Task.Delay(120, cts.Token).ConfigureAwait(false); // sanfte Drosselung
                }
                finally { gate.Release(); }
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

    private async Task<MarketInfo> Fetch(uint itemId, string scope)
    {
        var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(scope)}/{itemId}?listings=1&entries=0";
        try
        {
            using var resp = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new MarketInfo { ItemId = itemId, Scope = scope, Ok = true, NoData = true };
            if (!resp.IsSuccessStatusCode)
                return new MarketInfo { ItemId = itemId, Scope = scope, Ok = false, Error = $"HTTP {(int)resp.StatusCode}" };

            var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            double cheapest = 0;
            var cheapestWorld = "";
            var cheapestIsHq = false;
            if (r.TryGetProperty("listings", out var ls) && ls.ValueKind == JsonValueKind.Array && ls.GetArrayLength() > 0)
            {
                var l = ls[0];
                cheapest = D(l, "pricePerUnit");
                cheapestIsHq = l.TryGetProperty("hq", out var hqEl) && hqEl.ValueKind == JsonValueKind.True;
                if (l.TryGetProperty("worldName", out var wn) && wn.ValueKind == JsonValueKind.String)
                    cheapestWorld = wn.GetString() ?? "";
            }

            DateTime? last = null;
            var up = D(r, "lastUploadTime");
            if (up > 0) last = DateTimeOffset.FromUnixTimeMilliseconds((long)up).UtcDateTime;

            var minNq = D(r, "minPriceNQ");
            var minHq = D(r, "minPriceHQ");
            var avgSold = D(r, "averagePrice");
            var noData = minNq <= 0 && minHq <= 0 && cheapest <= 0 && avgSold <= 0;

            // Günstigstes Angebot je Qualität (mit Welt).
            double nqPrice = 0, hqPrice = 0;
            var nqWorld = "";
            var hqWorld = "";
            if (cheapest > 0)
            {
                if (cheapestIsHq) { hqPrice = cheapest; hqWorld = cheapestWorld; }
                else { nqPrice = cheapest; nqWorld = cheapestWorld; }
            }

            // Einzelne Welt: das Angebot der anderen Qualität liegt auf derselben Welt.
            // Rechenzentrum/Region: das günstigste Angebot der anderen Qualität wird gezielt nachgefragt.
            var singleWorld = string.Equals(cheapestWorld, scope, StringComparison.OrdinalIgnoreCase);
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
            using var resp = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (0, "");

            var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
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
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    public void Dispose()
    {
        cts.Cancel();
        http.Dispose();
        cts.Dispose();
    }
}
