using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using PartyMoogle.Notification;

namespace PartyMoogle.Impl;

/// <summary>
/// The marquee filterable source: incoming chat. A single <see cref="EventKind.Chat"/>
/// rule carries the channel subset (raw XivChatType values) and an optional keyword
/// regex, both applied here before firing.
/// </summary>
public static class ChatListener
{
    public static void On()
    {
        Service.PluginLog.Debug("ChatListener On");
        Service.ChatGui.ChatMessage += OnChatMessage;
    }

    public static void Off()
    {
        Service.PluginLog.Debug("ChatListener Off");
        Service.ChatGui.ChatMessage -= OnChatMessage;
    }

    private static void OnChatMessage(IHandleableChatMessage message)
    {
        var rule = Notifier.GetRule(EventKind.Chat);
        if (!rule.Enabled)
            return;

        var type = message.LogKind;

        // Channel filter: empty set = any type.
        if (rule.ChatTypes.Count > 0 && !rule.ChatTypes.Contains((int)type))
            return;

        var body = message.Message.TextValue;

        // Keyword filter: empty = match all.
        if (!string.IsNullOrWhiteSpace(rule.KeywordRegex))
        {
            try
            {
                if (!Regex.IsMatch(body, rule.KeywordRegex, RegexOptions.IgnoreCase))
                    return;
            }
            catch (RegexParseException)
            {
                // Bad user regex — fail open (notify) rather than silently swallowing.
            }
        }

        var who = message.Sender.TextValue;
        var title = string.IsNullOrEmpty(who) ? $"[{type}]" : $"[{type}] {who}";
        Notifier.Fire(EventKind.Chat, title, body);
    }
}
