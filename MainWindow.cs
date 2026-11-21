using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace GilBanking;

public sealed class MainWindow : Window
{
    private readonly Ledger ledger;
    private readonly GilTracker tracker;
    private readonly ChatTracker chat;
    private readonly Configuration config;

    private string? charFilter; // null = alle Charaktere
    private int rangeIdx = 1;

    private string search = "";
    private int kindFilter;
    private int itemSort;

    private int manKind;
    private string manName = "";
    private int manQty = 1;
    private int manTotal;
    private string manNote = "";

    private string kwSale;
    private string kwBuy;
    private string exportInfo = "";
    private bool selectSettings;

    private string pathData;
    private string pathExport;
    private string pathInfo = "";
    private bool pathOk = true;

    private string scopeCustom;

    // Zwischengespeicherte Auswertungen: nur bei Änderungen (Filter/Daten) oder alle paar Sekunden neu berechnet.
    private readonly Cache<OverviewData> overviewCache = new();
    private readonly Cache<List<LedgerEntry>> salesCache = new();
    private readonly Cache<List<ItemStat>> itemsCache = new();
    private readonly Cache<List<string>> charsCache = new();
    private readonly Cache<RetainerView> retainerCache = new();
    private readonly Cache<ExpenseView> expenseCache = new();

