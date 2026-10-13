// tests/Server.Tests/Content/ContentTemplateTests.cs
using FluentAssertions;
using Stallions.Server.Content;

namespace Stallions.Server.Tests.Content;

public class ContentTemplateTests
{
    private static readonly ContentCatalogEntry Email = new("Email.Test", ContentGroups.BuyerEmails, "Test", "A test email",
        HasSubject: true, SingleParagraph: false,
        [new ContentPlaceholder("StallionName", "The stallion", "Snitzel"), new ContentPlaceholder("Amount", "The amount", "$10,000.00")],
        ["Amount"], "Sold: {StallionName}", "You paid {Amount}.");

    private static readonly ContentCatalogEntry Disclosure = new("Disclosure.Test", ContentGroups.Disclosures, "Test", "A test disclosure",
        HasSubject: false, SingleParagraph: true, [], [], null, "Fee text.");

    private static readonly IReadOnlyDictionary<string, string?> Values = new Dictionary<string, string?>
    {
        ["StallionName"] = "Snitzel", ["Amount"] = "$10,000.00"
    };

    // ── Validation ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_GoodText_HasNoErrors() =>
        ContentTemplate.Validate(Email, "Sold: {StallionName}", "You won {StallionName}.\n\nYou paid {Amount}.").Should().BeEmpty();

    [Fact]
    public void Validate_UnknownPlaceholder_NamesIt() =>
        ContentTemplate.Validate(Email, "Sold", "You paid {Amount} for {Stalion}.")
            .Should().Equal("{Stalion} isn't available in this email.");

    [Fact]
    public void Validate_UnknownPlaceholderInTheSubject_IsRejectedToo() =>
        ContentTemplate.Validate(Email, "Sold: {Horse}", "You paid {Amount}.")
            .Should().Equal("{Horse} isn't available in this email.");

    [Fact]
    public void Validate_PlaceholderNamesAreCaseSensitive() =>
        ContentTemplate.Validate(Email, "Sold", "You paid {amount}.")
            .Should().BeEquivalentTo("{amount} isn't available in this email.", "The message must include {Amount}.");

    [Fact]
    public void Validate_MissingRequiredPlaceholder() =>
        ContentTemplate.Validate(Email, "Sold", "You won.").Should().Equal("The message must include {Amount}.");

    [Fact]
    public void Validate_ARequiredPlaceholderOnlyInTheSubject_DoesNotCount() =>
        ContentTemplate.Validate(Email, "You paid {Amount}", "You won.").Should().Equal("The message must include {Amount}.");

    [Theory]
    [InlineData("You paid {Amount")]
    [InlineData("You paid Amount}")]
    [InlineData("You paid {{Amount}}")]
    [InlineData("You paid {Amo{unt}")]
    public void Validate_UnbalancedBraces(string body) =>
        ContentTemplate.Validate(Email, "Sold", body).Should().ContainSingle(e => e.StartsWith("Check the braces"));

    [Fact]
    public void Validate_EmptySubjectAndBody() =>
        ContentTemplate.Validate(Email, "  ", " \r\n ").Should().BeEquivalentTo(
            "The subject can't be empty.", "The message can't be empty.", "The message must include {Amount}.");

    [Fact]
    public void Validate_TooLong() =>
        ContentTemplate.Validate(Email, new string('s', 201), "{Amount}" + new string('b', 4000)).Should().BeEquivalentTo(
            "The subject can be at most 200 characters.", "The message can be at most 4,000 characters.");

    [Fact]
    public void Validate_SubjectOnTwoLines_IsRejected() =>
        ContentTemplate.Validate(Email, "Sold\nBcc: someone@evil.example", "You paid {Amount}.")
            .Should().Equal("The subject must be on one line.");

    [Fact]
    public void Validate_DisclosureWithAPlaceholder() =>
        ContentTemplate.Validate(Disclosure, null, "The fee is {Amount}.")
            .Should().Equal("{Amount} isn't available here — this wording has no placeholders.");

    [Fact]
    public void Validate_DisclosureWithALineBreak() =>
        ContentTemplate.Validate(Disclosure, null, "One.\n\nTwo.")
            .Should().Equal("This wording is a single paragraph — remove the line breaks.");

    [Fact]
    public void Validate_DisclosureIgnoresASubject() =>
        ContentTemplate.Validate(Disclosure, "anything {Foo}\nat all", "Fee text.").Should().BeEmpty();

    // ── Rendering ───────────────────────────────────────────────────────────

    [Fact]
    public void RenderBody_SubstitutesValues_AndSplitsParagraphsOnBlankLines()
    {
        var missing = new HashSet<string>();

        var paragraphs = ContentTemplate.RenderBody("You won {StallionName}.\n\nYou paid {Amount}.", Values, missing);

        paragraphs.Should().Equal(
            new RenderedParagraph("You won Snitzel.", "You won Snitzel."),
            new RenderedParagraph("You paid $10,000.00.", "You paid $10,000.00."));
        missing.Should().BeEmpty();
    }

    [Fact]
    public void RenderBody_ALineBreakInsideAParagraph_IsABrInHtml_AndStaysInText() =>
        ContentTemplate.RenderBody("Line one\nLine two", Values, new HashSet<string>())
            .Should().Equal(new RenderedParagraph("Line one<br>Line two", "Line one\nLine two"));

    [Fact]
    public void RenderBody_NormalisesWindowsLineEndings_AndExtraBlankLines() =>
        ContentTemplate.RenderBody("One\r\n\r\n\r\n  \r\nTwo\r\n", Values, new HashSet<string>())
            .Select(p => p.Text).Should().Equal("One", "Two");

    [Fact]
    public void RenderBody_EncodesStaffTextAndValues()
    {
        var values = new Dictionary<string, string?> { ["StallionName"] = "<b>Snitzel</b>" };

        var paragraph = ContentTemplate.RenderBody("<script>alert(1)</script> {StallionName} & co", values, new HashSet<string>()).Single();

        paragraph.Html.Should().Be("&lt;script&gt;alert(1)&lt;/script&gt; &lt;b&gt;Snitzel&lt;/b&gt; &amp; co");
        paragraph.Text.Should().Be("<script>alert(1)</script> <b>Snitzel</b> & co");
    }

    [Fact]
    public void Render_APlaceholderWithNoValue_IsEmpty_AndReported()
    {
        var missing = new HashSet<string>();

        ContentTemplate.RenderLine("{BuyerFee} paid", Values, missing).Should().Be(" paid");
        missing.Should().Equal("BuyerFee");
    }

    [Fact]
    public void Render_ANullValue_IsEmpty_AndNotReported()
    {
        var missing = new HashSet<string>();

        ContentTemplate.RenderLine("Declined: {Reason}", new Dictionary<string, string?> { ["Reason"] = null }, missing)
            .Should().Be("Declined: ");
        missing.Should().BeEmpty();
    }

    [Fact]
    public void RenderLine_RendersASubject_WithoutEncoding() =>
        ContentTemplate.RenderLine("You've been outbid on {StallionName}", Values, new HashSet<string>())
            .Should().Be("You've been outbid on Snitzel");
}
