using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace GilBanking;

/// <summary>Gemeinsame Farben, Formatierung und selbstgezeichnete Karten/Diagramme.</summary>
internal static class Ui
{
    public static readonly Vector4 Accent = new(0.30f, 0.78f, 0.69f, 1f);
    public static readonly Vector4 Gold = new(1.00f, 0.82f, 0.35f, 1f);
    public static readonly Vector4 Good = new(0.45f, 0.87f, 0.50f, 1f);
    public static readonly Vector4 Bad = new(0.96f, 0.42f, 0.42f, 1f);
    public static readonly Vector4 Dim = new(0.62f, 0.65f, 0.70f, 1f);
    public static readonly Vector4 Text = new(0.92f, 0.93f, 0.95f, 1f);
    public static readonly Vector4 CardBg = new(0.12f, 0.13f, 0.16f, 1f);
    public static readonly Vector4 Grid = new(1f, 1f, 1f, 0.07f);

    private static uint U(Vector4 c) => ImGui.GetColorU32(c);

    // ---------- Formatierung ----------

    public static string Gil(long v) => v.ToString("N0", CultureInfo.CurrentCulture);

    public static string Signed(long v) => (v > 0 ? "+" : "") + Gil(v);

    public static Vector4 SignColor(long v) => v > 0 ? Good : v < 0 ? Bad : Dim;

    public static string Abbrev(double v)
    {
        var a = Math.Abs(v);
        if (a >= 1_000_000_000) return (v / 1_000_000_000d).ToString("0.##", CultureInfo.CurrentCulture) + L.T("abbr.b");
        if (a >= 1_000_000) return (v / 1_000_000d).ToString("0.##", CultureInfo.CurrentCulture) + L.T("abbr.m");
        if (a >= 1_000) return (v / 1_000d).ToString("0.#", CultureInfo.CurrentCulture) + L.T("abbr.k");
        return v.ToString("0", CultureInfo.CurrentCulture);
    }

    // ---------- Karten ----------

