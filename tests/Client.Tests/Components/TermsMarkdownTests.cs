using FluentAssertions;
using Stallions.Client.Components;

namespace Stallions.Client.Tests.Components;

public class TermsMarkdownTests
{
    [Fact]
    public void TermsMarkdown_RendersRawHtmlAsText()
    {
        var html = TermsMarkdown.ToHtml("<script>alert(1)</script>\n\n**Bold** <img src=x onerror=alert(1)>");

        html.Should().NotContain("<script").And.NotContain("<img")
            .And.Contain("&lt;script&gt;").And.Contain("<strong>Bold</strong>");
    }

    [Fact]
    public void TermsMarkdown_OfNothing_IsEmpty() => TermsMarkdown.ToHtml(null).Should().BeEmpty();
}
