using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>Renders coaching reports to a target format (Markdown, HTML, ...).</summary>
public interface IReportRenderer
{
    /// <summary>File extension this renderer produces, without the dot (e.g. "md").</summary>
    string Extension { get; }

    /// <summary>Render a single pull's report.</summary>
    string Render(CoachingReport report);

    /// <summary>Render a whole session (multiple pulls) as one document.</summary>
    string RenderSession(IReadOnlyList<CoachingReport> reports);
}