    public static void Card(string title, string value, Vector4 valueColor, float width, Vector4? accent = null, string? sub = null)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, 76 * s);

        dl.AddRectFilled(p, p + size, U(CardBg), 8 * s);
        dl.AddRectFilled(p, new Vector2(p.X + 4 * s, p.Y + size.Y), U(accent ?? Accent), 8 * s, ImDrawFlags.RoundCornersLeft);
        dl.AddText(p + new Vector2(14 * s, 8 * s), U(Dim), title);
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.45f, p + new Vector2(14 * s, 26 * s), U(valueColor), value);
        if (sub != null)
            dl.AddText(p + new Vector2(14 * s, 54 * s), U(Dim), sub);

        ImGui.Dummy(size);
    }

    // ---------- Liniendiagramm ----------

    public static void LineChart(string id, IReadOnlyList<(DateTime T, double V)> pts, float height, Vector4 color)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(100, ImGui.GetContentRegionAvail().X), height * s);

        dl.AddRectFilled(p0, p0 + size, U(CardBg), 8 * s);
        ImGui.InvisibleButton(id, size);

        if (pts.Count < 2)
        {
            dl.AddText(p0 + new Vector2(14 * s, size.Y / 2 - 8 * s), U(Dim), L.T("chart.notEnough"));
            return;
        }

        var plot0 = p0 + new Vector2(66 * s, 14 * s);
        var plot1 = p0 + size - new Vector2(16 * s, 26 * s);

        var min = pts.Min(x => x.V);
        var max = pts.Max(x => x.V);
        if (max - min < 1) max = min + 1;
        var pad = (max - min) * 0.08;
        min -= pad;
        max += pad;

        var t0 = pts[0].T;
        var t1 = pts[pts.Count - 1].T;
        var span = Math.Max(1, (t1 - t0).TotalSeconds);

        Vector2 Map(DateTime t, double v) => new(
            (float)(plot0.X + (t - t0).TotalSeconds / span * (plot1.X - plot0.X)),
            (float)(plot1.Y - (v - min) / (max - min) * (plot1.Y - plot0.Y)));

        for (var i = 0; i <= 4; i++)
        {
            var y = plot0.Y + (plot1.Y - plot0.Y) * i / 4f;
            var v = max - (max - min) * i / 4d;
            dl.AddLine(new Vector2(plot0.X, y), new Vector2(plot1.X, y), U(Grid), 1f);
            dl.AddText(new Vector2(p0.X + 8 * s, y - 7 * s), U(Dim), Abbrev(v));
        }

        var leftLabel = t0.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var rightLabel = t1.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        dl.AddText(new Vector2(plot0.X, plot1.Y + 6 * s), U(Dim), leftLabel);
        dl.AddText(new Vector2(plot1.X - ImGui.CalcTextSize(rightLabel).X, plot1.Y + 6 * s), U(Dim), rightLabel);

        var step = Math.Max(1, pts.Count / 500);
        var mapped = new List<(Vector2 P, DateTime T, double V)>();
        for (var i = 0; i < pts.Count; i += step) mapped.Add((Map(pts[i].T, pts[i].V), pts[i].T, pts[i].V));
        if ((pts.Count - 1) % step != 0)
            mapped.Add((Map(pts[pts.Count - 1].T, pts[pts.Count - 1].V), pts[pts.Count - 1].T, pts[pts.Count - 1].V));

        var fill = new Vector4(color.X, color.Y, color.Z, 0.14f);
        for (var i = 1; i < mapped.Count; i++)
        {
            var a = mapped[i - 1].P;
            var b = mapped[i].P;
            dl.AddQuadFilled(a, b, new Vector2(b.X, plot1.Y), new Vector2(a.X, plot1.Y), U(fill));
        }
        for (var i = 1; i < mapped.Count; i++)
            dl.AddLine(mapped[i - 1].P, mapped[i].P, U(color), 2f * s);

        if (ImGui.IsItemHovered())
        {
            var mx = ImGui.GetMousePos().X;
            var best = mapped[0];
            foreach (var m in mapped)
                if (Math.Abs(m.P.X - mx) < Math.Abs(best.P.X - mx)) best = m;

            dl.AddLine(new Vector2(best.P.X, plot0.Y), new Vector2(best.P.X, plot1.Y), U(new Vector4(1, 1, 1, 0.25f)), 1f);
            dl.AddCircleFilled(best.P, 4 * s, U(color));
            ImGui.SetTooltip($"{best.T.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}\n{Gil((long)best.V)} Gil");
        }
    }

    // ---------- Balkendiagramm (Gewinn pro Tag) ----------

    public static void BarChart(string id, IReadOnlyList<(DateTime Day, long Sales, long Purchases)> days, float height)
    {
        var s = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(100, ImGui.GetContentRegionAvail().X), height * s);

        dl.AddRectFilled(p0, p0 + size, U(CardBg), 8 * s);
        ImGui.InvisibleButton(id, size);

        if (days.Count == 0 || days.All(d => d.Sales == 0 && d.Purchases == 0))
        {
            dl.AddText(p0 + new Vector2(14 * s, size.Y / 2 - 8 * s), U(Dim), L.T("chart.noSales"));
            return;
        }

        var plot0 = p0 + new Vector2(66 * s, 14 * s);
        var plot1 = p0 + size - new Vector2(16 * s, 26 * s);

        var profits = days.Select(d => (double)(d.Sales - d.Purchases)).ToList();
        var max = Math.Max(1, profits.Max());
        var min = Math.Min(0, profits.Min());
        var range = max - min;
        var zeroY = (float)(plot1.Y - (0 - min) / range * (plot1.Y - plot0.Y));

        dl.AddLine(new Vector2(plot0.X, zeroY), new Vector2(plot1.X, zeroY), U(Grid), 1f);
        dl.AddText(new Vector2(p0.X + 8 * s, plot0.Y - 7 * s), U(Dim), Abbrev(max));
        dl.AddText(new Vector2(p0.X + 8 * s, zeroY - 7 * s), U(Dim), "0");
        if (min < 0) dl.AddText(new Vector2(p0.X + 8 * s, plot1.Y - 7 * s), U(Dim), Abbrev(min));

        var slot = (plot1.X - plot0.X) / days.Count;
        var barW = slot * 0.64f;
        var mouse = ImGui.GetMousePos();
        var hovered = -1;

        for (var i = 0; i < days.Count; i++)
        {
            var x = plot0.X + slot * i + (slot - barW) / 2;
            var v = profits[i];
            var y = (float)(plot1.Y - (v - min) / range * (plot1.Y - plot0.Y));
            var top = Math.Min(y, zeroY);
            var bottom = Math.Max(y, zeroY);
            if (bottom - top < 1) bottom = top + 1;

            var col = v >= 0 ? Good : Bad;
            if (ImGui.IsItemHovered() && mouse.X >= x - 2 && mouse.X <= x + barW + 2)
            {
                hovered = i;
                col = new Vector4(col.X, col.Y, col.Z, 1f);
            }
            else col = new Vector4(col.X, col.Y, col.Z, 0.78f);

            dl.AddRectFilled(new Vector2(x, top), new Vector2(x + barW, bottom), U(col), 3 * s);

            if (days.Count <= 16 || i % 2 == 0)
            {
                var label = days[i].Day.ToString("dd.MM", CultureInfo.CurrentCulture);
                dl.AddText(new Vector2(x + barW / 2 - ImGui.CalcTextSize(label).X / 2, plot1.Y + 6 * s), U(Dim), label);
            }
        }

        if (hovered >= 0)
        {
            var d = days[hovered];
            ImGui.SetTooltip(L.F("bar.tip",
                d.Day.ToString("dddd, dd.MM.", CultureInfo.CurrentCulture),
                Gil(d.Sales), Gil(d.Purchases), Signed(d.Sales - d.Purchases)));
        }
    }
}
