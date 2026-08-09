using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MogCoach.Ingest.Iinact;

/// <summary>Result of probing the IINACT WebSocket for live LogLine events.</summary>
public sealed record IinactProbeResult
{
    public bool Connected { get; init; }
    public int LogLineCount { get; init; }
    public string? SampleRawLine { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Connects to the IINACT OverlayPlugin WebSocket, subscribes to LogLine, and listens briefly to
/// confirm events actually flow. Used by <c>mogcoach doctor</c> so setup problems (wrong port, WS
/// not enabled, not in a duty) are obvious. Zero events while connected is a soft warning, not a
/// failure — you may simply be standing in a town.
/// </summary>
public static class IinactDiagnostics
{
    public static async Task<IinactProbeResult> ProbeAsync(Uri wsUrl, TimeSpan listenFor, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(listenFor);
        var token = timeoutCts.Token;

        try
        {
            using var ws = new ClientWebSocket();
            // Bound the connect by the listen window too, so an unreachable/filtered host can't hang.
            await ws.ConnectAsync(wsUrl, token).ConfigureAwait(false);

            await ws.SendAsync(
                Encoding.UTF8.GetBytes("""{"call":"subscribe","events":["LogLine"]}"""),
                WebSocketMessageType.Text, true, token).ConfigureAwait(false);

            var buffer = new byte[64 * 1024];
            var sb = new StringBuilder();
            var count = 0;
            string? sample = null;

            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                try
                {
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);
                }
                catch (OperationCanceledException)
                {
                    break; // listen window elapsed
                }

                var raw = ExtractRawLine(sb.ToString());
                if (raw is not null)
                {
                    count++;
                    sample ??= raw;
                }
            }

            return new IinactProbeResult { Connected = true, LogLineCount = count, SampleRawLine = sample };
        }
        catch (Exception ex)
        {
            return new IinactProbeResult { Connected = false, Error = ex.Message };
        }
    }

    private static string? ExtractRawLine(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var t) && t.GetString() == "LogLine" &&
                root.TryGetProperty("rawLine", out var raw))
                return raw.GetString();
        }
        catch (JsonException) { }
        return null;
    }
}
