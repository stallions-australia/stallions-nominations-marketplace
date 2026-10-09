using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Stallions.Server.Controllers;
using Stallions.Server.Payments;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Payments;
using Stallions.Shared.DTOs.Subscriptions;

namespace Stallions.Server.Tests.Controllers;

/// <summary>The return URLs tell the client pages what to wait for after the hosted payment page.</summary>
public class PaymentReturnUrlTests
{
    private static ControllerContext Context()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("marketplace.example");
        return new ControllerContext { HttpContext = http };
    }

    [Fact]
    public async Task CardSetup_SuccessUrlCarriesTheTimeTheSetupStarted()
    {
        string? success = null, cancel = null;
        var cards = new Mock<ICardService>();
        cards.Setup(c => c.StartSetupAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((s, c) => { success = s; cancel = c; })
            .ReturnsAsync(ServiceResult<PaymentRedirectDto>.Ok(new PaymentRedirectDto { Url = "https://pay" }));
        var controller = new PaymentsController(cards.Object, new Mock<IPaymentProvider>().Object,
            new Mock<IPaymentEventProcessor>().Object, NullLogger<PaymentsController>.Instance)
        { ControllerContext = Context() };

        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await controller.StartCardSetup();
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        success.Should().StartWith("https://marketplace.example/account/card?result=success&since=");
        long.Parse(success!.Split("since=")[1]).Should().BeInRange(before, after);
        cancel.Should().Be("https://marketplace.example/account/card?result=cancelled");
    }

    [Fact]
    public async Task Activate_ReturnUrlsNameTheStallion()
    {
        var stallionId = Guid.NewGuid();
        string? success = null, cancel = null;
        var subs = new Mock<ISubscriptionService>();
        subs.Setup(s => s.ActivateAsync(It.IsAny<ActivateStallionRequest>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<ActivateStallionRequest, string, string>((_, s, c) => { success = s; cancel = c; })
            .ReturnsAsync(ServiceResult<PaymentRedirectDto>.Ok(new PaymentRedirectDto { Url = "https://pay" }));
        var controller = new SubscriptionsController(subs.Object) { ControllerContext = Context() };

        await controller.Activate(new ActivateStallionRequest { StallionId = stallionId });

        success.Should().Be($"https://marketplace.example/admin/stallions?payment=success&stallion={stallionId}");
        cancel.Should().Be($"https://marketplace.example/admin/stallions?payment=cancelled&stallion={stallionId}");
    }
}
