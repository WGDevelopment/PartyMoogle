using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MogCoach.Analysis.Prompts;
using MogCoach.Core.Abstractions;
using MogCoach.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace MogCoach.Analysis.Pipeline;

/// <summary>
/// The vision pass: for each keyframe, downscales the frame(s), asks the VLM to read the picture for
/// the visual cause of what telemetry already flagged, and turns the answer into positioning/mechanic
/// findings with a saved evidence image. Runs only on the bounded set of keyframes.
/// </summary>
public sealed class VisionAnalyzer(
    ILlmClient llm,
    IOptions<AnalysisOptions> analysisOptions,
    ILogger<VisionAnalyzer> log)
{
    private readonly AnalysisOptions _opts = analysisOptions.Value;
    private readonly int _maxEdge = analysisOptions.Value.MaxImageEdge;

    public async Task<IReadOnlyList<Finding>> AnalyzeAsync(
        Pull pull, IReadOnlyList<Keyframe> keyframes, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_opts.EvidenceDir);
        var findings = new List<Finding>();

        foreach (var kf in keyframes)
        {
            ct.ThrowIfCancellationRequested();
            if (kf.Frames.Count == 0) continue;

            var parts = new List<LlmContentPart> { LlmContentPart.FromText(BuildPrompt(pull, kf)) };
            string? evidencePath = null;

            foreach (var frame in kf.Frames)
            {
                if (frame.ImageBytes is not { Length: > 0 }) continue;
                var png = Downscale(frame.ImageBytes, _maxEdge);
                parts.Add(LlmContentPart.FromImage(png, "image/png"));
                evidencePath ??= SaveEvidence(pull.Id, kf, frame, png);
            }

            if (parts.Count == 1) continue; // no usable images

            var request = new LlmChatRequest
            {
                Messages =
                [
                    LlmMessage.System(CoachPrompts.VisionSystem),
                    LlmMessage.User([.. parts]),
                ],
                JsonMode = true,
                Temperature = 0.2,
            };

            try
            {
                var resp = await llm.CompleteAsync(request, ct).ConfigureAwait(false);
                var env = FindingsEnvelope.Parse(resp.Content);
                var mapped = env.ToFindings("vision", evidencePath);
                foreach (var f in mapped)
                    findings.Add(f with { At = kf.Timestamp, PullOffsetSeconds = (kf.Timestamp - pull.StartedAt).TotalSeconds });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Vision analysis failed for keyframe {Label}", kf.Label);
            }
        }

        return findings;
    }

    private static string BuildPrompt(Pull pull, Keyframe kf)
    {
        var offset = (kf.Timestamp - pull.StartedAt).TotalSeconds;
        var sb = new StringBuilder();
        sb.AppendLine($"Encounter: {pull.EncounterName ?? pull.ZoneName}");
        sb.AppendLine($"Moment: {kf.Label} at {offset:F0}s into the pull ({kf.Reason}).");
        if (kf.Trigger?.SourceName is { } src)
            sb.AppendLine($"Telemetry attributes this to: {src}.");
        if (kf.Frames.Any(f => !string.IsNullOrWhiteSpace(f.OcrText)))
        {
            sb.AppendLine("On-screen text (OCR) near this moment:");
            foreach (var f in kf.Frames.Where(f => !string.IsNullOrWhiteSpace(f.OcrText)))
                sb.AppendLine($"  \"{Trim(f.OcrText!, 240)}\"");
        }
        sb.AppendLine("The image(s) follow in chronological order. Read the picture for the visual cause.");
        return sb.ToString();
    }

    private string SaveEvidence(string pullId, Keyframe kf, Frame frame, byte[] png)
    {
        var name = $"{pullId}-{kf.Reason}-{frame.Id}.png".Replace(' ', '_');
        var path = Path.Combine(_opts.EvidenceDir, name);
        try { File.WriteAllBytes(path, png); }
        catch (Exception ex) { log.LogWarning(ex, "Failed saving evidence frame {Path}", path); }
        return path;
    }

    private static byte[] Downscale(byte[] input, int maxEdge)
    {
        using var image = Image.Load(input);
        if (Math.Max(image.Width, image.Height) > maxEdge)
        {
            var scale = (double)maxEdge / Math.Max(image.Width, image.Height);
            image.Mutate(x => x.Resize((int)(image.Width * scale), (int)(image.Height * scale)));
        }
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
