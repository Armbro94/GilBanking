using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace GilBanking;

/// <summary>
/// ALPHA: liest die Angebotsliste des Marktbretts (Fenster "ItemSearchResult"), während du sie ansiehst, und merkt
/// sich das günstigste Angebot lokal. Rein lesend: es werden nur sichtbare Texte des Fensters gelesen,
/// nichts angeklickt, nichts hochgeladen. Der Aufbau des Fensters ist nicht offiziell dokumentiert, deshalb
/// werden Zeilen nur akzeptiert, wenn Preis × Menge zur Gesamtsumme passt.
/// </summary>
public sealed unsafe class MarketReader : IDisposable
{
    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly Configuration config;
    private readonly LocalMarket store;

    private DateTime next = DateTime.MinValue;
    private uint sessionItem;
    private readonly Dictionary<string, LocalObs> session = new();
    private readonly Dictionary<string, string> lastSig = new();
    private readonly Dictionary<string, DateTime> lastPut = new();

    public static string Status { get; private set; } = "-";
    public static List<string> DebugLines { get; private set; } = new();

    public MarketReader(IFramework framework, IGameGui gameGui, IObjectTable objectTable, Configuration config, LocalMarket store)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.objectTable = objectTable;
        this.config = config;
        this.store = store;
        framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        if (!config.LocalMarketEnabled) return;
        var now = DateTime.UtcNow;
        if (now < next) return;
        next = now.AddMilliseconds(700);

