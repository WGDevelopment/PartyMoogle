using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;
using PartyMoogle.Notification;

namespace PartyMoogle;

[Serializable]
public class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? PluginInterface;

    // --- Delivery credentials (ntfy + Discord only) ---
    public string NtfyServer { get; set; } = "https://ntfy.sh/";
    public string NtfyTopic { get; set; } = "";
    public string NtfyToken { get; set; } = "";

    public string DiscordWebhookToken { get; set; } = "";
    public string DiscordMessage { get; set; } = "";
    public bool DiscordUseEmbed { get; set; } = true;
    public uint DiscordEmbedColor { get; set; } = 0x00FF00;

    // --- Presence: single global gate (away = AFK OR window unfocused) ---
    public bool AwayOnly { get; set; } = true;

    // Collapse identical notifications repeated within this many seconds. 0 disables.
    public int ThrottleSeconds { get; set; } = 5;

    // --- Reply (inbound): phone -> ntfy -> in-game /tell. OPT-IN, OFF by default. ---
    // This path injects chat via the game's chat box (automated input); see the Reply
    // tab warning. All fields default to the safe/disarmed state.
    public bool ReplyEnabled { get; set; } = false;

    // Dedicated INBOUND topic — MUST be distinct from NtfyTopic and authenticated.
    public string ReplyTopic { get; set; } = "";
    public string ReplyToken { get; set; } = "";

    // A reply only resolves to a tell-sender seen within this many minutes; older = rejected.
    public int ReplyAllowlistWindowMinutes { get; set; } = 15;

    // Max injected /tells per minute (human-plausible throttle; floods are dropped, not queued).
    public int ReplyRateLimitPerMinute { get; set; } = 6;

    // --- Per-event rules ---
    public Dictionary<EventKind, EventRule> EventRules { get; set; } = new();

    public int Version { get; set; } = 3;

    // --- Legacy PushyFinder fields, kept only for one-time migration ---
    [Obsolete("Migrated to EventRules[DutyPop]. Do not read.")]
    public bool EnableForDutyPops { get; set; } = true;
    [Obsolete("Migrated to !AwayOnly. Do not read.")]
    public bool IgnoreAfkStatus { get; set; } = false;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        EnsureDefaults();
    }

    /// <summary>
    /// Seed a rule for every catalog entry on first run, and migrate PushyFinder
    /// config. Idempotent — safe to call on every load.
    /// </summary>
    private void EnsureDefaults()
    {
#pragma warning disable CS0618 // intentional read of obsolete migration fields
        var firstRun = EventRules.Count == 0;
        if (firstRun && Version < 2)
        {
            AwayOnly = !IgnoreAfkStatus;
        }
#pragma warning restore CS0618

        foreach (var kind in EventCatalog.Meta.Keys)
        {
            if (EventRules.ContainsKey(kind))
                continue;

            var rule = new EventRule();

            // Sensible first-run defaults: the classic PushyFinder set on, rest off.
            switch (kind)
            {
                case EventKind.DutyPop:
                case EventKind.PartyJoin:
                case EventKind.PartyLeave:
                case EventKind.PartyFull:
                    rule.Enabled = true;
                    break;
                case EventKind.Chat:
                    // Default chat filter to incoming tells so it isn't a firehose.
                    rule.ChatTypes.Add((int)Dalamud.Game.Text.XivChatType.TellIncoming);
                    break;
            }

            EventRules[kind] = rule;
        }

        // v2 -> v3: reply fields added. Their initializers already supply safe/disarmed
        // defaults to deserialized v2 configs, so there is nothing to backfill — just
        // normalize the stamp. Idempotent: EnsureDefaults runs on every load.
        Version = 3;
    }

    public void Save()
    {
        PluginInterface!.SavePluginConfig(this);
    }
}
