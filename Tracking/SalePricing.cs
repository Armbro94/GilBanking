using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GilBanking.Tracking;

/// <summary>
/// Üblicher Verkaufspreis je verkauftem Item für die Aufteilung von Gehilfen-Einnahmen. Eigene Klasse (ohne unsafe),
/// weil hier asynchron auf Universalis gewartet wird.
/// </summary>
internal static class SalePricing
{
    /// <summary>Median der letzten Verkäufe (gleiche Qualität), sonst aggregated-Durchschnitt, sonst Mindestpreis.</summary>
    public static async Task<double[]> LoadAsync(UniversalisClient market, string world, List<ListedItem> sold)
    {
        var ids = sold.Select(s => s.ItemId).Distinct().ToList();
        Dictionary<uint, List<HistorySale>>? hist;
        Dictionary<uint, AggItem>? agg;
        try
        {
            hist = await market.GetHistoryAsync(world, ids).ConfigureAwait(false);
            agg = await market.GetAggregatedAsync(world, ids).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // z. B. Plugin wird gerade entladen: ohne Preise wird nach Menge aufgeteilt.
            return new double[sold.Count];
        }

        var prices = new double[sold.Count];
        for (var i = 0; i < sold.Count; i++)
        {
            var s = sold[i];
            List<HistorySale>? sales = null;
            hist?.TryGetValue(s.ItemId, out sales);

            var same = sales?.Where(x => x.Hq == s.Hq).Select(x => x.Price).ToList() ?? new List<double>();
            if (same.Count >= 2) { prices[i] = Median(same); continue; }

            if (agg != null && agg.TryGetValue(s.ItemId, out var a))
            {
                var q = s.Hq ? a.Hq : a.Nq;
                var p = q.Avg > 0 ? q.Avg : q.Median > 0 ? q.Median : q.Min;
                if (p > 0) { prices[i] = p; continue; }
            }

            if (same.Count == 1) { prices[i] = same[0]; continue; }
            var any = sales?.Select(x => x.Price).ToList();
            if (any is { Count: > 0 }) prices[i] = Median(any);
        }
        return prices;
    }

    private static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        var m = s.Count / 2;
        return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2d;
    }
}
