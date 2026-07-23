using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Utility;
using Flurl.Http;
using PartyMoogle.Notification;

namespace PartyMoogle.Delivery;

public class NtfyDelivery : IDelivery
{
    public NotificationChannel Channel => NotificationChannel.Ntfy;

    public bool IsActive => !Plugin.Configuration.NtfyServer.IsNullOrWhitespace() &&
                            !Plugin.Configuration.NtfyTopic.IsNullOrWhitespace() && 
                            Uri.IsWellFormedUriString(Plugin.Configuration.NtfyServer, UriKind.Absolute);

    public void Deliver(string title, string text)
    {
        Task.Run(() => DeliverAsync(title, text));
    }

    private static async Task DeliverAsync(string title, string text)
    {
        // Publish using ntfy's JSON format (POST an object to the server root) rather than
        // POSTing a bare string to /{topic}. Posting a raw string serialized it as a JSON
        // string *literal*, so ntfy stored the surrounding quotes and \uXXXX escapes as
        // message text (apostrophes arrived as a literal '). This also gives us a
        // real title field instead of cramming "title: body" into the message.
        var payload = new Dictionary<string, object>
        {
            ["topic"] = Plugin.Configuration.NtfyTopic,
            ["message"] = text
        };
        if (!string.IsNullOrEmpty(title))
            payload["title"] = title;

        IFlurlRequest request = new FlurlRequest(Plugin.Configuration.NtfyServer);
        if (!Plugin.Configuration.NtfyToken.IsNullOrWhitespace())
            request = request.WithOAuthBearerToken(Plugin.Configuration.NtfyToken);

        try
        {
            await request.PostJsonAsync(payload);
            Service.PluginLog.Debug("Sent Ntfy message");
        }
        catch (FlurlHttpException e)
        {
            Service.PluginLog.Error($"Failed to make Ntfy request: '{e.Message}'");
            Service.PluginLog.Error($"{e.StackTrace}");
        }
        catch (ArgumentException e)
        {
            Service.PluginLog.Error($"{e.StackTrace}");
        }

    }
}
