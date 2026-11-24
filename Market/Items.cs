using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GilBanking.Market;

/// <summary>Hilfsfunktionen für Item-Namen (Suche in der Watchlist, Erkennung des Marktbrett-Items).</summary>
internal static class Items
{
    private static IDataManager? data;
    private static readonly object Gate = new();
    private static List<(uint Id, string Name)>? tradable;
    private static Dictionary<string, uint>? byName;

    public static void Init(IDataManager d)
    {
        data = d;
        _ = Task.Run(Build); // im Hintergrund vorbereiten, damit die erste Suche nicht ruckelt
    }

    private static void Build()
    {
        lock (Gate)
        {
            if (tradable != null) return;
            var list = new List<(uint, string)>();
            var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var sheet = data?.GetExcelSheet<Item>();
                if (sheet != null)
                {
                    foreach (var row in sheet)
                    {
                        if (row.ItemSearchCategory.RowId == 0 || row.IsUntradable) continue;
                        var n = row.Name.ToString();
                        if (string.IsNullOrEmpty(n)) continue;
                        list.Add((row.RowId, n));
                        map.TryAdd(n, row.RowId);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Item-Liste konnte nicht aufgebaut werden.");
            }
            tradable = list;
            byName = map;
        }
    }

    public static string NameOf(uint id)
    {
        try
        {
            var sheet = data?.GetExcelSheet<Item>();
            if (sheet != null && sheet.TryGetRow(id, out var row)) return row.Name.ToString();
        }
        catch { /* nur Anzeige */ }
        return $"#{id}";
    }

    public static bool IsTradable(uint id)
    {
        try
        {
            var sheet = data?.GetExcelSheet<Item>();
            return sheet != null && sheet.TryGetRow(id, out var row) && row.ItemSearchCategory.RowId != 0 && !row.IsUntradable;
        }
        catch { return false; }
    }

    public static bool CanHq(uint id)
    {
        try
        {
            var sheet = data?.GetExcelSheet<Item>();
            return sheet != null && sheet.TryGetRow(id, out var row) && row.CanBeHq;
        }
        catch { return false; }
    }

    /// <summary>Genauer Name → Item-ID (nur handelbare Items), sonst 0.</summary>
    public static uint IdByName(string name)
    {
        Build();
        return byName != null && byName.TryGetValue(name, out var id) ? id : 0;
    }

    /// <summary>Handelbare Items, deren Name den Text enthält (Treffer, die damit beginnen, zuerst).</summary>
    public static List<(uint Id, string Name)> Search(string text, int max)
    {
        Build();
        text = text.Trim();
        if (text.Length < 2 || tradable == null) return new List<(uint, string)>();
        return tradable
            .Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ThenBy(t => t.Name.Length)
            .Take(max)
            .ToList();
    }
}
