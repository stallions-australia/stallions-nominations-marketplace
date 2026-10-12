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

    [Theory]
    [InlineData("[Click here](javascript:alert(1))")]
    [InlineData("[Click here](JaVaScRiPt:alert(1))")]
    [InlineData("[Click here]( javascript:alert(1))")]
    [InlineData("[Click here](java&#x09;script:alert(1))")]
    [InlineData("[Click here](java&#x0A;script:alert(1))")]
    [InlineData("[Click here](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
    [InlineData("[Click here](vbscript:msgbox(1))")]
    [InlineData("[Click here][evil]\n\n[evil]: javascript:alert(1)")]
    public void TermsMarkdown_UnsafeLink_IsDroppedButItsTextKept(string markdown)
    {
        var html = TermsMarkdown.ToHtml(markdown);

        html.Should().Contain("Click here").And.NotContain("<a").And.NotContainEquivalentOf("script:")
            .And.NotContain("data:").And.NotContain("href");
    }

    [Theory]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<vbscript:msgbox(1)>")]
    public void TermsMarkdown_UnsafeAutolink_IsNotALink(string markdown) =>
        TermsMarkdown.ToHtml(markdown).Should().NotContain("<a").And.NotContain("href");

    [Fact]
    public void TermsMarkdown_UnsafeImage_IsDroppedButItsAltTextKept()
    {
        var html = TermsMarkdown.ToHtml("![Logo](javascript:alert(1)) and ![Pic](data:image/svg+xml;base64,AAAA)");

        html.Should().NotContain("<img").And.NotContain("src=").And.Contain("Logo").And.Contain("Pic");
    }

    [Theory]
    [InlineData("[Site](https://stallions.example/terms)", "href=\"https://stallions.example/terms\"")]
    [InlineData("[Site](HTTP://stallions.example)", "href=\"HTTP://stallions.example\"")]
    [InlineData("[Email us](mailto:help@stallions.example)", "href=\"mailto:help@stallions.example\"")]
    [InlineData("[Privacy](/privacy)", "href=\"/privacy\"")]
    [InlineData("[Fees](#fees)", "href=\"#fees\"")]
    [InlineData("[Fees](fees.html)", "href=\"fees.html\"")]
    [InlineData("<https://stallions.example>", "href=\"https://stallions.example\"")]
    [InlineData("<help@stallions.example>", "href=\"mailto:help@stallions.example\"")]
    public void TermsMarkdown_SafeLink_IsKept(string markdown, string expectedHref) =>
        TermsMarkdown.ToHtml(markdown).Should().Contain("<a " + expectedHref);
}
