using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GilBanking.Tracking;

/// <summary>
/// Liest Systemnachrichten mit und erkennt Verkäufe/Einkäufe. Rein passiv:
/// es wird nur mitgelesen, nichts gesendet oder ausgelöst.
///
/// Die IChatGui-Events haben sich zwischen Dalamud-Versionen in der Signatur geändert
/// (mal type/timestamp/sender/message, mal ein einzelnes Nachrichten-Objekt). Damit der Code
/// mit beiden kompiliert und läuft, wird der Handler zur Laufzeit passend zum Event gebaut.
/// </summary>
public sealed class ChatTracker : IDisposable
{
    private static readonly Regex GilRegex = new(
        @"(\d[\d.,   ]*)\s*(?:gil|ギル)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IChatGui chat;
    private readonly IDataManager data;
    private readonly Ledger ledger;
    private readonly Configuration config;
    private readonly Func<string?> characterKey;

    private readonly object recentLock = new();
    private readonly List<string> recent = new();

    private EventInfo? evt;
    private Delegate? handler;

    private string lastKey = "";
    private DateTime lastTime = DateTime.MinValue;

    private string statusKey = "chat.notConnected";
    private string statusArg = "";

    public string Status => L.F(statusKey, statusArg);

    public ChatTracker(IChatGui chat, IDataManager data, Ledger ledger, Configuration config, Func<string?> characterKey)
    {
        this.chat = chat;
        this.data = data;
        this.ledger = ledger;
        this.config = config;
        this.characterKey = characterKey;
        Subscribe();
    }

    public List<string> Recent()
    {
        lock (recentLock) return recent.ToList();
    }

    // ---------- Event-Anbindung ----------

    private void Subscribe()
    {
        try
        {
            foreach (var name in new[] { "ChatMessageUnhandled", "ChatMessage" })
            {
                var e = typeof(IChatGui).GetEvent(name);
                if (e?.EventHandlerType == null) continue;

                var invoke = e.EventHandlerType.GetMethod("Invoke")!;
                var ps = invoke.GetParameters()
                    .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                    .ToArray();

                var dispatch = typeof(ChatTracker).GetMethod(nameof(Dispatch), BindingFlags.Instance | BindingFlags.NonPublic)!;
                var boxed = Expression.NewArrayInit(
                    typeof(object),
                    ps.Select(p => (Expression)Expression.Convert(p, typeof(object))));
                var body = Expression.Call(Expression.Constant(this), dispatch, boxed);
                handler = Expression.Lambda(e.EventHandlerType, body, ps).Compile();

                e.AddEventHandler(chat, handler);
                evt = e;
                statusKey = "chat.connected";
                statusArg = name;
                return;
            }
            statusKey = "chat.noEvent";
            Plugin.Log.Warning("GilBanking: kein passendes IChatGui-Event gefunden.");
        }
        catch (Exception ex)
        {
            statusKey = "chat.error";
            statusArg = ex.Message;
            Plugin.Log.Error(ex, "GilBanking: Chat-Event konnte nicht abonniert werden.");
        }
    }

    /// <summary>Wird von jedem Chat-Event aufgerufen, egal welche Signatur es hat.</summary>
    private void Dispatch(object?[] args)
    {
        if (!config.TrackChatSales && !config.DebugChat) return;

        try
        {
            XivChatType? type = null;
            var strings = new List<SeString>();

            foreach (var a in args)
            {
                switch (a)
                {
                    case XivChatType t:
                        type ??= t;
                        break;
                    case SeString s:
                        strings.Add(s);
                        break;
                    case null:
                    case bool:
                    case int:
                        break;
                    default:
                        // Neues API: ein einzelnes Nachrichten-Objekt mit Eigenschaften.
                        ReadMessageObject(a, ref type, strings);
                        break;
                }
            }

            if (type == null || strings.Count == 0) return;

            // Bei (type, ts, sender, message) ist die Nachricht der letzte String.
            var message = strings[^1];
            Handle(type.Value, message);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Fehler beim Auswerten einer Chatnachricht.");
        }
    }

    private static void ReadMessageObject(object obj, ref XivChatType? type, List<SeString> strings)
    {
        var t = obj.GetType();
        foreach (var propName in new[] { "LogKind", "Type" })
        {
            if (t.GetProperty(propName)?.GetValue(obj) is XivChatType ct)
            {
                type ??= ct;
                break;
            }
        }
        if (t.GetProperty("Message")?.GetValue(obj) is SeString msg)
            strings.Add(msg);
    }

    // ---------- Auswertung ----------

    private void Handle(XivChatType type, SeString message)
    {
        var text = message.TextValue;
        var m = GilRegex.Match(text);
        if (!m.Success)
        {
            // Marktbrett-Kauf: "Du hast 99 Stücke X gekauft." nennt keinen Preis, aber Item und Menge.
            HandlePurchaseDetails(type, message, text);
            return;
        }

        // Meldungen mit Gil (außer Spieler-Chat) merken, um sonstige Gil-Einnahmen damit zu beschriften.
        var ti = (int)type;
        if (!((ti >= 10 && ti <= 37) || (ti >= 101 && ti <= 107)))
            GilMessages.Add(text);

        // Marktbrett-Meldungen kommen als Systemnachrichten; Echo erlaubt Tests mit /echo.
        if (type != XivChatType.SystemMessage && type != XivChatType.Notice && type != XivChatType.Echo)
            return;

        if (config.DebugChat)
        {
            lock (recentLock)
            {
                recent.Insert(0, $"[{type}] {text}");
                if (recent.Count > 30) recent.RemoveAt(recent.Count - 1);
            }
        }

        if (!config.TrackChatSales) return;

        var lower = text.ToLowerInvariant();
        EntryKind? kind = null;
        if (ContainsAny(lower, config.SaleKeywords)) kind = EntryKind.Sale;
        else if (ContainsAny(lower, config.PurchaseKeywords)) kind = EntryKind.Purchase;
        if (kind == null) return;

        var digits = new string(m.Groups[1].Value.Where(char.IsDigit).ToArray());
        if (!long.TryParse(digits, out var gil) || gil <= 0) return;

        uint itemId = 0;
        var hq = false;
        var name = "";
        var payload = message.Payloads.OfType<ItemPayload>().FirstOrDefault();
        if (payload != null)
        {
            var id = payload.ItemId;
            if (id > 1_000_000) { id -= 1_000_000; hq = true; }
            else if (id > 500_000) id -= 500_000;
            itemId = id;
            hq |= payload.IsHQ;

            var sheet = data.GetExcelSheet<Item>();
            if (sheet != null && sheet.TryGetRow(itemId, out var row))
                name = row.Name.ToString();
        }

        // Menge: die Zahl direkt vor dem Item-Namen ("Du hast 2 ▷ Item für 434 Gil verkauft").
        var quantity = 1;
        if (name.Length > 0)
        {
            var qm = Regex.Match(text, @"(\d[\d.,]*)[^\w\d]{0,4}" + Regex.Escape(name));
            if (qm.Success)
            {
                var qd = new string(qm.Groups[1].Value.Where(char.IsDigit).ToArray());
                if (int.TryParse(qd, out var q) && q > 0) quantity = q;
            }
        }

        // Doppelte Meldungen innerhalb kurzer Zeit ignorieren.
        var dedupe = $"{kind}|{itemId}|{gil}";
        var now = DateTime.UtcNow;
        if (dedupe == lastKey && (now - lastTime).TotalSeconds < 2) return;
        lastKey = dedupe;
        lastTime = now;

        ledger.AddEntry(new LedgerEntry
        {
            Kind = kind.Value,
            ItemId = itemId,
            ItemName = name,
            IsHq = hq,
            Quantity = quantity,
            TotalGil = gil,
            Character = characterKey() ?? L.T("unknown.char"),
            Source = EntrySources.Chat,
        });
    }

    private void HandlePurchaseDetails(XivChatType type, SeString message, string text)
    {
        if (!config.TrackChatSales || !config.TrackMarketBuys) return;

        // Spieler-Chat (Say, Party, Linkshell, FC …) ignorieren, damit niemand einen Kauf "vortäuschen" kann.
        var t = (int)type;
        if ((t >= 10 && t <= 37) || (t >= 101 && t <= 107)) return;
        if (!ContainsAny(text.ToLowerInvariant(), config.PurchaseKeywords)) return;

        var payload = message.Payloads.OfType<ItemPayload>().FirstOrDefault();
        if (payload == null) return;

        var id = payload.ItemId;
        var hq = payload.IsHQ;
        if (id > 1_000_000) { id -= 1_000_000; hq = true; }
        else if (id > 500_000) id -= 500_000;

        var name = "";
        var sheet = data.GetExcelSheet<Item>();
        if (sheet != null && sheet.TryGetRow(id, out var row))
            name = row.Name.ToString();
        if (string.IsNullOrEmpty(name)) return;

        var qty = 1;
        var qm = System.Text.RegularExpressions.Regex.Match(text, @"\d[\d.,]*");
        if (qm.Success)
        {
            var d = new string(qm.Value.Where(char.IsDigit).ToArray());
            if (int.TryParse(d, out var q) && q > 0) qty = q;
        }

        ledger.AttachPurchaseDetails(characterKey() ?? L.T("unknown.char"), id, name, hq, qty);
    }

    private static bool ContainsAny(string lower, string keywords)
    {
        foreach (var k in keywords.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (lower.Contains(k.ToLowerInvariant())) return true;
        return false;
    }

    public void Dispose()
    {
        try
        {
            if (evt != null && handler != null)
                evt.RemoveEventHandler(chat, handler);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Chat-Event konnte nicht abgemeldet werden.");
        }
    }
}
