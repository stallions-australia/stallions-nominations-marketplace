// src/Server/Content/ContentTemplate.cs
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Stallions.Server.Content;

/// <summary>One rendered paragraph: Html is the encoded inner HTML (line breaks as &lt;br&gt;, no &lt;p&gt;); Text is plain.</summary>
public sealed record RenderedParagraph(string Html, string Text);

/// <summary>
/// Staff wording is plain text with {Placeholder} names (case-sensitive). A blank line starts a new paragraph; a single
/// line break becomes &lt;br&gt;. Substituted text — Staff's and the values — is HTML-encoded, never trusted as markup.
/// </summary>
public static partial class ContentTemplate
{
    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();

    public static string Normalise(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>The placeholder names in the text, in order, or null when its braces don't pair up.</summary>
    public static IReadOnlyList<string>? PlaceholderNames(string text)
    {
        var names = new List<string>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '}') return null;
            if (text[i] != '{') continue;
            var end = text.IndexOf('}', i + 1);
            if (end < 0) return null;
            var name = text[(i + 1)..end];
            if (name.Contains('{')) return null;
            names.Add(name);
            i = end;
        }
        return names;
    }

    /// <summary>Every problem with this wording for this item; empty when it can be saved.</summary>
    public static IReadOnlyList<string> Validate(ContentCatalogEntry entry, string? subject, string? body)
    {
        var errors = new List<string>();
        var text = Normalise(body).Trim();
        var subjectText = entry.HasSubject ? (subject ?? string.Empty).Trim() : string.Empty;

        if (entry.HasSubject)
        {
            if (subjectText.Length == 0)
                errors.Add("The subject can't be empty.");
            else if (subjectText.Length > ContentCatalog.MaxSubjectLength)
                errors.Add($"The subject can be at most {ContentCatalog.MaxSubjectLength} characters.");
            if (subjectText.Contains('\n') || subjectText.Contains('\r'))
                errors.Add("The subject must be on one line.");
        }

        if (text.Length == 0)
            errors.Add("The message can't be empty.");
        else if (text.Length > ContentCatalog.MaxBodyLength)
            errors.Add($"The message can be at most {ContentCatalog.MaxBodyLength.ToString("N0", CultureInfo.InvariantCulture)} characters.");
        if (entry.SingleParagraph && text.Contains('\n'))
            errors.Add("This wording is a single paragraph — remove the line breaks.");

        var subjectNames = PlaceholderNames(subjectText);
        var bodyNames = PlaceholderNames(text);
        if (subjectNames == null || bodyNames == null)
        {
            errors.Add("Check the braces: each { must be closed by a } around a placeholder name, e.g. {StallionName}.");
            return errors;
        }

        foreach (var name in subjectNames.Concat(bodyNames).Distinct(StringComparer.Ordinal))
            if (!entry.Allows(name))
                errors.Add(entry.Placeholders.Count == 0
                    ? $"{{{name}}} isn't available here — this wording has no placeholders."
                    : $"{{{name}}} isn't available in this email.");

        foreach (var required in entry.Required)
            if (!bodyNames.Contains(required))
                errors.Add($"The message must include {{{required}}}.");

        return errors;
    }

    /// <summary>
    /// Substitutes the placeholders in one line (not encoded — used for subjects, and per line by RenderBody).
    /// A placeholder with no entry in values is rendered empty and added to missing; a null value is just empty.
    /// </summary>
    public static string RenderLine(string template, IReadOnlyDictionary<string, string?> values, ISet<string> missing)
    {
        var sb = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf('{', i);
            var close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (open < 0 || close < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }
            sb.Append(template, i, open - i);
            var name = template[(open + 1)..close];
            if (values.TryGetValue(name, out var value)) sb.Append(value);
            else missing.Add(name);
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>Splits on blank lines into paragraphs, substitutes each line, and encodes the HTML form.</summary>
    public static IReadOnlyList<RenderedParagraph> RenderBody(string template, IReadOnlyDictionary<string, string?> values,
        ISet<string> missing)
    {
        var paragraphs = new List<RenderedParagraph>();
        foreach (var raw in BlankLines().Split(Normalise(template)))
        {
            var paragraph = raw.Trim();
            if (paragraph.Length == 0) continue;
            var lines = paragraph.Split('\n').Select(line => RenderLine(line, values, missing)).ToList();
            paragraphs.Add(new RenderedParagraph(
                string.Join("<br>", lines.Select(WebUtility.HtmlEncode)),
                string.Join("\n", lines)));
        }
        return paragraphs;
    }
}