    public MainWindow(Ledger ledger, GilTracker tracker, ChatTracker chat, Configuration config)
        : base("Gil-Banking###GilBankingMain")
    {
        this.ledger = ledger;
        this.tracker = tracker;
        this.chat = chat;
        this.config = config;
        kwSale = config.SaleKeywords;
        kwBuy = config.PurchaseKeywords;
        pathData = config.DataDirectory;
        pathExport = config.ExportDirectory;
        scopeCustom = config.TooltipCustomScope;

        Size = new Vector2(980, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void ShowSettings()
    {
        selectSettings = true;
        IsOpen = true;
    }

    // ---------- Hilfen ----------

    private sealed class Cache<T> where T : class
    {
        private T? value;
        private string key = "";
        private long at;

        public T Get(string k, Func<T> make, long maxAgeMs = 3000)
        {
            var now = Environment.TickCount64;
            if (value == null || k != key || now - at > maxAgeMs)
            {
                value = make();
                key = k;
                at = now;
            }
            return value;
        }
    }

    private sealed record OverviewData(
        long Gil,
        long Retainer,
        Totals Totals,
        List<(DateTime Day, long Sales, long Purchases)> Daily,
        List<(DateTime T, double V)> Wealth);

    private sealed record RetainerEntry(string Character, string Name, long Gil, long Income, DateTime? Last);

    private sealed record ExpenseView(
        (long Repair, int RepairCount, long OtherOut, int OtherOutCount, long OtherIn, int OtherInCount) Sums,
        List<LedgerEntry> Rows);

    private sealed record RetainerView(List<RetainerEntry> Cards, long TotalGil, long TotalIncome);

    private static string[] Ranges() => new[]
    {
        L.T("range.today"), L.T("range.7"), L.T("range.30"), L.T("range.all"),
    };

    private static string[] KindFilters() => new[]
    {
        L.T("all"), L.T("filter.sales"), L.T("filter.purchases"),
    };

    private static string[] KindNames() => new[]
    {
        L.T("kind.sale"), L.T("kind.purchase"),
    };

    private static string[] ItemSorts() => new[]
    {
        L.T("sort.revenue"), L.T("sort.qty"), L.T("sort.avg"), L.T("sort.profit"),
    };

    private string? SelectedChar() => charFilter;

    private string BaseKey() => $"{ledger.Revision}|{charFilter ?? "*"}|{rangeIdx}";

    private DateTime FromUtc() => rangeIdx switch
    {
        0 => DateTime.Now.Date.ToUniversalTime(),
        1 => DateTime.UtcNow.AddDays(-7),
        2 => DateTime.UtcNow.AddDays(-30),
        _ => DateTime.MinValue,
    };

    private static bool Combo(string id, float width, IReadOnlyList<string> items, ref int idx)
    {
        var changed = false;
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo(id, items[Math.Clamp(idx, 0, items.Count - 1)]))
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (ImGui.Selectable(items[i], i == idx))
                {
                    idx = i;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    // ---------- Fenster ----------

    public override void Draw()
    {
        DrawHeader();
        ImGui.Spacing();

        if (ImGui.BeginTabBar("##gl_tabs"))
        {
            if (ImGui.BeginTabItem(L.T("tab.overview") + "###gl_tab_overview")) { DrawOverview(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(L.T("tab.sales") + "###gl_tab_sales")) { DrawSales(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(L.T("tab.items") + "###gl_tab_items")) { DrawItems(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(L.T("tab.expenses") + "###gl_tab_expenses")) { DrawExpenses(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(L.T("tab.retainers") + "###gl_tab_retainers")) { DrawRetainers(); ImGui.EndTabItem(); }

            var flags = selectSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            selectSettings = false;
            if (ImGui.BeginTabItem(L.T("tab.settings") + "###gl_tab_settings", flags)) { DrawSettings(); ImGui.EndTabItem(); }

            ImGui.EndTabBar();
        }
    }

    private void DrawHeader()
    {
        var s = ImGuiHelpers.GlobalScale;

        ImGui.TextColored(Ui.Accent, "Gil-Banking");
        ImGui.SameLine();
        ImGui.TextColored(Ui.Dim, tracker.CharacterKey != null ? $"· {tracker.CharacterKey}" : $"· {L.T("header.notLoggedIn")}");

        var items = new List<string> { L.T("all") };
        items.AddRange(charsCache.Get(ledger.Revision.ToString(), ledger.Characters, 5000));
        var idx = charFilter == null ? 0 : Math.Max(0, items.IndexOf(charFilter));

        ImGui.SameLine(0, 24 * s);
        ImGui.TextColored(Ui.Dim, L.T("header.character"));
        ImGui.SameLine();
        if (Combo("##gl_char", 230 * s, items, ref idx)) charFilter = idx == 0 ? null : items[idx];

        ImGui.SameLine(0, 16 * s);
        ImGui.TextColored(Ui.Dim, L.T("header.period"));
        ImGui.SameLine();
        Combo("##gl_range", 120 * s, Ranges(), ref rangeIdx);
    }

    // ---------- Übersicht ----------

    private void DrawOverview()
    {
        var s = ImGuiHelpers.GlobalScale;
        var ch = SelectedChar();
        var from = FromUtc();

        var d = overviewCache.Get(BaseKey(), () =>
        {
            var (g, r) = ledger.LatestWealth(ch);
            return new OverviewData(
                g, r,
                ledger.GetTotals(ch, from),
                ledger.GetDaily(ch, 14),
                ledger.GetWealthSeries(ch, from));
        });

        var totals = d.Totals;
        var session = tracker.HasSession ? tracker.CurrentGil - tracker.SessionStartGil : 0;

        var gap = 10 * s;
        var cardW = (ImGui.GetContentRegionAvail().X - gap * 3) / 4f;

        Ui.Card(L.T("card.gil"), Ui.Gil(d.Gil), Ui.Gold, cardW, Ui.Gold);
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("card.retainer"), Ui.Gil(d.Retainer), Ui.Text, cardW, Ui.Accent, L.T("card.retainerSub"));
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("card.wealth"), Ui.Gil(d.Gil + d.Retainer), Ui.Text, cardW, Ui.Accent);
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("card.session"), Ui.Signed(session), Ui.SignColor(session), cardW, Ui.SignColor(session), L.T("card.sessionSub"));

        ImGui.Spacing();

        var avg = totals.SaleCount > 0 ? totals.Sales / totals.SaleCount : 0;
        Ui.Card(L.T("card.revenue"), Ui.Gil(totals.Sales), Ui.Good, cardW, Ui.Good, L.F("card.salesCount", totals.SaleCount));
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("card.expenses"), Ui.Gil(totals.Purchases), Ui.Bad, cardW, Ui.Bad, L.F("card.purchCount", totals.PurchaseCount));
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("card.profit"), Ui.Signed(totals.Profit), Ui.SignColor(totals.Profit), cardW, Ui.SignColor(totals.Profit));
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("card.avg"), Ui.Gil(avg), Ui.Text, cardW, Ui.Accent);

        ImGui.Spacing();
        ImGui.TextColored(Ui.Dim, L.T("chart.daily"));
        Ui.BarChart("##gl_bars", d.Daily, 150);

        ImGui.Spacing();
        ImGui.TextColored(Ui.Dim, L.T("chart.wealth"));
        Ui.LineChart("##gl_wealth", d.Wealth, 190, Ui.Gold);
    }

    // ---------- Retainer ----------

    private void DrawRetainers()
    {
        var s = ImGuiHelpers.GlobalScale;
        var ch = SelectedChar();
        var from = FromUtc();

        var v = retainerCache.Get(BaseKey(), () =>
        {
            // Einnahmen je Retainer im gewählten Zeitraum (aus den erfassten Retainer-Verkäufen).
            var income = new Dictionary<string, (long Sum, DateTime Last)>();
            foreach (var e in ledger.Data.Entries)
            {
                if (e.Source != "Retainer" || e.TimeUtc < from) continue;
                if (ch != null && e.Character != ch) continue;

                var k = e.Character + "|" + e.ItemName;
                income.TryGetValue(k, out var cur);
                income[k] = (cur.Sum + e.TotalGil, e.TimeUtc > cur.Last ? e.TimeUtc : cur.Last);
            }

            var cards = new List<RetainerEntry>();
            long totalGil = 0, totalIncome = 0;
            foreach (var r in ledger.RetainerList(ch))
            {
                income.TryGetValue(r.Character + "|@retainer:" + r.Name, out var inc);
                cards.Add(new RetainerEntry(r.Character, r.Name, r.Gil, inc.Sum, inc.Sum > 0 ? inc.Last : null));
                totalGil += r.Gil;
                totalIncome += inc.Sum;
            }
            return new RetainerView(cards, totalGil, totalIncome);
        });

        var gap = 10 * s;
        var avail = ImGui.GetContentRegionAvail().X;
        var sumW = (avail - gap * 2) / 3f;
        var avg = v.Cards.Count > 0 ? v.TotalGil / v.Cards.Count : 0;

        Ui.Card(L.T("card.retainer"), Ui.Gil(v.TotalGil), Ui.Gold, sumW, Ui.Gold, L.F("ret.count", v.Cards.Count));
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("ret.sumIncome"), Ui.Signed(v.TotalIncome), Ui.SignColor(v.TotalIncome), sumW, Ui.Good, Ranges()[Math.Clamp(rangeIdx, 0, 3)]);
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("ret.avg"), Ui.Gil(avg), Ui.Text, sumW, Ui.Accent);