        try
        {
            Scan();
        }
        catch (Exception ex)
        {
            Status = "Fehler: " + ex.Message;
        }
    }

    private void ResetSession()
    {
        sessionItem = 0;
        session.Clear();
        lastSig.Clear();
    }

    private void Scan()
    {
        var addr = ItemTooltip.AddonAddress(gameGui.GetAddonByName("ItemSearchResult"));
        if (addr == 0)
        {
            Status = L.T("local.st.closed");
            ResetSession();
            return;
        }

        var unit = (AtkUnitBase*)addr;
        if (!unit->IsVisible)
        {
            Status = L.T("local.st.closed");
            ResetSession();
            return;
        }

        var rows = new List<List<string>>();
        var other = new List<string>();
        var all = new List<string>();
        Collect(&unit->UldManager, rows, other, all, 0);

        // Welches Item? Der Name steht irgendwo im Fensterkopf.
        uint id = 0;
        foreach (var t in all)
        {
            var c = Clean(t).Trim();
            if (c.Length < 2) continue;
            id = Items.IdByName(c);
            if (id != 0) break;
        }

        // Ersatz: das Item unter dem Mauszeiger (z. B. eine Zeile der Liste), falls der Name nicht gefunden wurde.
        if (id == 0)
        {
            var raw = gameGui.HoveredItem;
            if (raw >= 1_000_000) raw -= 1_000_000;
            else if (raw >= 500_000) raw -= 500_000;
            if (raw > 0 && raw < 500_000 && Items.IsTradable((uint)raw)) id = (uint)raw;
        }

        var dump = new List<string> { $"Item: {(id == 0 ? "?" : Items.NameOf(id))} | Zeilen: {rows.Count}" };
        dump.AddRange(all.Take(25).Select(t => "T: " + t));
        dump.AddRange(rows.Take(4).Select(r => "R: " + string.Join(" | ", r)));
        DebugLines = dump;

        if (id == 0) { Status = L.T("local.st.noItem"); return; }
        if (id != sessionItem) { ResetSession(); sessionItem = id; }

        var currentWorld = objectTable.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ToString() ?? "";
        var worlds = new HashSet<string>(Worlds.Names, StringComparer.OrdinalIgnoreCase);

        var parsed = new List<(long Price, bool Hq, string World)>();
        foreach (var r in rows)
            if (TryParseRow(r, worlds, currentWorld, out var p, out var hq, out var w))
                parsed.Add((p, hq, w));

        if (parsed.Count == 0) { Status = L.F("local.st.noRows", rows.Count); return; }

        // Nur bei Sortierung nach Preis aufsteigend ist der erste Treffer das Minimum.
        for (var i = 1; i < parsed.Count; i++)
        {
            if (parsed[i].Price < parsed[i - 1].Price && parsed[i].World == parsed[i - 1].World)
            {
                Status = L.T("local.st.notSorted");
                return;
            }
        }

        var now = DateTime.UtcNow;
        foreach (var g in parsed.GroupBy(x => x.World))
        {
            var nq = g.Where(x => !x.Hq).Select(x => x.Price).DefaultIfEmpty(0).Min();
            var hq = g.Where(x => x.Hq).Select(x => x.Price).DefaultIfEmpty(0).Min();
            var min = g.Min(x => x.Price);

            // Innerhalb eines geöffneten Fensters bleibt das niedrigste gesehene Minimum
            // (blättert man auf Seite 2, sind die Preise dort höher und sollen nichts überschreiben).
            if (!session.TryGetValue(g.Key, out var o))
            {
                o = new LocalObs { ItemId = id, World = g.Key, Min = min, MinNq = nq, MinHq = hq };
                session[g.Key] = o;
            }
            else
            {
                o.Min = Math.Min(o.Min, min);
                o.MinNq = Lower(o.MinNq, nq);
                o.MinHq = Lower(o.MinHq, hq);
            }

            var sig = $"{o.Min}/{o.MinNq}/{o.MinHq}";
            if (lastSig.TryGetValue(g.Key, out var old) && old == sig
                && lastPut.TryGetValue(g.Key, out var t) && (now - t).TotalSeconds < 60) continue;

            lastSig[g.Key] = sig;
            lastPut[g.Key] = now;
            store.Put(new LocalObs { ItemId = id, World = g.Key, Min = o.Min, MinNq = o.MinNq, MinHq = o.MinHq, SeenUtc = now });
        }

        Status = L.F("local.st.ok", Items.NameOf(id), parsed.Count);
    }

    private static long Lower(long a, long b) => a <= 0 ? b : b <= 0 ? a : Math.Min(a, b);

    // ---------- Fenster auslesen ----------

    private static void Collect(AtkUldManager* m, List<List<string>> rows, List<string> other, List<string> all, int depth)
    {
        if (m == null || depth > 7) return;
        for (var i = 0; i < m->NodeListCount; i++)
        {
            var n = m->NodeList[i];
            if (n == null) continue;

            if (n->Type == NodeType.Text)
            {
                var t = ReadText((AtkTextNode*)n);
                if (t.Length > 0) { other.Add(t); all.Add(t); }
            }
            else if ((int)n->Type >= 1000)
            {
                if ((n->NodeFlags & NodeFlags.Visible) == 0) continue;
                var comp = ((AtkComponentNode*)n)->Component;
                if (comp == null) continue;

                var texts = new List<string>();
                var sub = &comp->UldManager;
                for (var j = 0; j < sub->NodeListCount; j++)
                {
                    var c = sub->NodeList[j];
                    if (c != null && c->Type == NodeType.Text)
                    {
                        var t = ReadText((AtkTextNode*)c);
                        if (t.Length > 0) { texts.Add(t); all.Add(t); }
                    }
                }

                if (texts.Count(x => IsNumeric(Clean(x))) >= 3) rows.Add(texts);
                else Collect(sub, rows, other, all, depth + 1);
            }
        }
    }

    private static string ReadText(AtkTextNode* n)
    {
        try { return n->NodeText.ToString() ?? ""; }
        catch { return ""; }
    }

    /// <summary>Entfernt Symbolzeichen des Spiels (private Unicode-Bereiche, z. B. HQ- oder Gil-Symbol).</summary>
    private static string Clean(string s) =>
        new string(s.Where(ch => ch < '' || ch > '').ToArray());

    private static bool IsNumeric(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return false;
        var digit = false;
        foreach (var ch in s)
        {
            if (char.IsDigit(ch)) digit = true;
            else if (ch is not (',' or '.' or ' ' or ' ' or ' ' or '’' or '\'')) return false;
        }
        return digit;
    }

    private static long Num(string s)
    {
        var d = new string(s.Where(char.IsDigit).ToArray());
        return long.TryParse(d, out var v) ? v : 0;
    }

    private static bool TryParseRow(List<string> texts, HashSet<string> worlds, string currentWorld,
                                    out long price, out bool hq, out string world)
    {
        price = 0;
        hq = texts.Any(t => t.Contains(''));
        world = currentWorld;
        foreach (var t in texts)
        {
            var c = Clean(t).Trim();
            if (worlds.Contains(c)) { world = c; break; }
        }
        if (string.IsNullOrEmpty(world)) return false;

        var nums = texts.Select(Clean).Where(IsNumeric).Select(Num).ToList();
        for (var i = 0; i + 2 < nums.Count; i++)
        {
            long p = nums[i], q = nums[i + 1], t = nums[i + 2];
            if (p <= 0 || q <= 0 || q > 999) continue;
            var expected = (double)p * q;
            if (t >= expected * 0.85 && t <= expected * 1.2)
            {
                price = p;
                return true;
            }
        }
        return false;
    }

    public void Dispose() => framework.Update -= OnUpdate;
}
