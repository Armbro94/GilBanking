using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GilBanking;

/// <summary>
/// Liest in festen Abständen nur den Gil-Stand des Charakters und der Retainer aus dem Spielspeicher.
/// Es wird nichts im Spiel ausgelöst oder gesteuert.
/// </summary>
public sealed unsafe class GilTracker : IDisposable
{
    private const uint GilItemId = 1;

    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly Ledger ledger;
    private readonly Configuration config;

    private DateTime nextCheck = DateTime.MinValue;
    private DateTime lastSnapUtc = DateTime.MinValue;
    private long lastGil = -1;
    private long lastRet = -1;
    private int zeroStrikes;

    public string? CharacterKey { get; private set; }
    public long CurrentGil { get; private set; }
    public long SessionStartGil { get; private set; }
    public bool HasSession { get; private set; }

    public GilTracker(IFramework framework, IObjectTable objectTable, Ledger ledger, Configuration config)
    {
        this.framework = framework;
        this.objectTable = objectTable;
        this.ledger = ledger;
        this.config = config;
        framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        if (now < nextCheck) return;
        nextCheck = now.AddSeconds(Math.Max(5, config.SnapshotIntervalSeconds));

        try
        {
            var player = objectTable.LocalPlayer;
            if (player == null)
            {
                if (HasSession)
                {
                    HasSession = false;
                    CharacterKey = null;
                    lastGil = lastRet = -1;
                    ledger.SaveIfDue(true);
                }
                return;
            }

            var world = player.HomeWorld.ValueNullable?.Name.ToString() ?? "?";
            var key = $"{player.Name.TextValue}@{world}";

            var im = InventoryManager.Instance();
            if (im == null) return;
            long gil = im->GetInventoryItemCount(GilItemId);

            // Beim Laden/Zonen kann kurz 0 gelesen werden: ein einzelner Null-Wert wird ignoriert.
            if (gil == 0 && lastGil > 0 && zeroStrikes < 1)
            {
                zeroStrikes++;
                return;
            }
            zeroStrikes = 0;

            var ret = ReadRetainers(key);

            if (!HasSession || key != CharacterKey)
            {
                CharacterKey = key;
                SessionStartGil = gil;
                HasSession = true;
                lastGil = lastRet = -1;
            }

            CurrentGil = gil;

            if (gil != lastGil || ret != lastRet || (now - lastSnapUtc).TotalMinutes >= 30)
            {
                ledger.AddSnapshot(key, gil, ret);
                lastGil = gil;
                lastRet = ret;
                lastSnapUtc = now;
            }

            ledger.SaveIfDue(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler beim Gil-Tracking.");
        }
    }

    /// <summary>
    /// Retainer-Gil ist nur aktuell, wenn das Spiel die Retainer-Liste geladen hat (z. B. nach Öffnen der Rufglocke).
    /// Sind keine gültigen Retainer geladen, wird der zuletzt bekannte Wert verwendet.
    /// </summary>
    private long ReadRetainers(string key)
    {
        var rm = RetainerManager.Instance();
        if (rm != null)
        {
            var count = rm->GetRetainerCount();
            var dict = new Dictionary<string, long>();
            for (uint i = 0; i < count; i++)
            {
                var r = rm->GetRetainerBySortedIndex(i);
                if (r == null || r->RetainerId == 0) continue;
                var name = r->NameString;
                if (string.IsNullOrEmpty(name)) continue;
                dict[name] = r->Gil;
            }
            if (dict.Count > 0) ledger.SetRetainers(key, dict);
        }
        return ledger.RetainerTotal(key);
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
    }
}
