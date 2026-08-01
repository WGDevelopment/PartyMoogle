using System;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Utility;
using Flurl.Http;
using PartyMoogle.Discord;
using PartyMoogle.Notification;

namespace PartyMoogle.Delivery;

internal class DiscordDelivery : IDelivery
{
    public NotificationChannel Channel => NotificationChannel.Discord;

    public bool IsActive => !Plugin.Configuration.DiscordWebhookToken.IsNullOrWhitespace() &&
                            Uri.IsWellFormedUriString(Plugin.Configuration.DiscordWebhookToken, UriKind.Absolute);

    public void Deliver(string title, string text)
    {
        Task.Run(() => DeliverAsync(title, text));
    }

    private static async Task DeliverAsync(string title, string text)
    {
        const string iconUrl =
            "https://raw.githubusercontent.com/WGDevelopment/PartyMoogle/main/PartyMoogle/images/icon.png";
        const string repoUrl = "https://github.com/WGDevelopment/PartyMoogle";

        var webhook = new WebhookBuilder();

        if (!Plugin.Configuration.DiscordUseEmbed)
            webhook.WithContent(title + "\n" + text);
        else
        {
            webhook
                .WithContent(Plugin.Configuration.DiscordMessage)
                .WithEmbed(new EmbedBuilder()
                              .WithDescription(text)
                              .WithTitle(title)
                              .WithColor(Plugin.Configuration.DiscordEmbedColor)
                              .WithAuthor("PartyMoogle", repoUrl, iconUrl));
        }

        webhook.WithAvatarUrl(iconUrl);
        webhook.WithUsername("PartyMoogle");

        try
        {
            // this can break if they register a webhook to a channel type of forum or media
            await Plugin.Configuration.DiscordWebhookToken.PostJsonAsync(webhook.Build());
            Service.PluginLog.Information($"Sent Discord notification: {title}");
        }
        catch (FlurlHttpException e)
        {
            // Discord returns a json object within the message that contains the error not sure if this is something that should be parsed or not
            Service.PluginLog.Error($"Failed to make Discord request: '{e.Message}'");
            Service.PluginLog.Error($"{e.StackTrace}");
            Service.PluginLog.Debug(JsonSerializer.Serialize(webhook.Build()));
        }
        catch (ArgumentException e)
        {
            Service.PluginLog.Error($"{e.StackTrace}");
        }
    }
}
