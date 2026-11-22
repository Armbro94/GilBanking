using System;
using System.Collections.Generic;
using System.Linq;

namespace GilBanking;

/// <summary>
/// Merkt sich kurz die Systemmeldungen mit Gil-Beträgen (z. B. "Für die Teilnahme … erhältst du 4.320 Gil."),
/// damit sonstige Gil-Einnahmen mit der Meldung beschriftet werden können, die dazu gehört.
/// </summary>
internal static class GilMessages
{
    private static readonly object Gate = new();
    private static readonly List<(DateTime At, string Text)> Items = new();

    public static void Add(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return;

        lock (Gate)
        {
            var now = DateTime.UtcNow;
            Items.RemoveAll(i => (now - i.At).TotalSeconds > 60);
            if (Items.Any(i => i.Text == t && (now - i.At).TotalSeconds < 1)) return;
            Items.Add((now, t));
        }
    }

    /// <summary>Meldungen, die zeitlich zu einer Gil-Änderung passen (± seconds).</summary>
    public static List<string> Near(DateTime utc, double seconds)
    {
        lock (Gate)
        {
            return Items
                .Where(i => Math.Abs((i.At - utc).TotalSeconds) <= seconds)
                .OrderBy(i => i.At)
                .Select(i => i.Text)
                .Distinct()
                .ToList();
        }
    }
}
