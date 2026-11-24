using System.Collections.Generic;
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace GilBanking.Windows;

/// <summary>
/// Zeigt neben dem Mauszeiger ein kleines Fenster mit dem Marktwert (Universalis), solange ein Item
/// im Spiel mit der Maus anvisiert wird. Rein lesend: es wird nur gelesen, welches Item gerade unter
/// dem Mauszeiger liegt.
/// Preisquelle wählbar: eigene Welt, eigenes Rechenzentrum, ganz Europa oder eigene Eingabe.
/// Bei abweichender Quelle folgt ein Block für die Heimatwelt (NQ und HQ getrennt) mit Vergleich
/// in derselben Qualität wie das Item unter der Maus.
/// </summary>
public sealed unsafe class ItemTooltip
{
    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly IDataManager data;
    private readonly UniversalisClient market;
    private readonly Ledger ledger;
    private readonly Configuration config;
    private readonly LocalMarket local;

    private uint hoverId;
    private DateTime hoverStartUtc;

    private Vector2 lastSize;

    /// <summary>Für die Fehlersuche: was beim Auslesen des Spiel-Tooltips herauskam.</summary>
    public static string GameTooltipDebug { get; private set; } = "-";

    private long ownAvg;
    private int ownCount;

    public ItemTooltip(IGameGui gameGui, IObjectTable objectTable, IDataManager data,
                       UniversalisClient market, Ledger ledger, Configuration config, LocalMarket local)
    {
        this.gameGui = gameGui;
        this.objectTable = objectTable;
        this.data = data;
        this.market = market;
        this.ledger = ledger;
        this.config = config;
        this.local = local;
    }

    public void Draw()
    {
        if (!config.ShowMarketTooltip) return;
        if (HideKeyHeld()) return;

        try
        {
            DrawInternal();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler im Marktpreis-Tooltip.");
        }
    }

    /// <summary>Ist die in den Einstellungen gewählte "Ausblenden"-Taste gerade gedrückt?</summary>
    private bool HideKeyHeld()
    {
        if (config.TooltipHideKey == 0) return false;
        var io = ImGui.GetIO();
        return config.TooltipHideKey switch
        {
            1 => io.KeyAlt,
            2 => io.KeyCtrl,
            3 => io.KeyShift,
            4 => io.KeyCtrl && io.KeyShift,
            _ => false,
        };
    }

    /// <summary>Position und Größe (in Pixeln) des Item-Tooltips des Spiels, falls gerade sichtbar.</summary>
    /// <summary>Sucht in einem Zeigerobjekt (je nach Dalamud-Version unterschiedlich aufgebaut) die Speicheradresse.</summary>
    private static nint FindAddress(object r, out string members)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var t = r.GetType();
        var names = new List<string>();

        foreach (var f in t.GetFields(flags))
        {
            names.Add($"{f.Name}:{f.FieldType.Name}");
            var v = f.GetValue(r);
            var a = AsAddress(v);
            if (a != 0) { members = ""; return a; }
        }
        foreach (var p in t.GetProperties(flags))
        {
            names.Add($"{p.Name}:{p.PropertyType.Name}");
            if (p.GetIndexParameters().Length > 0) continue;
            object? v;
            try { v = p.GetValue(r); } catch { continue; }
            var a = AsAddress(v);
            if (a != 0 && (p.Name.Contains("Address") || p.Name.Contains("Pointer") || p.Name.Contains("Ptr")))
            { members = ""; return a; }
        }

