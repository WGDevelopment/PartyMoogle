using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using PartyMoogle.Notification;
using PartyMoogle.Reply;

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

        // Incoming tells get an indexed, repliable title so the phone can address a reply
        // back to the exact sender. All other chat types keep the plain "[type] who" form.
        var title = type == XivChatType.TellIncoming
            ? BuildTellTitle(message, who)
            : string.IsNullOrEmpty(who) ? $"[{type}]" : $"[{type}] {who}";

        Notifier.Fire(EventKind.Chat, title, body);
    }

    /// <summary>
    /// Record the tell sender in <see cref="ReplyTargetStore"/> and produce a title that
    /// carries its reply index. Falls back to a non-repliable "[Tell]" title when the
    /// sender can't be resolved to a name+world (fail-closed — no guessed recipients).
    /// Runs on the framework thread (ChatGui event), so the store write is safe.
    /// </summary>
    private static string BuildTellTitle(IHandleableChatMessage message, string who)
    {
        PlayerPayload? player = null;
        foreach (var payload in message.Sender.Payloads)
            if (payload is PlayerPayload pp) { player = pp; break; }

        if (player is not null)
        {
            var name = player.PlayerName;
            var world = ResolveWorld(player);
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(world))
            {
                var index = ReplyTargetStore.Record(name!, world!);
                Service.PluginLog.Debug($"Recorded reply target #{index}: {name}@{world}");
                return $"Tell #{index} from {name}@{world}";
            }
        }

        Service.PluginLog.Debug("Incoming tell without a resolvable sender; non-repliable.");
        return string.IsNullOrEmpty(who) ? "[Tell]" : $"[Tell] {who}";
    }

    /// <summary>
    /// World name from the sender's <see cref="PlayerPayload"/>. Same-world tells often omit
    /// the world payload; in that case fall back to the local player's home world.
    /// </summary>
    private static string? ResolveWorld(PlayerPayload player)
    {
        try
        {
            var w = player.World.Value.Name.ExtractText();
            if (!string.IsNullOrEmpty(w))
                return w;
        }
        catch
        {
            // Fall through to home-world resolution below.
        }

        try
        {
            var home = Service.ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ExtractText();
            return string.IsNullOrEmpty(home) ? null : home;
        }
        catch
        {
            return null;
        }
    }
}
