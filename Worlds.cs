using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GilBanking;

/// <summary>Liste der öffentlichen Welten (mit Rechenzentrum) für die Auswahl der Heimatwelt.</summary>
internal static class Worlds
{
    private static readonly Dictionary<string, string> Dc = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bekannte Zuordnung Welt → Rechenzentrum. Die Spieldaten ordnen manche Welten (z. B. Light) falsch zu,
    /// deshalb hat diese Liste Vorrang; unbekannte (neue) Welten nehmen den Wert aus den Spieldaten.
    /// </summary>
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cerberus"] = "Chaos",
        ["Louisoix"] = "Chaos",
        ["Moogle"] = "Chaos",
        ["Omega"] = "Chaos",
        ["Phantom"] = "Chaos",
        ["Ragnarok"] = "Chaos",
        ["Sagittarius"] = "Chaos",
        ["Spriggan"] = "Chaos",
        ["Alpha"] = "Light",
        ["Lich"] = "Light",
        ["Odin"] = "Light",
        ["Phoenix"] = "Light",
        ["Raiden"] = "Light",
        ["Shiva"] = "Light",
        ["Twintania"] = "Light",
        ["Zodiark"] = "Light",
        ["Innocence"] = "Shadow",
        ["Pixie"] = "Shadow",
        ["Titania"] = "Shadow",
        ["Tycoon"] = "Shadow",
        ["Bismarck"] = "Materia",
        ["Ravana"] = "Materia",
        ["Sephirot"] = "Materia",
        ["Sophia"] = "Materia",
        ["Zurvan"] = "Materia",
        ["Aegis"] = "Elemental",
        ["Atomos"] = "Elemental",
        ["Carbuncle"] = "Elemental",
        ["Garuda"] = "Elemental",
        ["Gungnir"] = "Elemental",
        ["Kujata"] = "Elemental",
        ["Tonberry"] = "Elemental",
        ["Typhon"] = "Elemental",
        ["Alexander"] = "Gaia",
        ["Bahamut"] = "Gaia",
        ["Durandal"] = "Gaia",
        ["Fenrir"] = "Gaia",
        ["Ifrit"] = "Gaia",
        ["Ridill"] = "Gaia",
        ["Tiamat"] = "Gaia",
        ["Ultima"] = "Gaia",
        ["Anima"] = "Mana",
        ["Asura"] = "Mana",
        ["Chocobo"] = "Mana",
        ["Hades"] = "Mana",
        ["Ixion"] = "Mana",
        ["Masamune"] = "Mana",
        ["Pandaemonium"] = "Mana",
        ["Titan"] = "Mana",
        ["Belias"] = "Meteor",
        ["Mandragora"] = "Meteor",
        ["Ramuh"] = "Meteor",
        ["Shinryu"] = "Meteor",
        ["Unicorn"] = "Meteor",
        ["Valefor"] = "Meteor",
        ["Yojimbo"] = "Meteor",
        ["Zeromus"] = "Meteor",
        ["Adamantoise"] = "Aether",
        ["Cactuar"] = "Aether",
        ["Faerie"] = "Aether",
        ["Gilgamesh"] = "Aether",
        ["Jenova"] = "Aether",
        ["Midgardsormr"] = "Aether",
        ["Sargatanas"] = "Aether",
        ["Siren"] = "Aether",
        ["Behemoth"] = "Primal",
        ["Excalibur"] = "Primal",
        ["Exodus"] = "Primal",
        ["Famfrit"] = "Primal",
        ["Hyperion"] = "Primal",
        ["Lamia"] = "Primal",
        ["Leviathan"] = "Primal",
        ["Ultros"] = "Primal",
        ["Balmung"] = "Crystal",
        ["Brynhildr"] = "Crystal",
        ["Coeurl"] = "Crystal",
        ["Diabolos"] = "Crystal",
        ["Goblin"] = "Crystal",
        ["Malboro"] = "Crystal",
        ["Mateus"] = "Crystal",
        ["Zalera"] = "Crystal",
        ["Cuchulainn"] = "Dynamis",
        ["Golem"] = "Dynamis",
        ["Halicarnassus"] = "Dynamis",
        ["Kraken"] = "Dynamis",
        ["Maduin"] = "Dynamis",
        ["Marilith"] = "Dynamis",
        ["Rafflesia"] = "Dynamis",
        ["Seraph"] = "Dynamis",
    };

    /// <summary>Weltnamen, sortiert nach Rechenzentrum und Name.</summary>
    public static List<string> Names { get; private set; } = new();

    public static void Init(IDataManager data)
    {
        try
        {
            var sheet = data.GetExcelSheet<World>();
            if (sheet == null) return;

            foreach (var w in sheet)
            {
                if (!w.IsPublic) continue;
                var name = w.Name.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                Dc[name] = Known.TryGetValue(name, out var known) ? known : w.DataCenter.ValueNullable?.Name.ToString() ?? "";
            }

            Names = Dc.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Weltenliste konnte nicht geladen werden.");
        }
    }

    public static string? DataCenterOf(string? world)
    {
        if (world == null) return null;
        if (Known.TryGetValue(world, out var k)) return k;
        return Dc.TryGetValue(world, out var d) && d.Length > 0 ? d : null;
    }

    public static string Label(string world) =>
        DataCenterOf(world) is { } d ? $"{world} ({d})" : world;
}
