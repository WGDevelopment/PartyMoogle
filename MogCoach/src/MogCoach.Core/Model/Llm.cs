namespace MogCoach.Core.Model;

/// <summary>Role of a chat message.</summary>
public enum LlmRole { System, User, Assistant }

/// <summary>
/// A single piece of message content. Text parts carry <see cref="Text"/>; image parts carry
/// raw <see cref="ImageBytes"/> + <see cref="ImageMediaType"/> (the client base64-encodes them
/// into a data URI for the vision request).
/// </summary>
public sealed record LlmContentPart
{
    public string? Text { get; init; }
    public byte[]? ImageBytes { get; init; }
    public string? ImageMediaType { get; init; }

    public bool IsImage => ImageBytes is { Length: > 0 };

    public static LlmContentPart FromText(string text) => new() { Text = text };

    public static LlmContentPart FromImage(byte[] bytes, string mediaType = "image/png") =>
        new() { ImageBytes = bytes, ImageMediaType = mediaType };
}

public sealed record LlmMessage
{
    public required LlmRole Role { get; init; }
    public IReadOnlyList<LlmContentPart> Content { get; init; } = [];

    public static LlmMessage System(string text) =>
        new() { Role = LlmRole.System, Content = [LlmContentPart.FromText(text)] };

    public static LlmMessage User(params LlmContentPart[] parts) =>
        new() { Role = LlmRole.User, Content = parts };

    public static LlmMessage UserText(string text) =>
        new() { Role = LlmRole.User, Content = [LlmContentPart.FromText(text)] };
}

public sealed record LlmChatRequest
{
    public required IReadOnlyList<LlmMessage> Messages { get; init; }

    /// <summary>Override the configured model for this request; null uses the default.</summary>
    public string? Model { get; init; }

    public double Temperature { get; init; } = 0.2;
    public int? MaxTokens { get; init; }

    /// <summary>Ask the server for a JSON object response (OpenAI response_format).</summary>
    public bool JsonMode { get; init; }
}

public sealed record LlmChatResponse
{
    public required string Content { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public string? Model { get; init; }
}
