using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MogCoach.Ingest.Iinact;

/// <summary>
/// Capture-time recorder: connects to IINACT's OverlayPlugin-compatible WebSocket, subscribes to
/// <c>LogLine</c> events, and appends each <c>rawLine</c> to a network-log file that the
/// post-session pipeline later reads via <see cref="IinactCaptureFileSource"/>.
/// <para>
/// Run this on the gaming PC alongside FFXIV while you play; stop it when the session ends. The
/// resulting .log is one half of a "capture" (the other half being the Screenpipe frame DB, which
/// records independently on the same clock).
/// </para>
/// </summary>
public sealed class IinactWebSocketRecorder
{
    private readonly Uri _wsUrl;
    private readonly ILogger<IinactWebSocketRecorder> _log;

    public IinactWebSocketRecorder(Uri wsUrl, ILogger<IinactWebSocketRecorder> log)
    {
        _wsUrl = wsUrl;
        _log = log;
    }

    /// <summary>Records until <paramref name="ct"/> is cancelled. Appends to <paramref name="outputPath"/>.</summary>
    public async Task RecordAsync(string outputPath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var ws = new ClientWebSocket();
        _log.LogInformation("Connecting to IINACT WebSocket at {Url}", _wsUrl);
        await ws.ConnectAsync(_wsUrl, ct).ConfigureAwait(false);

        // OverlayPlugin subscription handshake.
        var subscribe = """{"call":"subscribe","events":["LogLine"]}""";
        await ws.SendAsync(Encoding.UTF8.GetBytes(subscribe), WebSocketMessageType.Text, true, ct)
            .ConfigureAwait(false);

        await using var writer = new StreamWriter(outputPath, append: true);
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                // ArraySegment overload returns the classic WebSocketReceiveResult (the Memory<byte>
                // overload returns a ValueWebSocketReceiveResult struct instead).
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _log.LogInformation("IINACT WebSocket closed by server.");
                    return;
                }
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            var rawLine = ExtractRawLine(sb.ToString());
            if (rawLine is not null)
            {
                await writer.WriteLineAsync(rawLine).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Pulls the <c>rawLine</c> string out of an OverlayPlugin LogLine event message.</summary>
    private static string? ExtractRawLine(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var t) && t.GetString() == "LogLine" &&
                root.TryGetProperty("rawLine", out var raw))
            {
                return raw.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON / control frames are ignored.
        }
        return null;
    }
}
