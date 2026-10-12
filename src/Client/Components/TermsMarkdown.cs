using Markdig;

namespace Stallions.Client.Components;

/// <summary>Terms markdown → HTML with raw HTML disabled, so terms text can't carry markup or script.</summary>
public static class TermsMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    public static string ToHtml(string? markdown) => Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
}
