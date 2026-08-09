namespace MogCoach.Llm;

/// <summary>
/// Connection settings for the OpenAI-compatible endpoint (llama.cpp llama-server hosting
/// Qwen2.5-VL on VM108, reached over the tailnet).
/// </summary>
public sealed class OpenAiOptions
{
    public const string SectionName = "Llm";

    /// <summary>Base URL, e.g. "http://shared-services:11434". "/v1" is appended if absent.</summary>
    public string BaseUrl { get; set; } = "http://shared-services:11434";

    /// <summary>Model id as served, e.g. "qwen2.5-vl-7b-instruct".</summary>
    public string Model { get; set; } = "qwen2.5-vl-7b-instruct";

    /// <summary>Optional bearer token (GPU_LLM_KEY). Leave empty when the endpoint is open.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Per-request timeout. VLM keyframe calls can be slow; default matches Screenpipe's 900s.</summary>
    public int TimeoutSeconds { get; set; } = 900;

    /// <summary>Default sampling temperature for analysis calls.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Max completion tokens per call.</summary>
    public int MaxTokens { get; set; } = 1536;
}
