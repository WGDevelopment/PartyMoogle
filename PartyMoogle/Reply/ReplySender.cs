using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PartyMoogle.Reply;

/// <summary>
/// Turns an inbound phone message ("&lt;index&gt; body") into a validated /tell and injects it on
/// the framework thread. Every send path — the ntfy listener and the debug subcommand — routes
/// through <see cref="HandleInbound"/> so the rate limiter and every rejection are a single choke.
/// </summary>
public static class ReplySender
{
    // "17 hello there" -> group1 = "17", group2 = "hello there"
    private static readonly Regex LeadingIndex =
        new(@"^\s*(\d{1,9})\s+(.*)$", RegexOptions.Singleline | RegexOptions.Compiled);

    // Any FFXIV text placeholder token: <pos>, <flag>, <t>, <me>, <r>, ...
    private static readonly Regex Placeholder = new(@"<[^>]*>", RegexOptions.Compiled);

    private static readonly object RateGate = new();
    private static readonly Queue<DateTime> SendTimes = new();

    /// <summary>
    /// Validate, rate-limit, and (if all checks pass) inject the reply on the framework thread.
    /// Never throws to the caller; all failures are logged and dropped.
    /// </summary>
    public static async Task HandleInbound(string id, string rawMessage)
    {
        if (!BuildCommand(rawMessage, out var command, out var reason))
        {
            Service.PluginLog.Info($"Reply {id} rejected: {reason}");
            return;
        }

        if (!TryConsumeRateToken())
        {
            Service.PluginLog.Warning(
                $"Reply {id} dropped: rate limit ({Plugin.Configuration.ReplyRateLimitPerMinute}/min) exceeded.");
            return;
        }

        try
        {
            await Service.Framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    ChatBox.SendMessage(command!);
                    Service.PluginLog.Debug($"Reply {id} sent.");
                }
                catch (Exception ex)
                {
                    Service.PluginLog.Error($"Reply {id} send failed on framework thread: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error($"Reply {id} dispatch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Pure, side-effect-free (except reading config/store) construction + validation of the
    /// /tell command. Returns false with a reason on any rejection. Ordering matters: cheap
    /// structural checks, then the allowlist gate, then content defenses, then the full-command
    /// invariants — nothing reaches the framework thread unless every check passes.
    /// </summary>
    internal static bool BuildCommand(string rawMessage, out string? command, out string reason)
    {
        command = null;

        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            reason = "empty message";
            return false;
        }

        var m = LeadingIndex.Match(rawMessage);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var index))
        {
            reason = "no leading '<index> ' prefix";
            return false;
        }

        var body = m.Groups[2].Value.Trim();
        if (body.Length == 0)
        {
            reason = "empty body";
            return false;
        }

        var target = ReplyTargetStore.Resolve(index, Plugin.Configuration.ReplyAllowlistWindowMinutes);
        if (target is null)
        {
            reason = $"index {index} unknown or expired";
            return false;
        }

        // Game text placeholders would expand and leak position/target — reject outright.
        if (Placeholder.IsMatch(body))
        {
            reason = "body contains a <...> placeholder";
            return false;
        }

        // The body must never itself become a slash-command.
        if (body.StartsWith("/", StringComparison.Ordinal))
        {
            reason = "body starts with '/'";
            return false;
        }

        var sanitizedBody = ChatBox.Sanitize(body);
        if (sanitizedBody != body)
        {
            reason = "body contained invalid characters";
            return false;
        }

        var t = target.Value;
        var cmd = $"/tell {t.Name}@{t.World} {sanitizedBody}";

        // Validate the FULLY-ASSEMBLED command before the framework hop (red-team H2/M4).
        if (Encoding.UTF8.GetByteCount(cmd) > 500)
        {
            reason = "assembled command exceeds 500 bytes";
            return false;
        }

        if (ChatBox.Sanitize(cmd).Length != cmd.Length)
        {
            reason = "assembled command failed the sanitize invariant";
            return false;
        }

        command = cmd;
        reason = "ok";
        return true;
    }

    /// <summary>Sliding-window limiter shared by all send paths. Reject (drop), never queue.</summary>
    private static bool TryConsumeRateToken()
    {
        var now = DateTime.UtcNow;
        var limit = Math.Max(1, Plugin.Configuration.ReplyRateLimitPerMinute);

        lock (RateGate)
        {
            while (SendTimes.Count > 0 && (now - SendTimes.Peek()).TotalSeconds >= 60)
                SendTimes.Dequeue();

            if (SendTimes.Count >= limit)
                return false;

            SendTimes.Enqueue(now);
            return true;
        }
    }
}
