// tests/Server.Tests/Controllers/PaymentsControllerWebhookTests.cs
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Stallions.Server.Controllers;
using Stallions.Server.Payments;
using Stallions.Server.Services;

namespace Stallions.Server.Tests.Controllers;

public class PaymentsControllerWebhookTests
{
    private readonly Mock<IPaymentProvider> _provider = new();
    private readonly Mock<IPaymentEventProcessor> _processor = new();

    private PaymentsController CreateSut(string body, string? signature)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (signature != null) context.Request.Headers["Stripe-Signature"] = signature;
        return new PaymentsController(new Mock<ICardService>().Object, _provider.Object, _processor.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    [Fact]
    public async Task ValidEvent_IsProcessedAndAcknowledged()
    {
        _provider.SetupGet(p => p.Name).Returns("Stripe");
        var evt = new UnhandledPaymentEvent("evt_1", "x");
        _provider.Setup(p => p.ParseWebhookAsync("{\"a\":1}", "sig")).ReturnsAsync(evt);

        var result = await CreateSut("{\"a\":1}", "sig").StripeWebhook();

        result.Should().BeOfType<OkResult>();
        _processor.Verify(p => p.ProcessAsync(evt), Times.Once);
    }

    [Fact]
    public async Task BadSignature_Returns400AndProcessesNothing()
    {
        _provider.SetupGet(p => p.Name).Returns("Stripe");
        _provider.Setup(p => p.ParseWebhookAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new PaymentSignatureException("bad"));

        var result = await CreateSut("{}", "bad").StripeWebhook();

        result.Should().BeOfType<BadRequestObjectResult>();
        _processor.Verify(p => p.ProcessAsync(It.IsAny<PaymentEvent>()), Times.Never);
    }

    [Fact]
    public async Task EventStillInProgress_Returns409SoStripeRetries()
    {
        _provider.SetupGet(p => p.Name).Returns("Stripe");
        var evt = new UnhandledPaymentEvent("evt_1", "x");
        _provider.Setup(p => p.ParseWebhookAsync(It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(evt);
        _processor.Setup(p => p.ProcessAsync(evt)).ReturnsAsync(PaymentEventOutcome.InProgress);

        var result = await CreateSut("{}", "sig").StripeWebhook();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task WhenProviderIsNotStripe_Returns404()
    {
        _provider.SetupGet(p => p.Name).Returns("Fake");

        (await CreateSut("{}", "sig").StripeWebhook()).Should().BeOfType<NotFoundResult>();
    }
}
