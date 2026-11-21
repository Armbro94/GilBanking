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

        string? scope = null;
        var player = objectTable.LocalPlayer;
        if (player != null)
        {
            var world = player.CurrentWorld.ValueNullable;
            if (world != null)
            {
                scope = config.TooltipUseDataCenter
                    ? world.Value.DataCenter.ValueNullable?.Name.ToString()
                    : world.Value.Name.ToString();
            }
        }

        MarketInfo? info = null;
        if (tradable && !string.IsNullOrEmpty(scope))
            info = market.Get(id, scope);

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
                PriceLine(L.T("tip.minNq"), info.MinNq);
                if (canHq) PriceLine(L.T("tip.minHq"), info.MinHq);
                if (config.TooltipUseDataCenter && info.CheapestPrice > 0)
                {
                    var w = string.IsNullOrEmpty(info.CheapestWorld) ? "" : $" ({info.CheapestWorld})";
                    ImGui.Text(L.T("tip.cheapest"));
                    ImGui.SameLine();
                    ImGui.TextColored(Ui.Good, $"{Ui.Gil((long)info.CheapestPrice)} Gil{w}");
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
                    ImGui.TextColored(Ui.Dim, L.F("tip.updated", Age(info.LastUpload.Value)));
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

    private static void PriceLine(string label, double value)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        if (value > 0) ImGui.TextColored(Ui.Gold, $"{Ui.Gil((long)value)} Gil");
        else ImGui.TextColored(Ui.Dim, "–");
    }

    private static string Age(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 1) return L.T("age.now");
        if (d.TotalHours < 1) return L.F("age.min", (int)d.TotalMinutes);
        if (d.TotalDays < 1) return L.F("age.hour", (int)d.TotalHours);
        return L.F("age.day", (int)d.TotalDays);
    }

    private void CalcOwn(uint id)
    {
        var sales = ledger.Data.Entries.Where(e => e.Kind == EntryKind.Sale && e.ItemId == id).ToList();
        ownCount = sales.Count;
        var qty = sales.Sum(e => (long)Math.Max(1, e.Quantity));
        ownAvg = qty > 0 ? sales.Sum(e => e.TotalGil) / qty : 0;
    }
}