        ImGui.Spacing();
        if (v.Cards.Count == 0)
        {
            ImGui.TextWrapped(L.T("ret.empty"));
            return;
        }

        // Kästchen: so viele Spalten wie passen, Breite füllt die Zeile.
        var minW = 270 * s;
        var cols = Math.Max(1, (int)((avail + gap) / (minW + gap)));
        var w = (avail - gap * (cols - 1)) / cols;

        for (var i = 0; i < v.Cards.Count; i++)
        {
            var c = v.Cards[i];
            if (i % cols != 0) ImGui.SameLine(0, gap);

            var incomeLine = c.Income > 0 ? L.F("ret.income", Ui.Gil(c.Income)) : L.T("ret.none");
            var lastLine = c.Last != null ? L.F("ret.last", L.Age(c.Last.Value)) : "";
            Ui.RetainerCard(c.Name, ch == null ? c.Character : "", Ui.Gil(c.Gil),
                incomeLine, c.Income > 0 ? Ui.Good : Ui.Dim, lastLine, w);
        }

        ImGui.Spacing();
        ImGui.TextWrapped(L.T("ret.hint"));
    }

    // ---------- Verkäufe ----------

    private void DrawSales()
    {
        var s = ImGuiHelpers.GlobalScale;
        var ch = SelectedChar();
        var from = FromUtc();
        var kindNames = KindNames();

        ImGui.SetNextItemWidth(240 * s);
        ImGui.InputText("##gl_search", ref search, 128);
        ImGui.SameLine();
        ImGui.TextColored(Ui.Dim, L.T("sales.search"));
        ImGui.SameLine(0, 20 * s);
        Combo("##gl_kind", 130 * s, KindFilters(), ref kindFilter);

        if (ImGui.CollapsingHeader(L.T("sales.add") + "###gl_manual"))
        {
            Combo("##gl_mankind", 120 * s, kindNames, ref manKind);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(220 * s);
            ImGui.InputText("##gl_manname", ref manName, 128);
            ImGui.SameLine();
            ImGui.TextColored(Ui.Dim, L.T("man.item"));

            ImGui.SetNextItemWidth(110 * s);
            ImGui.InputInt("##gl_manqty", ref manQty);
            ImGui.SameLine();
            ImGui.TextColored(Ui.Dim, L.T("man.qty"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(160 * s);
            ImGui.InputInt("##gl_mantotal", ref manTotal, 0, 0);
            ImGui.SameLine();
            ImGui.TextColored(Ui.Dim, L.T("man.total"));

            ImGui.SetNextItemWidth(340 * s);
            ImGui.InputText("##gl_mannote", ref manNote, 200);
            ImGui.SameLine();
            ImGui.TextColored(Ui.Dim, L.T("man.note"));

            var canAdd = manTotal > 0 && !string.IsNullOrWhiteSpace(manName) && tracker.CharacterKey != null;
            if (!canAdd) ImGui.BeginDisabled();
            if (ImGui.Button(L.T("man.addBtn") + "##gl_manadd"))
            {
                ledger.AddEntry(new LedgerEntry
                {
                    Kind = manKind == 0 ? EntryKind.Sale : EntryKind.Purchase,
                    ItemName = manName.Trim(),
                    Quantity = Math.Max(1, manQty),
                    TotalGil = manTotal,
                    Character = tracker.CharacterKey ?? L.T("unknown.char"),
                    Source = "Manual",
                    Note = manNote.Trim(),
                });
                manName = "";
                manNote = "";
                manQty = 1;
                manTotal = 0;
            }
            if (!canAdd) ImGui.EndDisabled();
            if (tracker.CharacterKey == null)
            {
                ImGui.SameLine();
                ImGui.TextColored(Ui.Dim, L.T("man.loginOnly"));
            }
        }

        var kf = kindFilter;
        var q = search;
        var rows = salesCache.Get($"{BaseKey()}|{q}|{kf}", () => ledger.Data.Entries
            .Where(e => e.TimeUtc >= from && (ch == null || e.Character == ch))
            .Where(e => !e.IsExpenseEntry)
            .Where(e => kf == 0 || (int)e.Kind == kf - 1)
            .Where(e => q.Length == 0 || e.ItemName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.TimeUtc)
            .Take(1000)
            .ToList());

        ImGui.Spacing();
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable |
                                      ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp;

        Guid? toDelete = null;
        if (ImGui.BeginTable("##gl_sales", 9, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn(L.T("col.time"), ImGuiTableColumnFlags.WidthFixed, 125 * s);
            ImGui.TableSetupColumn(L.T("col.type"), ImGuiTableColumnFlags.WidthFixed, 75 * s);
            ImGui.TableSetupColumn(L.T("col.item"), ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableSetupColumn(L.T("col.qty"), ImGuiTableColumnFlags.WidthFixed, 55 * s);
            ImGui.TableSetupColumn(L.T("col.each"), ImGuiTableColumnFlags.WidthFixed, 95 * s);
            ImGui.TableSetupColumn(L.T("col.total"), ImGuiTableColumnFlags.WidthFixed, 105 * s);
            ImGui.TableSetupColumn(L.T("col.char"), ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn(L.T("col.source"), ImGuiTableColumnFlags.WidthFixed, 105 * s);
            ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 28 * s);
            ImGui.TableHeadersRow();

            for (var i = 0; i < rows.Count; i++)
            {
                var e = rows[i];
                ImGui.PushID(i);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextColored(Ui.Dim, e.TimeUtc.ToLocalTime().ToString("g", L.Culture));
                ImGui.TableNextColumn();
                ImGui.TextColored(e.Kind == EntryKind.Sale ? Ui.Good : Ui.Bad, kindNames[(int)e.Kind]);
                ImGui.TableNextColumn();
                ImGui.Text(L.ItemName(e.ItemName) + (e.IsHq ? " ★" : ""));
                if (!string.IsNullOrEmpty(e.Note) && ImGui.IsItemHovered()) ImGui.SetTooltip(e.Note);
                ImGui.TableNextColumn();
                ImGui.Text(e.Quantity.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(Ui.Gil(e.UnitGil));
                ImGui.TableNextColumn();
                ImGui.TextColored(e.Kind == EntryKind.Sale ? Ui.Good : Ui.Bad, Ui.Gil(e.TotalGil));
                ImGui.TableNextColumn();
                ImGui.TextColored(Ui.Dim, e.Character);
                ImGui.TableNextColumn();
                ImGui.TextColored(Ui.Dim, L.Source(e.Source));
                ImGui.TableNextColumn();
                // Bei aktivem Schutz ("Verkaufsdaten nie löschen") ist das Löschen gesperrt.
                ImGui.BeginDisabled(ledger.ProtectSales);
                if (ImGui.SmallButton("X") && ImGui.GetIO().KeyCtrl) toDelete = e.Id;
                ImGui.EndDisabled();
                if (!ledger.ProtectSales && ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("del.tip"));

                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        if (toDelete != null) ledger.RemoveEntry(toDelete.Value);
    }

    // ---------- Ausgaben & Sonstiges ----------

    private void DrawExpenses()
    {
        var s = ImGuiHelpers.GlobalScale;
        var ch = SelectedChar();
        var from = FromUtc();

        var v = expenseCache.Get(BaseKey(), () => new ExpenseView(
            ledger.GetExpenseTotals(ch, from),
            ledger.Data.Entries
                .Where(e => e.IsExpenseEntry && e.TimeUtc >= from && (ch == null || e.Character == ch))
                .OrderByDescending(e => e.TimeUtc)
                .Take(1000)
                .ToList()));

        var gap = 10 * s;
        var cardW = (ImGui.GetContentRegionAvail().X - gap * 2) / 3f;
        var sums = v.Sums;
        Ui.Card(L.T("exp.cardRepair"), Ui.Gil(sums.Repair), Ui.Bad, cardW, Ui.Bad, L.F("exp.count", sums.RepairCount));
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("exp.cardOut"), Ui.Gil(sums.OtherOut), Ui.Bad, cardW, Ui.Bad, L.F("exp.count", sums.OtherOutCount));
        ImGui.SameLine(0, gap);
        Ui.Card(L.T("exp.cardIn"), Ui.Gil(sums.OtherIn), Ui.Good, cardW, Ui.Good, L.F("exp.count", sums.OtherInCount));

        ImGui.Spacing();
        ImGui.TextWrapped(L.T("exp.hint"));
        ImGui.Spacing();

        if (v.Rows.Count == 0)
        {
            ImGui.TextColored(Ui.Dim, L.T("exp.empty"));
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable |
                                      ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp;

        Guid? toDelete = null;
        if (ImGui.BeginTable("##gl_exp", 5, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn(L.T("col.time"), ImGuiTableColumnFlags.WidthFixed, 125 * s);
            ImGui.TableSetupColumn(L.T("col.type"), ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn(L.T("col.total"), ImGuiTableColumnFlags.WidthFixed, 115 * s);
            ImGui.TableSetupColumn(L.T("col.char"), ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 28 * s);
            ImGui.TableHeadersRow();

            for (var i = 0; i < v.Rows.Count; i++)
            {
                var e = v.Rows[i];
                var income = e.Kind == EntryKind.Sale;
                ImGui.PushID(i);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextColored(Ui.Dim, e.TimeUtc.ToLocalTime().ToString("g", L.Culture));
                ImGui.TableNextColumn();
                var label = e.Source == "Other" ? (income ? L.T("exp.otherIn") : L.T("exp.otherOut")) : L.T("src.Repair");
                ImGui.Text(label);
                if (!string.IsNullOrEmpty(e.Note) && ImGui.IsItemHovered()) ImGui.SetTooltip(e.Note);
                ImGui.TableNextColumn();
                ImGui.TextColored(income ? Ui.Good : Ui.Bad, (income ? "+" : "-") + Ui.Gil(e.TotalGil));
                ImGui.TableNextColumn();
                ImGui.TextColored(Ui.Dim, e.Character);
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(ledger.ProtectSales);
                if (ImGui.SmallButton("X") && ImGui.GetIO().KeyCtrl) toDelete = e.Id;
                ImGui.EndDisabled();
                if (!ledger.ProtectSales && ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("del.tip"));
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        if (toDelete != null) ledger.RemoveEntry(toDelete.Value);
    }

    // ---------- Items ----------

    private void DrawItems()
    {
        var s = ImGuiHelpers.GlobalScale;
        var ch = SelectedChar();
        var from = FromUtc();

        ImGui.TextColored(Ui.Dim, L.T("items.sortBy"));
        ImGui.SameLine();
        Combo("##gl_itemsort", 190 * s, ItemSorts(), ref itemSort);

        var sort = itemSort;
        var sorted = itemsCache.Get($"{BaseKey()}|{sort}", () =>
        {
            var stats = ledger.GetItemStats(ch, from);
            IEnumerable<ItemStat> ordered = sort switch
            {
                1 => stats.OrderByDescending(x => x.SoldQty),
                2 => stats.OrderByDescending(x => x.AvgSale),
                3 => stats.OrderByDescending(x => x.Profit),
                _ => stats.OrderByDescending(x => x.SoldGil),
            };
            return ordered.ToList();
        });

        ImGui.Spacing();
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable |
                                      ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("##gl_items", 7, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn(L.T("col.item"), ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableSetupColumn(L.T("col.sales"), ImGuiTableColumnFlags.WidthFixed, 75 * s);
            ImGui.TableSetupColumn(L.T("col.qty"), ImGuiTableColumnFlags.WidthFixed, 65 * s);
            ImGui.TableSetupColumn(L.T("col.revenue"), ImGuiTableColumnFlags.WidthFixed, 110 * s);
            ImGui.TableSetupColumn(L.T("col.avgSale"), ImGuiTableColumnFlags.WidthFixed, 105 * s);
            ImGui.TableSetupColumn(L.T("col.avgBuy"), ImGuiTableColumnFlags.WidthFixed, 105 * s);
            ImGui.TableSetupColumn(L.T("col.profit"), ImGuiTableColumnFlags.WidthFixed, 110 * s);
            ImGui.TableHeadersRow();

            foreach (var it in sorted)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(L.ItemName(it.Name));
                ImGui.TableNextColumn();
                ImGui.Text(it.SaleCount.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(it.SoldQty.ToString());
                ImGui.TableNextColumn();
                ImGui.TextColored(Ui.Good, Ui.Gil(it.SoldGil));
                ImGui.TableNextColumn();
                ImGui.Text(it.SoldQty > 0 ? Ui.Gil(it.AvgSale) : "–");
                ImGui.TableNextColumn();
                ImGui.Text(it.BoughtQty > 0 ? Ui.Gil(it.AvgBuy) : "–");
                ImGui.TableNextColumn();
                ImGui.TextColored(Ui.SignColor(it.Profit), Ui.Signed(it.Profit));
            }
            ImGui.EndTable();
        }
    }

    // ---------- Einstellungen ----------

    private void ApplyPaths()
    {
        var (ok, msg) = ledger.SetPaths(pathData, pathExport);
        pathOk = ok;
        pathInfo = msg;
        if (ok)
        {
            config.DataDirectory = pathData.Trim();
            config.ExportDirectory = pathExport.Trim();
            config.Save();
        }
    }

    private void DrawSettings()
    {
        var s = ImGuiHelpers.GlobalScale;

        // Sprache
        ImGui.TextColored(Ui.Accent, L.T("section.language"));
        var langItems = new[] { L.T("lang.auto"), "English", "Deutsch", "Français" };
        var lang = Math.Clamp(config.Language, 0, 3);
        if (Combo("##gl_lang", 320 * s, langItems, ref lang))
        {
            config.Language = lang;
            config.Save();
        }

        // Erkennung
        ImGui.Spacing();
        ImGui.TextColored(Ui.Accent, L.T("section.detection"));
        var track = config.TrackChatSales;
        if (ImGui.Checkbox(L.T("chk.chat"), ref track))
        {
            config.TrackChatSales = track;
            config.Save();
        }

        ImGui.SetNextItemWidth(420 * s);
        ImGui.InputText("##gl_kwsale", ref kwSale, 256);
        if (ImGui.IsItemDeactivatedAfterEdit()) { config.SaleKeywords = kwSale; config.Save(); }
        ImGui.SameLine();
        ImGui.TextColored(Ui.Dim, L.T("kw.sale"));

        ImGui.SetNextItemWidth(420 * s);
        ImGui.InputText("##gl_kwbuy", ref kwBuy, 256);
        if (ImGui.IsItemDeactivatedAfterEdit()) { config.PurchaseKeywords = kwBuy; config.Save(); }
        ImGui.SameLine();
        ImGui.TextColored(Ui.Dim, L.T("kw.buy"));

        // Tracking
        ImGui.Spacing();
        ImGui.TextColored(Ui.Accent, L.T("section.tracking"));
        var npc = config.TrackNpcShop;
        if (ImGui.Checkbox(L.T("chk.npc"), ref npc))
        {
            config.TrackNpcShop = npc;
            config.Save();
        }
        var repair = config.TrackRepairs;
        if (ImGui.Checkbox(L.T("chk.repair"), ref repair))
        {
            config.TrackRepairs = repair;
            config.Save();
        }
        var market = config.TrackMarketBuys;
        if (ImGui.Checkbox(L.T("chk.market"), ref market))
        {
            config.TrackMarketBuys = market;
            config.Save();
        }
        var other = config.TrackOtherGil;
        if (ImGui.Checkbox(L.T("chk.other"), ref other))
        {
            config.TrackOtherGil = other;
            config.Save();
        }
        var retSales = config.TrackRetainerSales;
        if (ImGui.Checkbox(L.T("chk.retainer"), ref retSales))
        {
            config.TrackRetainerSales = retSales;
            config.Save();
        }
        var interval = config.SnapshotIntervalSeconds;
        ImGui.SetNextItemWidth(260 * s);
        if (ImGui.SliderInt(L.T("slider.gilInterval"), ref interval, 5, 300))
        {
            config.SnapshotIntervalSeconds = interval;
            config.Save();
        }
        var thinOn = config.ThinHistory;
        if (ImGui.Checkbox(L.T("chk.thin"), ref thinOn))
        {
            config.ThinHistory = thinOn;
            config.Save();
        }
        var thin = config.ThinAfterDays;
        ImGui.BeginDisabled(!config.ThinHistory);
        ImGui.SetNextItemWidth(260 * s);
        if (ImGui.SliderInt(L.T("slider.thin"), ref thin, 7, 365))
        {
            config.ThinAfterDays = thin;
            config.Save();
        }
        ImGui.EndDisabled();
        ImGui.TextColored(Ui.Dim, L.T("thin.help"));

        // Speicherorte
        ImGui.Spacing();
        ImGui.TextColored(Ui.Accent, L.T("section.paths"));
        ImGui.TextWrapped(L.T("paths.help"));

        ImGui.SetNextItemWidth(520 * s);
        ImGui.InputText("##gl_pdata", ref pathData, 500);
        ImGui.SameLine();
        ImGui.TextColored(Ui.Dim, L.T("paths.data"));

        ImGui.SetNextItemWidth(520 * s);
        ImGui.InputText("##gl_pexport", ref pathExport, 500);
        ImGui.SameLine();
        ImGui.TextColored(Ui.Dim, L.T("paths.export"));

        if (ImGui.Button(L.T("paths.apply") + "##gl_papply")) ApplyPaths();
        ImGui.SameLine();
        if (ImGui.Button(L.T("paths.reset") + "##gl_preset"))
        {
            pathData = "";
            pathExport = "";
            ApplyPaths();
        }
        if (pathInfo.Length > 0) ImGui.TextColored(pathOk ? Ui.Good : Ui.Bad, pathInfo);
        ImGui.TextColored(Ui.Dim, L.F("paths.current", ledger.DataDir));
        ImGui.TextColored(Ui.Dim, L.F("paths.exports", ledger.ExportDir));

        var saveMin = config.SaveIntervalMinutes;
        ImGui.SetNextItemWidth(260 * s);
        if (ImGui.SliderInt(L.T("slider.save"), ref saveMin, 1, 60))
        {
            config.SaveIntervalMinutes = saveMin;
            ledger.SaveIntervalMinutes = saveMin;
            config.Save();
        }
        ImGui.TextColored(Ui.Dim, L.T("save.help"));

        // Tooltip
        ImGui.Spacing();
        ImGui.TextColored(Ui.Accent, L.T("section.tooltip"));
        var showTip = config.ShowMarketTooltip;
        if (ImGui.Checkbox(L.T("chk.tooltip"), ref showTip))
        {
            config.ShowMarketTooltip = showTip;
            config.Save();
        }
        // Feste Heimatwelt
        var worldNames = Worlds.Names;
        var homeItems = new List<string> { L.T("home.auto") };
        homeItems.AddRange(worldNames.Select(Worlds.Label));
        var homeIdx = string.IsNullOrWhiteSpace(config.HomeWorld) ? 0 : worldNames.FindIndex(w => string.Equals(w, config.HomeWorld, StringComparison.OrdinalIgnoreCase)) + 1;
        ImGui.TextColored(Ui.Dim, L.T("home.label"));
        ImGui.SameLine();
        if (Combo("##gl_home", 300 * s, homeItems, ref homeIdx))
        {
            config.HomeWorld = homeIdx <= 0 ? "" : worldNames[homeIdx - 1];
            config.Save();
        }
        ImGui.TextColored(Ui.Dim, L.T("home.help"));

        var scopeItems = new[] { L.T("scope.world"), L.T("scope.dc"), L.T("scope.eu"), L.T("scope.custom") };
        var scopeMode = Math.Clamp(config.TooltipScopeMode, 0, 3);
        ImGui.TextColored(Ui.Dim, L.T("scope.label"));
        ImGui.SameLine();
        if (Combo("##gl_scope", 260 * s, scopeItems, ref scopeMode))
        {
            config.TooltipScopeMode = scopeMode;
            config.Save();
        }
        if (scopeMode == 3)
        {
            ImGui.SetNextItemWidth(260 * s);
            ImGui.InputText("##gl_scopecustom", ref scopeCustom, 64);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                config.TooltipCustomScope = scopeCustom.Trim();
                config.Save();
            }
            ImGui.SameLine();
            ImGui.TextColored(Ui.Dim, L.T("scope.customHelp"));
        }
        var compare = config.TooltipCompareHome;
        if (ImGui.Checkbox(L.T("chk.compare"), ref compare))
        {
            config.TooltipCompareHome = compare;
            config.Save();
        }
        var delay = config.TooltipDelayMs;
        ImGui.SetNextItemWidth(260 * s);
        if (ImGui.SliderInt(L.T("slider.delay"), ref delay, 0, 1000))
        {
            config.TooltipDelayMs = delay;
            config.Save();
        }
        var ox = config.TooltipOffsetX;
        ImGui.SetNextItemWidth(260 * s);
        if (ImGui.SliderFloat(L.T("slider.offx"), ref ox, -400f, 400f))
        {
            config.TooltipOffsetX = ox;
            config.Save();
        }
        var oy = config.TooltipOffsetY;
        ImGui.SetNextItemWidth(260 * s);
        if (ImGui.SliderFloat(L.T("slider.offy"), ref oy, -400f, 400f))
        {
            config.TooltipOffsetY = oy;
            config.Save();
        }
        ImGui.TextColored(Ui.Dim, L.T("tip.help"));

        // Daten
        ImGui.Spacing();
        ImGui.TextColored(Ui.Accent, L.T("section.data"));
        if (ImGui.Button(L.T("btn.export") + "##gl_export"))
        {
            try { exportInfo = L.F("export.saved", ledger.ExportCsv(SelectedChar())); }
            catch (Exception ex) { exportInfo = L.F("export.failed", ex.Message); }
        }
        if (exportInfo.Length > 0) ImGui.TextWrapped(exportInfo);

        ImGui.Spacing();
        var protect = config.KeepSalesForever;
        if (ImGui.Checkbox(L.T("chk.protect"), ref protect))
        {
            config.KeepSalesForever = protect;
            ledger.ProtectSales = protect;
            config.Save();
        }
        ImGui.TextColored(Ui.Dim, L.T("protect.help"));

        ImGui.Spacing();
        ImGui.BeginDisabled(ledger.ProtectSales);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        if (ImGui.Button(L.T("btn.deleteAll") + "##gl_delall") && ImGui.GetIO().KeyCtrl) ledger.ClearAll();
        ImGui.PopStyleColor();
        ImGui.EndDisabled();
        if (!ledger.ProtectSales && ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("deleteAll.tip"));

        // Fehlersuche
        ImGui.Spacing();
        ImGui.TextColored(Ui.Accent, L.T("section.debug"));
        var debug = config.DebugChat;
        if (ImGui.Checkbox(L.T("chk.debug"), ref debug))
        {
            config.DebugChat = debug;
            config.Save();
        }
        ImGui.TextWrapped(L.T("debug.help"));
        ImGui.TextColored(Ui.Dim, L.F("chat.status", chat.Status));

        if (config.DebugChat)
        {
            foreach (var line in chat.Recent())
                ImGui.TextColored(Ui.Dim, line);

            ImGui.Spacing();
            ImGui.TextColored(Ui.Accent, L.T("debug.gil"));
            foreach (var line in tracker.RecentGilChanges())
                ImGui.TextColored(Ui.Dim, line);
        }
    }
}
