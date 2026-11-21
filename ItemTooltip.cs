using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GilBanking;

/// <summary>
/// Zeigt neben dem Mauszeiger ein kleines Fenster mit dem Marktwert (Universalis), solange ein Item
/// im Spiel mit der Maus anvisiert wird. Rein lesend: es wird nur gelesen, welches Item gerade unter
/// dem Mauszeiger liegt.
/// Preisquelle wählbar: eigene Welt, eigenes Rechenzentrum, ganz Europa oder eigene Eingabe.
/// Bei abweichender Quelle folgt ein Block für die Heimatwelt (NQ und HQ getrennt) mit Vergleich
/// in derselben Qualität wie das Item unter der Maus.
/// </summary>
public sealed class ItemTooltip
{
    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly IDataManager data;
    private readonly UniversalisClient market;
    private readonly Ledger ledger;
    private readonly Configuration config;

    private uint hoverId;
    private DateTime hoverStartUtc;

    private long ownAvg;
    private int ownCount;

    public ItemTooltip(IGameGui gameGui, IObjectTable objectTable, IDataManager data,
                       UniversalisClient market, Ledger ledger, Configuration config)
    {
        this.gameGui = gameGui;
        this.objectTable = objectTable;
        this.data = data;
        this.market = market;
        this.ledger = ledger;
        this.config = config;
    }

    public void Draw()
    {
        if (!config.ShowMarketTooltip) return;

        try
        {
            DrawInternal();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler im Marktpreis-Tooltip.");
        }
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
            if (showHome) home = market.Get(id, homeWorld!);
        }

        var s = ImGuiHelpers.GlobalScale;
        var pos = ImGui.GetMousePos() + new Vector2(config.TooltipOffsetX, config.TooltipOffsetY) * s;
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, new Vector2(1f, 0f));
        ImGui.SetNextWindowBgAlpha(0.94f);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f * s);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * s);
        if (ImGui.Begin("##GilBankingTip", flags))
        {
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
                ImGui.TextColored(Ui.Bad, L.T("tip.unreachable"));
                ImGui.TextColored(Ui.Dim, info.Error ?? "");
            }
            else if (info.NoData)
            {
                ImGui.TextColored(Ui.Dim, L.F("tip.noData", scope));
                ImGui.TextColored(Ui.Dim, L.T("tip.noData2"));
            }
            else
            {
                ImGui.TextColored(Ui.Dim, L.F("tip.board", scope));

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
                    ImGui.TextColored(Ui.Dim, L.F("tip.updated", L.Age(info.LastUpload.Value)));

                // Heimatwelt: lohnt es sich, dort einzustellen?
                if (showHome)
                    DrawHomeBlock(info, home, homeWorld!, scope, canHq, hq);
            }

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

    private void CalcOwn(uint id)
    {
        var sales = ledger.Data.Entries.Where(e => e.Kind == EntryKind.Sale && e.ItemId == id).ToList();
        ownCount = sales.Count;
        var qty = sales.Sum(e => (long)Math.Max(1, e.Quantity));
        ownAvg = qty > 0 ? sales.Sum(e => e.TotalGil) / qty : 0;
    }
}
