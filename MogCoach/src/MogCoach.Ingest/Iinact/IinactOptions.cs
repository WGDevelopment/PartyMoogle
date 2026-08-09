namespace MogCoach.Ingest.Iinact;

/// <summary>Settings for the live IINACT capture recorder.</summary>
public sealed class IinactOptions
{
    public const string SectionName = "Iinact";

    /// <summary>
    /// OverlayPlugin-compatible WebSocket URL exposed by IINACT. Confirm the port in IINACT's
    /// settings; the legacy WSServer default is ws://127.0.0.1:10501/ws.
    /// </summary>
    public string WebSocketUrl { get; set; } = "ws://127.0.0.1:10501/ws";
}
