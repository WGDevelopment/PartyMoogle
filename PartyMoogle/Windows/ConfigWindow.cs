using System;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using PartyMoogle.Delivery;
using PartyMoogle.Impl;
using PartyMoogle.Notification;
using PartyMoogle.Util;

namespace PartyMoogle.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration Configuration;
    private readonly TimedBool notifSentMessageTimer = new(3.0f);

    // Curated chat types for the Chat rule's filter UI (Phase 1 subset).
    private static readonly (string Label, XivChatType Type)[] ChatTypeChoices =
    [
        ("Tell", XivChatType.TellIncoming),
        ("Party", XivChatType.Party),
        ("Cross-world Party", XivChatType.CrossParty),
        ("Alliance", XivChatType.Alliance),
        ("Free Company", XivChatType.FreeCompany),
        ("Say", XivChatType.Say),
        ("Shout", XivChatType.Shout),
        ("Yell", XivChatType.Yell),
        ("Novice Network", XivChatType.NoviceNetwork)
    ];

    public ConfigWindow(Plugin plugin) : base(
        "PartyMoogle Configuration",
        ImGuiWindowFlags.NoCollapse)
    {
        Configuration = Plugin.Configuration;
        Size = new Vector2(480, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    // ---------- Delivery credentials ----------

    private void DrawNtfyConfig()
    {
        var ntfyServer = Configuration.NtfyServer ?? "";
        if (ImGui.InputText("Server", ref ntfyServer, 2048)) Configuration.NtfyServer = ntfyServer;

        var ntfyTopic = Configuration.NtfyTopic ?? "";
        if (ImGui.InputText("Topic", ref ntfyTopic, 2048)) Configuration.NtfyTopic = ntfyTopic;

        var ntfyToken = Configuration.NtfyToken ?? "";
        if (ImGui.InputText("Token (if any)", ref ntfyToken, 2048)) Configuration.NtfyToken = ntfyToken;
    }

    private void DrawDiscordConfig()
    {
        var discordWebhookToken = Configuration.DiscordWebhookToken ?? "";
        if (ImGui.InputText("Webhook URL", ref discordWebhookToken, 2048))
            Configuration.DiscordWebhookToken = discordWebhookToken;

        var discordMessage = Configuration.DiscordMessage ?? "";
        if (ImGui.InputText("Message (optional)", ref discordMessage, 2048))
            Configuration.DiscordMessage = discordMessage;

        {
            var cfg = Configuration.DiscordUseEmbed;
            if (ImGui.Checkbox("Use embeds?", ref cfg)) Configuration.DiscordUseEmbed = cfg;
        }
        {
            var vec3Col = new Vector3();
            var cfg = Configuration.DiscordEmbedColor;
            vec3Col.X = ((cfg >> 16) & 0xFF) / 255.0f;
            vec3Col.Y = ((cfg >> 8) & 0xFF) / 255.0f;
            vec3Col.Z = (cfg & 0xFF) / 255.0f;
            if (ImGui.ColorEdit3("Embed color", ref vec3Col))
            {
                cfg = ((uint)(vec3Col.X * 255) << 16) | ((uint)(vec3Col.Y * 255) << 8) | (uint)(vec3Col.Z * 255);
                Configuration.DiscordEmbedColor = cfg;
            }
        }
    }

    private void DrawReplyConfig()
    {
        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f),
                          "WARNING: replies are injected as automated chat input.");
        ImGui.TextWrapped(
            "This sends /tell for you via the game's chat box. Automated input violates the "
            + "FFXIV User Agreement and is the category Square Enix enforces against. Opt-in, "
            + "at your own risk. Reply-only, rate-limited, and restricted to players who "
            + "recently sent you a tell.");

        ImGui.Separator();

        {
            var cfg = Configuration.ReplyEnabled;
            if (ImGui.Checkbox("Enable tell-reply (automated input)", ref cfg))
                Configuration.ReplyEnabled = cfg;
        }

        var replyTopic = Configuration.ReplyTopic ?? "";
        if (ImGui.InputText("Reply topic (must differ from outbound)", ref replyTopic, 2048))
            Configuration.ReplyTopic = replyTopic;

        var replyToken = Configuration.ReplyToken ?? "";
        if (ImGui.InputText("Reply token", ref replyToken, 2048, ImGuiInputTextFlags.Password))
            Configuration.ReplyToken = replyToken;
        ImGui.TextDisabled("Stored in plaintext in the plugin config file.");

        {
            var win = Configuration.ReplyAllowlistWindowMinutes;
            if (ImGui.InputInt("Allowlist window (minutes)", ref win))
                Configuration.ReplyAllowlistWindowMinutes = Math.Max(1, win);
        }
        {
            var rl = Configuration.ReplyRateLimitPerMinute;
            if (ImGui.InputInt("Max sends / minute", ref rl))
                Configuration.ReplyRateLimitPerMinute = Math.Max(1, rl);
        }

        ImGui.Separator();
        ImGui.TextDisabled("Reply from your phone with:  <index> your message");
    }

    // ---------- Per-event rules ----------

    private void DrawEventRules()
    {
        foreach (EventCategory category in Enum.GetValues<EventCategory>())
        {
            if (!ImGui.CollapsingHeader(category.ToString()))
                continue;

            using var _ = ImRaii.PushIndent();

            foreach (var (kind, meta) in EventCatalog.Meta)
            {
                if (meta.Category != category)
                    continue;

                using var id = ImRaii.PushId(kind.ToString());

                var enabled = Configuration.EventRules.TryGetValue(kind, out var rule) && rule.Enabled;
                rule ??= Notifier.GetRule(kind);

                if (ImGui.Checkbox(meta.Display, ref enabled))
                    rule.Enabled = enabled;

                if (!enabled)
                    continue;

                using var __ = ImRaii.PushIndent();

                // Per-event channel routing.
                var ntfy = rule.Channels.HasFlag(NotificationChannel.Ntfy);
                var discord = rule.Channels.HasFlag(NotificationChannel.Discord);
                ImGui.Text("Send to:");
                ImGui.SameLine();
                if (ImGui.Checkbox("ntfy", ref ntfy)) SetChannel(rule, NotificationChannel.Ntfy, ntfy);
                ImGui.SameLine();
                if (ImGui.Checkbox("Discord", ref discord)) SetChannel(rule, NotificationChannel.Discord, discord);

                if (meta.Filter == FilterType.Chat)
                    DrawChatFilter(rule);
                else if (meta.Filter == FilterType.Threshold)
                    DrawThresholdFilter(rule);
            }
        }
    }

    private void DrawThresholdFilter(EventRule rule)
    {
        var threshold = rule.Threshold;
        if (ImGui.InputInt("Fire at or below (%)", ref threshold))
            rule.Threshold = Math.Clamp(threshold, 1, 100);
    }

    private static void SetChannel(EventRule rule, NotificationChannel channel, bool on)
    {
        if (on) rule.Channels |= channel;
        else rule.Channels &= ~channel;
    }

    private void DrawChatFilter(EventRule rule)
    {
        ImGui.TextDisabled("Channels (none checked = all):");
        foreach (var (label, type) in ChatTypeChoices)
        {
            var on = rule.ChatTypes.Contains((int)type);
            if (ImGui.Checkbox(label, ref on))
            {
                if (on) rule.ChatTypes.Add((int)type);
                else rule.ChatTypes.Remove((int)type);
            }
        }

        var keyword = rule.KeywordRegex ?? "";
        if (ImGui.InputText("Keyword regex (optional)", ref keyword, 512))
            rule.KeywordRegex = keyword;
    }

    // ---------- Window ----------

    public override void Draw()
    {
        using (var tabBar = ImRaii.TabBar("PmTabs"))
        {
            if (tabBar)
            {
                using (var eventsTab = ImRaii.TabItem("Events"))
                {
                    if (eventsTab) DrawEventRules();
                }
                using (var ntfyTab = ImRaii.TabItem("ntfy"))
                {
                    if (ntfyTab) DrawNtfyConfig();
                }
                using (var discordTab = ImRaii.TabItem("Discord"))
                {
                    if (discordTab) DrawDiscordConfig();
                }
                using (var replyTab = ImRaii.TabItem("Reply"))
                {
                    if (replyTab) DrawReplyConfig();
                }
            }
        }

        ImGui.Separator();

        // Global presence gate.
        {
            var cfg = Configuration.AwayOnly;
            if (ImGui.Checkbox("Only notify while away (AFK or window unfocused)", ref cfg))
                Configuration.AwayOnly = cfg;
        }

        if (Configuration.AwayOnly)
        {
            if (PresenceGate.IsAway())
                ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), "You are away — notifications will fire.");
            else
                ImGui.TextColored(new Vector4(1f, 0.6f, 0f, 1f),
                                  "You are present — notifications are paused until you're away.");
        }

        ImGui.Separator();

        if (ImGui.Button("Send test notification"))
        {
            notifSentMessageTimer.Start();
            MasterDelivery.DeliverToAll("Test notification",
                                        "If you received this, PartyMoogle is configured correctly.");
        }

        if (notifSentMessageTimer.Value)
        {
            ImGui.SameLine();
            ImGui.Text("Notification sent!");
        }

        ImGui.SameLine();
        if (ImGui.Button("Save and close"))
        {
            Configuration.Save();
            // Re-arm the inbound listener so topic/token/enable changes take effect (On() self-resets).
            ReplyListener.On();
            IsOpen = false;
        }
    }
}
