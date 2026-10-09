using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Stallions.Server.Controllers;
using Stallions.Server.Payments;

namespace Stallions.Server.Tests.Controllers;

public class FakePaymentControllerTests
{
    private readonly FakePaymentProvider _fake = new();
    private readonly Mock<IPaymentEventProcessor> _processor = new();

    private FakePaymentController Controller(IPaymentProvider? provider = null) =>
        new(provider ?? _fake, _processor.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private async Task<string> NewFeeSession(string description = "Listing fee") =>
        (await _fake.CreateListingFeeSessionAsync(Guid.NewGuid(), 990m, description, null, "https://ok", "https://no"))
            .Split('/').Last();

    [Fact]
    public void Show_WhenProviderIsNotFake_IsNotFound() =>
        Controller(new Mock<IPaymentProvider>().Object).Show("abc").Should().BeOfType<NotFoundResult>();

    [Fact]
    public async Task Show_HtmlEncodesDescription_AndIsNotCached()
    {
        var id = await NewFeeSession("<script>alert(1)</script>");
        var controller = Controller();

        var result = controller.Show(id).Should().BeOfType<ContentResult>().Subject;

        result.Content.Should().Contain("&lt;script&gt;").And.NotContain("<script>alert");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Theory]
    [InlineData(PaymentEventOutcome.Processed, "https://ok")]
    [InlineData(PaymentEventOutcome.Duplicate, "https://ok")]
    [InlineData(PaymentEventOutcome.Rejected, "https://no")]
    [InlineData(PaymentEventOutcome.Ignored, "https://no")]
    [InlineData(PaymentEventOutcome.InProgress, "https://no")]
    public async Task Approve_RedirectsAccordingToOutcome(PaymentEventOutcome outcome, string expected)
    {
        var id = await NewFeeSession();
        _processor.Setup(p => p.ProcessAsync(It.IsAny<PaymentEvent>())).ReturnsAsync(outcome);

        var result = await Controller().Approve(id);

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(expected);
    }

    [Fact]
    public async Task Approve_WhenProcessingThrows_PropagatesAndSessionCanBeRetried()
    {
        var id = await NewFeeSession();
        _processor.SetupSequence(p => p.ProcessAsync(It.IsAny<PaymentEvent>()))
            .ThrowsAsync(new InvalidOperationException("db down"))
            .ReturnsAsync(PaymentEventOutcome.Processed);
        var controller = Controller();

        await FluentActions.Awaiting(() => controller.Approve(id)).Should().ThrowAsync<InvalidOperationException>();
        var retry = await controller.Approve(id);

        retry.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("https://ok");
    }
}