        members = string.Join(", ", names);
        return 0;
    }

    /// <summary>Speicheradresse eines Addons aus dem Rückgabewert von GetAddonByName (0 = nicht vorhanden).</summary>
    public static nint AddonAddress(object? r)
    {
        if (r == null) return 0;
        if (r is nint p) return p;
        if (r.GetType().GetProperty("IsNull")?.GetValue(r) is true) return 0;
        return FindAddress(r, out _);
    }

    private static nint AsAddress(object? v)
    {
        switch (v)
        {
            case nint n: return n;
            case ulong u: return (nint)u;
            case long l: return (nint)l;
            case System.Reflection.Pointer ptr: return (nint)System.Reflection.Pointer.Unbox(ptr);
            default: return 0;
        }
    }

    private bool TryGetGameTooltip(out Vector2 pos, out Vector2 size)
    {
        // Je nach Menü nutzt das Spiel unterschiedliche Tooltip-Fenster (Items, Sammlungen, Aktionen).
        foreach (var name in new[] { "ItemDetail", "ActionDetail", "Tooltip" })
            if (TryGetAddonRect(name, out pos, out size)) return true;
        pos = default;
        size = default;
        return false;
    }

    private bool TryGetAddonRect(string addonName, out Vector2 pos, out Vector2 size)
    {
        pos = default;
        size = default;
        try
        {
            object? r = gameGui.GetAddonByName(addonName);
            if (r == null) { GameTooltipDebug = "ItemDetail: null"; return false; }

            nint addr = 0;
            if (r is nint p) addr = p;
            else
            {
                var t = r.GetType();
                if (t.GetProperty("IsNull")?.GetValue(r) is true) { GameTooltipDebug = $"ItemDetail: IsNull ({t.Name})"; return false; }
                addr = FindAddress(r, out var members);
                if (addr == 0)
                {
                    GameTooltipDebug = $"ItemDetail: keine Adresse ({t.Name}: {members})";
                    return false;
                }
            }
            if (addr == 0) { GameTooltipDebug = "ItemDetail: Adresse 0"; return false; }

            var unit = (AtkUnitBase*)addr;
            var visible = unit->IsVisible;
            var root = unit->RootNode;
            var scale = unit->Scale <= 0 ? 1f : unit->Scale;
            var rw = root == null ? 0 : root->Width * scale;
            var rh = root == null ? 0 : root->Height * scale;
            GameTooltipDebug = $"ItemDetail: sichtbar={visible}, X={unit->X}, Y={unit->Y}, Größe={rw:0}x{rh:0}, Skalierung={scale:0.##}";

            if (!visible || root == null) return false;

            var display = ImGui.GetIO().DisplaySize;
            if (rw < 2 || rh < 2 || rw > display.X * 0.9f || rh > display.Y * 0.95f) return false;

            pos = new Vector2(unit->X, unit->Y);
            size = new Vector2(rw, rh);
            return true;
        }
        catch (Exception ex)
        {
            GameTooltipDebug = "Fehler: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Obere linke Ecke des Tooltips: links vom Mauszeiger. Überdeckt das Tooltip des Spiels diese Stelle,
    /// wird links daneben, rechts daneben oder darunter ausgewichen.
    /// </summary>
    private Vector2 PlaceWindow(Vector2 mouse, float scale)
    {
        var display = ImGui.GetIO().DisplaySize;
        var size = lastSize.X > 1 ? lastSize : new Vector2(320, 200) * scale;
        var gap = 8f * scale;

        var pos = new Vector2(mouse.X + config.TooltipOffsetX * scale - size.X, mouse.Y + config.TooltipOffsetY * scale);

        // Kein Platz links vom Mauszeiger (linker Bildschirmrand): sonst würde das Fenster auf dem Zeiger und dem
        // Tooltip des Spiels landen, das meist rechts unten vom Zeiger erscheint. Dann lieber darüber.
        var noRoomLeft = mouse.X + config.TooltipOffsetX * scale - size.X < 0;
        if (noRoomLeft && config.TooltipAvoidGame)
        {
            var above = new Vector2(mouse.X - size.X * 0.5f, mouse.Y - size.Y - 24f * scale);
            if (above.Y >= 0) pos = above;
        }

        if (config.TooltipAvoidGame && TryGetGameTooltip(out var gp, out var gs))
        {
            bool Overlaps(Vector2 p) =>
                p.X < gp.X + gs.X + gap && p.X + size.X + gap > gp.X &&
                p.Y < gp.Y + gs.Y + gap && p.Y + size.Y + gap > gp.Y;

            if (Overlaps(pos))
            {
                var left = new Vector2(gp.X - gap - size.X, gp.Y);
                var right = new Vector2(gp.X + gs.X + gap, gp.Y);
                var below = new Vector2(gp.X, gp.Y + gs.Y + gap);
                var above = new Vector2(gp.X, gp.Y - gap - size.Y);

                if (left.X >= 0) pos = left;
                else if (right.X + size.X <= display.X) pos = right;
                else if (below.Y + size.Y <= display.Y) pos = below;
                else if (above.Y >= 0) pos = above;
            }
        }

        pos.X = Math.Max(0, Math.Min(pos.X, display.X - size.X));
        pos.Y = Math.Max(0, Math.Min(pos.Y, display.Y - size.Y));
        return pos;
    }

    /// <summary>
    /// Abfrage-Bereich laut Einstellung. Die Heimatwelt ist fest (Einstellung oder Heimatwelt des Charakters)
    /// und ändert sich nicht, wenn man gerade auf einer anderen Welt zu Besuch ist.
    /// </summary>
    private string? ResolveScope(string? homeWorld, string? homeDc)
    {
        return config.TooltipScopeMode switch
        {
            1 => homeDc ?? homeWorld,
            2 => "Europe",
            3 => string.IsNullOrWhiteSpace(config.TooltipCustomScope) ? homeWorld : config.TooltipCustomScope.Trim(),
            _ => homeWorld,
        };
    }

    /// <summary>Günstigstes Angebot einer Qualität (mit Welt, falls bekannt).</summary>
    private static (double Price, string World) Cheapest(MarketInfo m, bool hq)
    {
        if (hq) return m.CheapestHqPrice > 0 ? (m.CheapestHqPrice, m.CheapestHqWorld) : (m.MinHq, "");
        return m.CheapestNqPrice > 0 ? (m.CheapestNqPrice, m.CheapestNqWorld) : (m.MinNq, "");
    }

    private void DrawInternal()
    {
        var raw = gameGui.HoveredItem;
        if (raw == 0 || raw >= 2_000_000)
        {
            hoverId = 0;
            return;
        }

        var hq = false;
        if (raw >= 1_000_000) { raw -= 1_000_000; hq = true; }
        else if (raw >= 500_000) raw -= 500_000;
        var id = (uint)raw;

        if (id != hoverId)
        {
            hoverId = id;
            hoverStartUtc = DateTime.UtcNow;
            CalcOwn(id);
        }
        if ((DateTime.UtcNow - hoverStartUtc).TotalMilliseconds < config.TooltipDelayMs) return;

        var sheet = data.GetExcelSheet<Item>();
        if (sheet == null || !sheet.TryGetRow(id, out var row)) return;

        var name = row.Name.ToString();
        var tradable = row.ItemSearchCategory.RowId != 0 && !row.IsUntradable;
        var canHq = row.CanBeHq;

        if (!tradable && config.TooltipHideUntradable) return;

        // Heimatwelt (hier verkaufen die Retainer): feste Einstellung, sonst die Heimatwelt des Charakters.
        string? homeWorld = string.IsNullOrWhiteSpace(config.HomeWorld) ? null : config.HomeWorld.Trim();
        string? homeDc = null;
        var player = objectTable.LocalPlayer;
        if (player != null)
        {
            var hw = player.HomeWorld.ValueNullable;
            if (homeWorld == null && hw != null) homeWorld = hw.Value.Name.ToString();
            if (homeWorld != null && hw != null && string.Equals(homeWorld, hw.Value.Name.ToString(), StringComparison.OrdinalIgnoreCase))
                homeDc = hw.Value.DataCenter.ValueNullable?.Name.ToString();
        }
        // Die bekannte Liste hat Vorrang: die Spieldaten ordnen z. B. Light-Welten falsch zu.
        homeDc = Worlds.DataCenterOf(homeWorld) ?? homeDc;

        var scope = ResolveScope(homeWorld, homeDc);
        var showHome = config.TooltipCompareHome
                       && !string.IsNullOrEmpty(homeWorld)
                       && !string.IsNullOrEmpty(scope)
                       && !string.Equals(scope, homeWorld, StringComparison.OrdinalIgnoreCase);

        MarketInfo? info = null;
        MarketInfo? home = null;
        if (tradable && !string.IsNullOrEmpty(scope))
        {
            info = market.Get(id, scope);
            if (showHome) home = market.Get(id, homeWorld!, lite: true); // leichter Abruf, nur Preise
        }

        var s = ImGuiHelpers.GlobalScale;
        var pos = PlaceWindow(ImGui.GetMousePos(), s);
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, Vector2.Zero);
        ImGui.SetNextWindowBgAlpha(0.94f);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        var scale = Math.Clamp(config.TooltipScale, 0.6f, 2.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f * s * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * s * scale);
        if (ImGui.Begin("##GilBankingTip", flags))
        {
            ImGui.SetWindowFontScale(scale);
            lastSize = ImGui.GetWindowSize();
            ImGui.TextColored(Ui.Accent, "Gil-Banking");
            ImGui.SameLine();
            ImGui.TextColored(Ui.Dim, "· " + name + (hq ? " ★" : ""));
            ImGui.Separator();

            if (!tradable)
            {
                ImGui.TextColored(Ui.Bad, L.T("tip.notTradable"));
                ImGui.TextColored(Ui.Dim, L.T("tip.notTradableSub"));
                if (row.PriceLow > 0)
                    ImGui.TextColored(Ui.Dim, L.F("tip.npc", Ui.Gil(row.PriceLow)));
            }
            else if (string.IsNullOrEmpty(scope))
            {
                ImGui.TextColored(Ui.Dim, L.T("tip.notLoggedIn"));
            }
            else if (info == null)
            {
                ImGui.TextColored(Ui.Dim, L.F("tip.loading", scope));
            }
            else if (!info.Ok)
            {
                // HTTP-Fehler (Universalis antwortet, aber mit Fehler) getrennt von "nicht erreichbar" (Netzwerk).
                if (info.Error != null && info.Error.StartsWith("HTTP ", StringComparison.Ordinal))
                    ImGui.TextColored(Ui.Bad, L.F("tip.httpError", info.Error));
                else
                {
                    ImGui.TextColored(Ui.Bad, L.T("tip.unreachable"));
                    ImGui.TextColored(Ui.Dim, info.Error ?? "");
                }
            }
            else if (info.NoData)
            {
                ImGui.TextColored(Ui.Dim, L.F("tip.noData", scope));
                ImGui.TextColored(Ui.Dim, L.T("tip.noData2"));
            }
            else
            {
                ImGui.TextColored(Ui.Dim, L.F("tip.board", scope));
                if (info.Stale)
                    ImGui.TextColored(Ui.Bad, L.F("tip.stale", L.Age(info.DataUtc)));

                var (nqPrice, nqWorld) = Cheapest(info, false);
                PriceLine(L.T("tip.minNq"), nqPrice, nqWorld, scope);
                if (canHq)
                {
                    var (hqPrice, hqWorld) = Cheapest(info, true);
                    PriceLine(L.T("tip.minHq"), hqPrice, hqWorld, scope);
                }

                PriceLine(L.T("tip.avgCur"), info.AvgCurrent);
                PriceLine(L.T("tip.avgSold"), info.AvgSold);

                if (info.SaleVelocity > 0)
                {
                    ImGui.Text(L.T("tip.velocity"));
                    ImGui.SameLine();
                    ImGui.TextColored(Ui.Text, info.SaleVelocity.ToString("0.#", L.Culture));
                }
                if (info.LastUpload != null)
                {
                    // Sehr alte Uploads deutlich kennzeichnen: der Preis kann längst überholt sein.
                    if ((DateTime.UtcNow - info.LastUpload.Value).TotalHours > 24)
                        ImGui.TextColored(Ui.Gold, L.F("tip.updatedOld", L.Age(info.LastUpload.Value)));
                    else
                        ImGui.TextColored(Ui.Dim, L.F("tip.updated", L.Age(info.LastUpload.Value)));
                }

                // Heimatwelt: lohnt es sich, dort einzustellen?
                if (showHome)
                    DrawHomeBlock(info, home, homeWorld!, scope, canHq, hq);
            }

            if (tradable && config.LocalMarketEnabled && !string.IsNullOrEmpty(scope))
                DrawLocalObs(id, scope, canHq, hq, info);

            if (ownCount > 0)
            {
                ImGui.Separator();
                ImGui.Text(L.T("tip.ownAvg"));
                ImGui.SameLine();
                ImGui.TextColored(Ui.Gold, $"{Ui.Gil(ownAvg)} Gil");
                ImGui.SameLine();
                ImGui.TextColored(Ui.Dim, $"({ownCount}×)");
            }

            ImGui.Spacing();
            ImGui.TextColored(Ui.Dim, L.T("tip.source"));
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    /// <summary>
    /// Preise auf der Heimatwelt (NQ und HQ getrennt) und Vergleich mit dem günstigsten Angebot im Abfragebereich.
    /// Verglichen wird immer die Qualität des Items unter der Maus (HQ mit HQ, NQ mit NQ).
    /// </summary>
    private static void DrawHomeBlock(MarketInfo info, MarketInfo? home, string homeWorld, string scope, bool canHq, bool hq)
    {
        ImGui.Separator();
        ImGui.TextColored(Ui.Accent, L.F("tip.homeWorld", homeWorld));

        if (home == null)
        {
            ImGui.TextColored(Ui.Dim, L.F("tip.loading", homeWorld));
            return;
        }
        if (!home.Ok)
        {
            ImGui.TextColored(Ui.Bad, L.T("tip.unreachable"));
            return;
        }

        var cmpHq = canHq && hq;
        var qLabel = canHq ? (hq ? "HQ" : "NQ") : "";

        PriceLine(L.T("tip.minNq"), Cheapest(home, false).Price);
        if (canHq) PriceLine(L.T("tip.minHq"), Cheapest(home, true).Price);

        if (!home.NoData)
        {
            PriceLine(L.T("tip.avgSold"), home.AvgSold);
            if (home.SaleVelocity > 0)
            {
                ImGui.Text(L.T("tip.velocity"));
                ImGui.SameLine();
                ImGui.TextColored(Ui.Text, home.SaleVelocity.ToString("0.#", L.Culture));
            }
        }

        if (canHq) ImGui.TextColored(Ui.Dim, L.F("tip.compareFor", qLabel));

        var homePrice = home.NoData ? 0 : Cheapest(home, cmpHq).Price;
        var (bestPrice, bestWorld) = Cheapest(info, cmpHq);

        if (homePrice <= 0)
        {
            // Niemand bietet es (in dieser Qualität) auf der Heimatwelt an.
            ImGui.TextColored(Ui.Good, canHq ? L.F("tip.noneOnHome", qLabel) : L.T("tip.noneOnHomeAny"));
        }
        else if (bestPrice > 0 && bestPrice < homePrice)
        {
            var diff = homePrice - bestPrice;
            var pct = diff / homePrice * 100d;
            var where = string.IsNullOrEmpty(bestWorld) ? scope : bestWorld;
            ImGui.TextColored(Ui.Good, L.F("tip.cheaperElsewhere", Ui.Gil((long)diff), pct.ToString("0", L.Culture), where));
            ImGui.TextColored(Ui.Gold, L.F("tip.margin", Ui.Gil((long)diff)));
        }
        else if (bestPrice > 0)
        {
            ImGui.TextColored(Ui.Dim, L.T("tip.homeCheapest"));
        }
    }

    private static void PriceLine(string label, double value, string world = "", string? scope = null)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        if (value > 0)
        {
            var suffix = !string.IsNullOrEmpty(world) && !string.Equals(world, scope, StringComparison.OrdinalIgnoreCase)
                ? $" ({world})"
                : "";
            ImGui.TextColored(Ui.Gold, $"{Ui.Gil((long)value)} Gil{suffix}");
        }
        else
        {
            ImGui.TextColored(Ui.Dim, "–");
        }
    }

    /// <summary>Selbst am Marktbrett gesehener Preis (Alpha); zeigt, ob er neuer ist als die Universalis-Daten.</summary>
    private void DrawLocalObs(uint id, string scope, bool canHq, bool hq, MarketInfo? info)
    {
        var o = local.Best(id, scope);
        if (o == null) return;

        var price = canHq ? (hq ? o.MinHq : o.MinNq) : o.Min;
        var quality = canHq ? (hq ? " HQ" : " NQ") : "";
        if (price <= 0) { price = o.Min; quality = ""; }

        ImGui.Separator();
        var newer = info == null || info.LastUpload == null || o.SeenUtc > info.LastUpload.Value;
        ImGui.TextColored(newer ? Ui.Good : Ui.Dim, L.T("tip.own") + quality);
        ImGui.SameLine();
        ImGui.TextColored(Ui.Gold, $"{Ui.Gil(price)} Gil ({o.World})");
        ImGui.TextColored(Ui.Dim, L.F(newer ? "tip.ownNewer" : "tip.ownOlder", L.Age(o.SeenUtc)));
    }

    private void CalcOwn(uint id)
    {
        var sales = ledger.EntriesSnapshot().Where(e => e.Kind == EntryKind.Sale && e.ItemId == id).ToList();
        ownCount = sales.Count;
        var qty = sales.Sum(e => (long)Math.Max(1, e.Quantity));
        ownAvg = qty > 0 ? sales.Sum(e => e.TotalGil) / qty : 0;
    }
}
