namespace MogCoach.Analysis;

/// <summary>Tuning for the analysis pipeline — bounds how much the (expensive) vision pass runs.</summary>
public sealed class AnalysisOptions
{
    public const string SectionName = "Analysis";

    /// <summary>Hard cap on keyframes per pull handed to the VLM.</summary>
    public int MaxKeyframes { get; set; } = 6;

    /// <summary>Frames shown to the VLM per keyframe (the moment plus a little lead-up).</summary>
    public int FramesPerKeyframe { get; set; } = 2;

    /// <summary>Lead-up window before a moment when gathering frames.</summary>
    public double KeyframeWindowBeforeSeconds { get; set; } = 3;

    /// <summary>Trailing window after a moment when gathering frames.</summary>
    public double KeyframeWindowAfterSeconds { get; set; } = 1;

    /// <summary>Where evidence frames referenced by findings are saved.</summary>
    public string EvidenceDir { get; set; } = "reports/evidence";

    /// <summary>Longest image edge (px) before downscaling frames for the VLM (bounds VRAM/latency).</summary>
    public int MaxImageEdge { get; set; } = 1280;
}
