using System.Runtime.CompilerServices;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;

namespace MogCoach.Ingest.Iinact;

/// <summary>
/// Reads a captured FFXIV network log (pipe-delimited, one line per event) and yields parsed
/// <see cref="CombatEvent"/>s. This is the post-session ingest path: point it at the .log written
/// by the capture recorder (or exported by ACT/IINACT). Player identity for self-attribution is
/// tracked from ChangePrimaryPlayer lines, with an optional override from the request.
/// </summary>
public sealed class IinactCaptureFileSource : ITelemetrySource
{
    private readonly string _path;
    private string? _playerId;
    private string? _playerName;

    public IinactCaptureFileSource(string path, string? playerNameOverride = null)
    {
        _path = path;
        _playerName = playerNameOverride;
    }

    public async IAsyncEnumerable<CombatEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            throw new FileNotFoundException($"Capture file not found: {_path}", _path);

        using var reader = new StreamReader(_path);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            ct.ThrowIfCancellationRequested();

            // Learn player identity from ChangePrimaryPlayer (type 02) as we go.
            var fields = line.Split('|');
            if (fields.Length >= 4 && fields[0] == "02")
            {
                _playerId = fields[2];
                _playerName ??= fields[3];
            }

            if (IinactLogLineParser.TryParse(line, _playerId, _playerName, out var ev))
                yield return ev;
        }
    }
}
