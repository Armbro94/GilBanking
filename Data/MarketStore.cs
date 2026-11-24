using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GilBanking.Data;

/// <summary>Kleine JSON-Datei in einem frei wählbaren Ordner (Preis-Cache, selbst gesehene Marktpreise).</summary>
public sealed class JsonStore<T> where T : new()
{
    private readonly string fileName;
    private readonly string defaultDir;
    private DateTime lastSave = DateTime.UtcNow;
    private int saving;

    public readonly object Sync = new();
    public T Data { get; private set; } = new();
    public string Dir { get; private set; }
    public string? LastError { get; private set; }
    public volatile bool Dirty;

    public string FilePath => Path.Combine(Dir, fileName);

    public JsonStore(string fileName, string defaultDir, string configuredDir)
    {
        this.fileName = fileName;
        this.defaultDir = defaultDir;
        Dir = Resolve(configuredDir);
        Load();
    }

    private string Resolve(string configured) =>
        string.IsNullOrWhiteSpace(configured) ? defaultDir : configured.Trim();

    public void Load()
    {
        lock (Sync)
        {
            try
            {
                Data = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath)) ?? new T()
                    : new T();
                LastError = null;
            }
            catch (Exception ex)
            {
                Data = new T();
                LastError = ex.Message;
                Plugin.Log.Warning(ex, $"GilBanking: {fileName} konnte nicht gelesen werden.");
            }
        }
    }

    public void Save()
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Data));
                File.Move(tmp, FilePath, true);
                Dirty = false;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Plugin.Log.Warning(ex, $"GilBanking: {fileName} konnte nicht gespeichert werden.");
            }
        }
        lastSave = DateTime.UtcNow;
    }

    /// <summary>Speichert im Hintergrund, aber höchstens alle N Sekunden.</summary>
    public void SaveSoon(int minSeconds = 120)
    {
        if ((DateTime.UtcNow - lastSave).TotalSeconds < minSeconds) return;
        if (Interlocked.Exchange(ref saving, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try { Save(); }
            finally { Interlocked.Exchange(ref saving, 0); }
        });
    }

    /// <summary>Anderen Ordner wählen. Liegt dort schon eine Datei, wird sie geladen, sonst wird die aktuelle dorthin geschrieben.</summary>
    public bool SetDir(string configured, out string error)
    {
        error = "";
        var nd = Resolve(configured);
        try
        {
            Directory.CreateDirectory(nd);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!string.Equals(nd, Dir, StringComparison.OrdinalIgnoreCase))
            Flush();

        Dir = nd;
        if (File.Exists(FilePath)) Load();
        else Save();
        if (LastError != null) { error = LastError; return false; }
        return true;
    }

    public void Flush()
    {
        if (Dirty) Save();
    }
}

/// <summary>
/// Offline-Cache der Universalis-Preise. Ist Universalis nicht erreichbar, werden die zuletzt gespeicherten
/// Werte als "veraltet" angezeigt. Speicherort frei wählbar (Einstellungen).
/// </summary>
public sealed class PriceCache
{
    private readonly JsonStore<Dictionary<string, MarketInfo>> store;
    private readonly Configuration config;

    public PriceCache(string defaultDir, Configuration config)
    {
        this.config = config;
        store = new JsonStore<Dictionary<string, MarketInfo>>("pricecache.json", defaultDir, config.PriceCacheDirectory);
        Prune();
    }

    public JsonStore<Dictionary<string, MarketInfo>> Store => store;
    public bool Enabled => config.PriceCacheEnabled;

    public int Count
    {
        get { lock (store.Sync) return store.Data.Count; }
    }

    public MarketInfo? Get(string key)
    {
        if (!Enabled) return null;
        lock (store.Sync) return store.Data.TryGetValue(key, out var m) ? m : null;
    }

    public void Put(string key, MarketInfo info)
    {
        if (!Enabled || !info.Ok || info.NoData) return;
        lock (store.Sync) store.Data[key] = info;
        store.Dirty = true;
        store.SaveSoon();
    }

    public void Prune()
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, config.PriceCacheDays));
        lock (store.Sync)
        {
            foreach (var k in store.Data.Where(kv => kv.Value.DataUtc < cutoff).Select(kv => kv.Key).ToList())
                store.Data.Remove(k);
        }
        store.Dirty = true;
    }

    public void Clear()
    {
        lock (store.Sync) store.Data.Clear();
        store.Save();
    }

    public void Flush()
    {
        Prune();
        store.Flush();
    }
}

/// <summary>Ein vom Plugin selbst am Marktbrett gesehener Preis (günstigstes Angebot je Welt und Item).</summary>
public sealed class LocalObs
{
    public uint ItemId { get; set; }
    public string World { get; set; } = "";
    public long Min { get; set; }
    public long MinNq { get; set; }
    public long MinHq { get; set; }
    public DateTime SeenUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Selbst beobachtete Marktbrett-Preise (Alpha). Speicherort frei wählbar (Einstellungen).</summary>
public sealed class LocalMarket
{
    private readonly JsonStore<Dictionary<string, LocalObs>> store;
    private readonly Configuration config;

    public LocalMarket(string defaultDir, Configuration config)
    {
        this.config = config;
        store = new JsonStore<Dictionary<string, LocalObs>>("localmarket.json", defaultDir, config.LocalMarketDirectory);
        Prune();
    }

    public JsonStore<Dictionary<string, LocalObs>> Store => store;

    public int Count
    {
        get { lock (store.Sync) return store.Data.Count; }
    }

    public void Put(LocalObs o)
    {
        lock (store.Sync) store.Data[$"{o.World}/{o.ItemId}"] = o;
        store.Dirty = true;
        store.SaveSoon(60);
    }

    /// <summary>Günstigster selbst gesehener Preis im Bereich (Welt, Rechenzentrum oder "Europe").</summary>
    public LocalObs? Best(uint itemId, string? scope)
    {
        if (string.IsNullOrEmpty(scope)) return null;
        lock (store.Sync)
        {
            LocalObs? best = null;
            foreach (var o in store.Data.Values)
            {
                if (o.ItemId != itemId || o.Min <= 0) continue;
                var inScope = scope.Equals("Europe", StringComparison.OrdinalIgnoreCase)
                              || scope.Equals(o.World, StringComparison.OrdinalIgnoreCase)
                              || scope.Equals(Worlds.DataCenterOf(o.World), StringComparison.OrdinalIgnoreCase);
                if (!inScope) continue;
                if (best == null || o.Min < best.Min) best = o;
            }
            return best;
        }
    }

    public void Prune()
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, config.LocalMarketDays));
        lock (store.Sync)
        {
            foreach (var k in store.Data.Where(kv => kv.Value.SeenUtc < cutoff).Select(kv => kv.Key).ToList())
                store.Data.Remove(k);
        }
        store.Dirty = true;
    }

    public void Clear()
    {
        lock (store.Sync) store.Data.Clear();
        store.Save();
    }

    public void Flush()
    {
        Prune();
        store.Flush();
    }
}
