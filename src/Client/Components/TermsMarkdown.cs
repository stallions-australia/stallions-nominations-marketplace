using System.Net;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Stallions.Client.Components;

/// <summary>
/// Terms markdown → HTML with raw HTML disabled, so terms text can't carry markup or script. Links, autolinks and
/// images are kept only when their URL is http, https, mailto or relative (no scheme, or starting with "/" or "#");
/// any other scheme (javascript:, data:, vbscript: …, in any case or with whitespace / encoded characters) is dropped:
/// a link keeps its text, an image keeps its alt text, an autolink becomes plain text.
/// </summary>
public static class TermsMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    public static string ToHtml(string? markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (IsSafeUrl(link.Url)) continue;
            var child = link.FirstChild;
            while (child is not null)
            {
                var next = child.NextSibling;
                child.Remove();
                link.InsertBefore(child);
                child = next;
            }
            link.Remove();
        }

        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (autolink.IsEmail || IsSafeUrl(autolink.Url)) continue;
            autolink.ReplaceBy(new LiteralInline(autolink.Url));
        }

        return document.ToHtml(Pipeline);
    }

    /// <summary>
    /// True for http(s), mailto and relative URLs. Entities are decoded and whitespace / control characters removed
    /// first (browsers ignore them inside a scheme), so "java&amp;#x09;script:" is still seen as javascript:.
    /// </summary>
    private static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        var decoded = WebUtility.HtmlDecode(url);
        var normalised = new string(decoded.Where(c => c > ' ' && !char.IsControl(c) && !char.IsWhiteSpace(c)).ToArray());
        if (normalised.StartsWith('/') || normalised.StartsWith('#')) return true;

        var colon = normalised.IndexOf(':');
        if (colon < 0) return true;
        var firstDelimiter = normalised.IndexOfAny(['/', '?', '#']);
        if (firstDelimiter >= 0 && firstDelimiter < colon) return true; // the colon is in the path / query, not a scheme

        var scheme = normalised[..colon];
        return AllowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);
    }
}
