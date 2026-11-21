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
    public DateTime FetchedUtc { get; init; } = DateTime.UtcNow;
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

    public UniversalisClient()
    {
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GilBanking-DalamudPlugin/0.1");
    }

    /// <summary>Liefert den gecachten Wert (evtl. veraltet) und stößt bei Bedarf eine Aktualisierung an.</summary>
    public MarketInfo? Get(uint itemId, string scope)
    {
        var key = $"{scope}/{itemId}";
        if (cache.TryGetValue(key, out var info))
        {
            var age = DateTime.UtcNow - info.FetchedUtc;
            if (age > (info.Ok ? Ttl : ErrorTtl)) Request(key, itemId, scope);
            return info;
        }
        Request(key, itemId, scope);
        return null;
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
                    cache[key] = await Fetch(itemId, scope).ConfigureAwait(false);
                    await Task.Delay(120, cts.Token).ConfigureAwait(false); // sanfte Drosselung
                }
                finally { gate.Release(); }
            }
            catch (OperationCanceledException) { /* Plugin wird entladen */ }
            catch (Exception ex)
            {
                cache[key] = new MarketInfo { ItemId = itemId, Scope = scope, Ok = false, Error = ex.Message };
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
            if (r.TryGetProperty("listings", out var ls) && ls.ValueKind == JsonValueKind.Array && ls.GetArrayLength() > 0)
            {
                var l = ls[0];
                cheapest = D(l, "pricePerUnit");
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
            };
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new MarketInfo { ItemId = itemId, Scope = scope, Ok = false, Error = ex.Message };
        }
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
