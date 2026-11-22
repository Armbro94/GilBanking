using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GilBanking;

/// <summary>
/// Speichert und wertet alle getrackten Daten aus (ledger.json).
/// Schreiben auf die Festplatte passiert selten (einstellbares Intervall), nur bei Änderungen
/// und in einem Hintergrund-Thread, damit das Spiel nicht ruckelt.
/// </summary>
public sealed class Ledger
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly string defaultDir;
    private readonly object writeLock = new();

    private string dataDir;
    private string exportDirCustom;
    private string path;

    private volatile bool dirty;
    private int saving; // 0 = frei, 1 = ein Speichervorgang läuft
    private long lastSaveTick = Environment.TickCount64;

    public LedgerData Data { get; private set; } = new();

    /// <summary>Zählt bei jeder Änderung hoch; wird für das Caching der Auswertungen im UI genutzt.</summary>
    public int Revision { get; private set; }

    /// <summary>Höchstens alle N Minuten wird auf die Festplatte geschrieben (nur bei Änderungen).</summary>
    public int SaveIntervalMinutes { get; set; } = 10;

    /// <summary>Schutz: Verkaufsdaten werden nicht gelöscht (weder einzeln noch alle auf einmal).</summary>
    public bool ProtectSales { get; set; } = true;

    public string DataDir => dataDir;
    public string ExportDir => string.IsNullOrWhiteSpace(exportDirCustom) ? Path.Combine(dataDir, "exports") : exportDirCustom;
    public string DefaultDir => defaultDir;

    public Ledger(string defaultDirectory, string? customDataDir, string? customExportDir)
    {
        defaultDir = defaultDirectory;
        dataDir = defaultDirectory;
        exportDirCustom = customExportDir?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(customDataDir))
        {
            var dir = customDataDir.Trim();
            var err = Prepare(dir);
            if (err == null) dataDir = dir;
            else Plugin.Log.Warning($"GilBanking: Datenordner '{dir}' nicht nutzbar ({err}) – Standardordner wird verwendet.");
        }

        Directory.CreateDirectory(dataDir);
        path = Path.Combine(dataDir, "ledger.json");
        Load();
    }

    // ---------- Laden / Speichern ----------

    private static string? Prepare(string dir)
    {
        try
        {
            if (!Path.IsPathRooted(dir)) return L.T("err.pathRooted");
            Directory.CreateDirectory(dir);
            var test = Path.Combine(dir, ".gilbanking_write_test");
            File.WriteAllText(test, "x");
            File.Delete(test);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Übernimmt einmalig die Daten der früheren Version (Plugin hieß vorher anders), falls im
    /// Standardordner noch keine ledger.json existiert.
    /// </summary>
    private void MigrateLegacy()
    {
        try
        {
            if (File.Exists(path) || !string.Equals(dataDir, defaultDir, StringComparison.OrdinalIgnoreCase)) return;

            var parent = Directory.GetParent(defaultDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (parent == null) return;

            var legacy = Path.Combine(parent.FullName, "Gil" + "Ledger", "ledger.json");
            if (File.Exists(legacy)) File.Copy(legacy, path, false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Daten der Vorgängerversion konnten nicht übernommen werden.");
        }
    }

    private void Load()
    {
        MigrateLegacy();
        try
        {
            Data = File.Exists(path)
                ? JsonSerializer.Deserialize<LedgerData>(File.ReadAllText(path), JsonOpts) ?? new LedgerData()
                : new LedgerData();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "ledger.json konnte nicht gelesen werden – Sicherung wird angelegt.");
            try { File.Copy(path, path + ".broken", true); } catch { /* ignorieren */ }

            // Falls vorhanden, die letzte Tagessicherung verwenden.
            Data = new LedgerData();
            try
            {
                var bak = path + ".bak";
                if (File.Exists(bak))
                {
                    Data = JsonSerializer.Deserialize<LedgerData>(File.ReadAllText(bak), JsonOpts) ?? new LedgerData();
                    Plugin.Log.Warning("GilBanking: Daten aus ledger.json.bak wiederhergestellt.");
                }
            }
            catch
            {
                Data = new LedgerData();
            }
        }
        Revision++;
    }

    private void MarkDirty()
    {
        dirty = true;
        Revision++;
    }

    /// <summary>Kopie für den Hintergrund-Thread (flach; Einträge werden nach dem Anlegen nicht mehr verändert).</summary>
    private LedgerData Copy() => new()
    {
        Entries = new List<LedgerEntry>(Data.Entries),
        Snapshots = Data.Snapshots.ToDictionary(k => k.Key, v => new List<GilSnapshot>(v.Value)),
        Retainers = Data.Retainers.ToDictionary(k => k.Key, v => new Dictionary<string, long>(v.Value)),
        Sessions = Data.Sessions.ToDictionary(k => k.Key, v => new DaySession { Day = v.Value.Day, StartGil = v.Value.StartGil }),
    };

    private void Write(LedgerData copy, string target)
    {
        lock (writeLock)
        {
            // Höchstens einmal pro Tag eine Sicherung der bisherigen Datei anlegen.
            try
            {
                var bak = target + ".bak";
                if (File.Exists(target) &&
                    (!File.Exists(bak) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(bak)).TotalHours >= 24))
                    File.Copy(target, bak, true);
            }
            catch { /* Sicherung ist optional */ }

            var tmp = target + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(copy, JsonOpts));
            File.Move(tmp, target, true);
        }
    }

    /// <summary>
    /// Speichert im Hintergrund, aber nur wenn es Änderungen gibt und das Intervall abgelaufen ist
    /// (oder force gesetzt ist).
    /// </summary>
    public void SaveIfDue(bool force)
    {
        if (!dirty) return;

        var now = Environment.TickCount64;
        if (!force && now - lastSaveTick < Math.Max(1, SaveIntervalMinutes) * 60_000L) return;
        if (Interlocked.CompareExchange(ref saving, 1, 0) != 0) return;

        var copy = Copy();
        var target = path;
        dirty = false;
        lastSaveTick = now;

        _ = Task.Run(() =>
        {
            try { Write(copy, target); }
            catch (Exception ex)
            {
                dirty = true;
                Plugin.Log.Error(ex, "ledger.json konnte nicht gespeichert werden.");
            }
            finally { Interlocked.Exchange(ref saving, 0); }
        });
    }

    /// <summary>Speichert sofort und wartet darauf (beim Entladen des Plugins und beim Ordnerwechsel).</summary>
    public void Flush()
    {
        var sw = Stopwatch.StartNew();
        while (Interlocked.CompareExchange(ref saving, 1, 0) != 0)
        {
            if (sw.ElapsedMilliseconds > 5000) break;
            Thread.Sleep(10);
        }

        try
        {
            if (!dirty) return;
            var copy = Copy();
            dirty = false;
            Write(copy, path);
            lastSaveTick = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            dirty = true;
            Plugin.Log.Error(ex, "ledger.json konnte nicht gespeichert werden.");
        }
        finally
        {
            Interlocked.Exchange(ref saving, 0);
        }
    }

    /// <summary>
    /// Ändert Daten- und Export-Ordner. Leer = Standard. Liegt im neuen Datenordner schon eine ledger.json,
    /// wird diese geladen, sonst werden die aktuellen Daten dorthin geschrieben.
    /// </summary>
    public (bool Ok, string Message) SetPaths(string? newData, string? newExport)
    {
        var data = string.IsNullOrWhiteSpace(newData) ? defaultDir : newData.Trim();
        var export = newExport?.Trim() ?? "";
        var info = "";

        if (!string.Equals(data, dataDir, StringComparison.OrdinalIgnoreCase))
        {
            var err = Prepare(data);
            if (err != null) return (false, L.F("err.dataFolder", err));

            Flush(); // aktuellen Stand noch im alten Ordner sichern

            var target = Path.Combine(data, "ledger.json");
            var existed = File.Exists(target);

            dataDir = data;
            path = target;

            if (existed)
            {
                Load();
                dirty = false;
                info = L.T("msg.loaded");
            }
            else
            {
                MarkDirty();
                Flush();
                info = L.T("msg.written");
            }
        }

        if (export.Length > 0 && !Path.IsPathRooted(export))
            return (false, L.T("err.exportRooted"));
        exportDirCustom = export;

        return (true, info.Length > 0 ? info : L.T("msg.applied"));
    }

    // ---------- Schreiben ----------

    private (DateTime At, string Character, uint Id, string Name, bool Hq, int Qty)? pendingDetail;

    /// <summary>
    /// Item und Menge eines Marktbrett-Kaufs aus der Chat-Meldung. Passt ein erkannter Kauf ohne Namen, wird er ergänzt,
    /// sonst wird die Angabe kurz für den gleich folgenden Kauf gemerkt.
    /// </summary>
    public void AttachPurchaseDetails(string character, uint id, string name, bool hq, int qty)
    {
        for (var i = Data.Entries.Count - 1; i >= 0 && i >= Data.Entries.Count - 30; i--)
        {
            var o = Data.Entries[i];
            if (o.Kind != EntryKind.Purchase || o.Source != "Market" || !string.IsNullOrEmpty(o.ItemName)) continue;
            if (o.Character != character || (DateTime.UtcNow - o.TimeUtc).TotalSeconds > 15) continue;

            o.ItemId = id;
            o.ItemName = name;
            o.IsHq = hq;
            o.Quantity = Math.Max(1, qty);
            MarkDirty();
            return;
        }
        pendingDetail = (DateTime.UtcNow, character, id, name, hq, qty);
    }

    private static bool IsMergeSource(string s) => s == "Chat" || s == "Market" || s == "NPC";

    public void AddEntry(LedgerEntry e)
    {
        if (e.Source == "Market" && string.IsNullOrEmpty(e.ItemName) &&
            pendingDetail is { } p && p.Character == e.Character && (DateTime.UtcNow - p.At).TotalSeconds <= 15)
        {
            e.ItemId = p.Id;
            e.ItemName = p.Name;
            e.IsHq = p.Hq;
            e.Quantity = Math.Max(1, p.Qty);
            pendingDetail = null;
        }

        // Dieselbe Buchung wird oft doppelt erkannt (Gil-Änderung beim Händler/Marktbrett und Chat-Meldung):
        // zu einem Eintrag zusammenführen. Die Chat-Meldung liefert Item und Menge.
        if (e.TotalGil > 0 && IsMergeSource(e.Source))
        {
            for (var i = Data.Entries.Count - 1; i >= 0 && i >= Data.Entries.Count - 30; i--)
            {
                var o = Data.Entries[i];
                if (o.Paired || o.Kind != e.Kind || o.Source == e.Source || !IsMergeSource(o.Source)) continue;
                if (o.Character != e.Character || o.TotalGil != e.TotalGil) continue;
                if (Math.Abs((e.TimeUtc - o.TimeUtc).TotalSeconds) > 15) continue;

                var chat = e.Source == "Chat" ? e : o.Source == "Chat" ? o : null;
                var from = chat != null && !string.IsNullOrEmpty(chat.ItemName) ? chat
                         : string.IsNullOrEmpty(o.ItemName) && !string.IsNullOrEmpty(e.ItemName) ? e
                         : null;
                if (from != null && !ReferenceEquals(from, o))
                {
                    o.ItemId = from.ItemId;
                    o.ItemName = from.ItemName;
                    o.IsHq = from.IsHq;
                    o.Quantity = Math.Max(1, from.Quantity);
                }

                // Die technisch erkannte Quelle (Marktbrett vor Händler) bleibt erhalten, nicht "Chat".
                if (o.Source == "Chat") o.Source = e.Source;
                else if (e.Source == "Market") o.Source = "Market";
                o.Paired = true;
                MarkDirty();
                return;
            }
        }

        Data.Entries.Add(e);
        MarkDirty();
    }

    public void RemoveEntry(Guid id)
    {
        if (ProtectSales) return;
        if (Data.Entries.RemoveAll(e => e.Id == id) > 0) MarkDirty();
    }

    public void AddSnapshot(string character, long gil, long retainerGil)
    {
        if (!Data.Snapshots.TryGetValue(character, out var list))
            Data.Snapshots[character] = list = new List<GilSnapshot>();
        list.Add(new GilSnapshot { TimeUtc = DateTime.UtcNow, Gil = gil, RetainerGil = retainerGil });
        MarkDirty();
    }

    /// <summary>
    /// Gil-Stand zu Beginn des heutigen Tages. Beim ersten Aufruf des Tages wird der aktuelle Stand gemerkt,
    /// ein Relog startet also keine neue Sitzung.
    /// </summary>
    public long DayStart(string character, long gilNow)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (Data.Sessions.TryGetValue(character, out var s) && s.Day == today) return s.StartGil;
        Data.Sessions[character] = new DaySession { Day = today, StartGil = gilNow };
        MarkDirty();
        return gilNow;
    }

    public void SetRetainers(string character, Dictionary<string, long> retainers)
    {
        // Nur bei echter Änderung speichern, sonst würde jede Abfrage einen Schreibvorgang auslösen.
        if (Data.Retainers.TryGetValue(character, out var old) &&
            old.Count == retainers.Count &&
            old.All(kv => retainers.TryGetValue(kv.Key, out var v) && v == kv.Value))
            return;

        Data.Retainers[character] = retainers;
        MarkDirty();
    }

    public void ClearAll()
    {
        if (ProtectSales) return;
        Data = new LedgerData();
        MarkDirty();
        SaveIfDue(true);
    }

    /// <summary>Dünnt alte Gil-Snapshots aus: älter als N Tage bleibt nur der letzte Punkt pro Tag.</summary>
    public void Thin(int afterDays)
    {
        var limit = DateTime.UtcNow.AddDays(-Math.Max(7, afterDays));
        foreach (var key in Data.Snapshots.Keys.ToList())
        {
            var list = Data.Snapshots[key];
            var old = list.Where(s => s.TimeUtc < limit)
                          .GroupBy(s => s.TimeUtc.Date)
                          .Select(g => g.OrderBy(s => s.TimeUtc).Last());
            var recent = list.Where(s => s.TimeUtc >= limit);
            var merged = old.Concat(recent).OrderBy(s => s.TimeUtc).ToList();
            if (merged.Count != list.Count)
            {
                Data.Snapshots[key] = merged;
                MarkDirty();
            }
        }
    }

    // ---------- Lesen / Auswerten ----------

    /// <summary>Entfernt alle Daten eines Charakters (Einträge, Gil-Verlauf, Retainer, Tagesstand).</summary>
    public void RemoveCharacter(string character)
    {
        Data.Entries.RemoveAll(e => e.Character == character);
        Data.Snapshots.Remove(character);
        Data.Retainers.Remove(character);
        Data.Sessions.Remove(character);
        MarkDirty();
    }

    public List<string> Characters()
    {
        return Data.Entries.Select(e => e.Character)
                   .Concat(Data.Snapshots.Keys)
                   .Concat(Data.Retainers.Keys)
                   .Where(c => !string.IsNullOrEmpty(c))
                   .Distinct()
                   .OrderBy(c => c)
                   .ToList();
    }

    private static bool Match(string? filter, string c) => filter == null || filter == c;

    public Totals GetTotals(string? ch, DateTime fromUtc)
    {
        long sales = 0, purch = 0;
        int sc = 0, pc = 0;
        foreach (var e in Data.Entries)
        {
            if (e.TimeUtc < fromUtc || !Match(ch, e.Character) || e.Source == "Other") continue;
            if (e.Kind == EntryKind.Sale) { sales += e.TotalGil; sc++; }
            else { purch += e.TotalGil; pc++; }
        }
        return new Totals(sales, purch, sc, pc);
    }

    public List<(DateTime Day, long Sales, long Purchases)> GetDaily(string? ch, int days)
    {
        var today = DateTime.Now.Date;
        var sales = new long[days];
        var purch = new long[days];

        // Ein Durchlauf über alle Einträge statt einem pro Tag.
        foreach (var e in Data.Entries)
        {
            if (!Match(ch, e.Character) || e.Source == "Other") continue;
            var idx = (int)(e.TimeUtc.ToLocalTime().Date - today).TotalDays + days - 1;
            if (idx < 0 || idx >= days) continue;
            if (e.Kind == EntryKind.Sale) sales[idx] += e.TotalGil; else purch[idx] += e.TotalGil;
        }

        var result = new List<(DateTime, long, long)>(days);
        for (var i = 0; i < days; i++)
            result.Add((today.AddDays(i - days + 1), sales[i], purch[i]));
        return result;
    }

    public List<ItemStat> GetItemStats(string? ch, DateTime fromUtc)
    {
        var map = new Dictionary<string, ItemStat>();
        foreach (var e in Data.Entries.OrderBy(e => e.TimeUtc))
        {
            if (e.TimeUtc < fromUtc || !Match(ch, e.Character)) continue;
            if (e.IsExpenseEntry || e.Source == "Retainer") continue; // Reparatur, Sonstiges, Retainer-Pauschalen sind keine Items
            var key = e.ItemId != 0 ? "#" + e.ItemId : e.ItemName;
            if (!map.TryGetValue(key, out var st))
                map[key] = st = new ItemStat { Key = key, ItemId = e.ItemId };
            if (!string.IsNullOrEmpty(e.ItemName)) st.Name = e.ItemName;

            if (e.Kind == EntryKind.Sale)
            {
                st.SoldQty += e.Quantity;
                st.SoldGil += e.TotalGil;
                st.SaleCount++;
            }
            else
            {
                st.BoughtQty += e.Quantity;
                st.BoughtGil += e.TotalGil;
            }
        }
        return map.Values.ToList();
    }

    /// <summary>Vermögen (Gil + Retainer-Gil) über die Zeit. Bei "Alle" werden die Charaktere aufsummiert.</summary>
    public List<(DateTime T, double V)> GetWealthSeries(string? ch, DateTime fromUtc)
    {
        var events = new List<(DateTime T, string C, long W)>();
        foreach (var kv in Data.Snapshots)
        {
            if (!Match(ch, kv.Key)) continue;
            foreach (var s in kv.Value) events.Add((s.TimeUtc, kv.Key, s.Gil + s.RetainerGil));
        }
        events.Sort((a, b) => a.T.CompareTo(b.T));

        var last = new Dictionary<string, long>();
        var res = new List<(DateTime, double)>();
        foreach (var e in events)
        {
            last[e.C] = e.W;
            if (e.T >= fromUtc) res.Add((e.T, last.Values.Sum()));
        }
        return res;
    }

    public (long Gil, long Retainer) LatestWealth(string? ch)
    {
        long g = 0, r = 0;
        foreach (var kv in Data.Snapshots)
        {
            if (!Match(ch, kv.Key) || kv.Value.Count == 0) continue;
            var s = kv.Value[^1];
            g += s.Gil;
            r += s.RetainerGil;
        }
        return (g, r);
    }

    /// <summary>Summen der Ausgaben-Einträge (Reparatur, sonstige Ausgaben, sonstige Einnahmen) im Zeitraum.</summary>
    public (long Repair, int RepairCount, long OtherOut, int OtherOutCount, long OtherIn, int OtherInCount) GetExpenseTotals(string? ch, DateTime fromUtc)
    {
        long rep = 0, oo = 0, oi = 0;
        int rc = 0, ooc = 0, oic = 0;
        foreach (var e in Data.Entries)
        {
            if (!e.IsExpenseEntry || e.TimeUtc < fromUtc || !Match(ch, e.Character)) continue;
            if (e.Source == "Other")
            {
                if (e.Kind == EntryKind.Sale) { oi += e.TotalGil; oic++; }
                else { oo += e.TotalGil; ooc++; }
            }
            else { rep += e.TotalGil; rc++; }
        }
        return (rep, rc, oo, ooc, oi, oic);
    }

    public long RetainerTotal(string character) =>
        Data.Retainers.TryGetValue(character, out var d) ? d.Values.Sum() : 0;

    public List<(string Character, string Name, long Gil)> RetainerList(string? ch)
    {
        var list = new List<(string, string, long)>();
        foreach (var kv in Data.Retainers)
        {
            if (!Match(ch, kv.Key)) continue;
            foreach (var r in kv.Value) list.Add((kv.Key, r.Key, r.Value));
        }
        return list.OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToList();
    }

    public string ExportCsv(string? ch)
    {
        var dir = ExportDir;
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"gilbanking_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var sb = new StringBuilder();
        sb.AppendLine(L.T("csv.header"));
        foreach (var e in Data.Entries.Where(e => Match(ch, e.Character)).OrderBy(e => e.TimeUtc))
        {
            sb.Append(e.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append(';')
              .Append(e.Kind == EntryKind.Sale ? L.T("kind.sale") : L.T("kind.purchase")).Append(';')
              .Append(Csv(L.ItemName(e.ItemName))).Append(';')
              .Append(e.IsHq ? L.T("yes") : L.T("no")).Append(';')
              .Append(e.Quantity).Append(';')
              .Append(e.UnitGil).Append(';')
              .Append(e.TotalGil).Append(';')
              .Append(Csv(e.Character)).Append(';')
              .Append(Csv(e.Source)).Append(';')
              .AppendLine(Csv(e.Note));
        }
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        return file;
    }

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
