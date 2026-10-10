using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Controllers;
using Stallions.Server.Options;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Tests.Controllers;

public class DisclosuresControllerTests
{
    [Fact]
    public void GetBuyerFee_ReturnsTheConfiguredWording()
    {
        var controller = new DisclosuresController(Microsoft.Extensions.Options.Options.Create(new DisclosureOptions
        {
            BuyerFeeExplanation = "CONFIGURED fee",
            StudFarmBalanceArrangement = "CONFIGURED balance",
            SavedCardExplanation = "CONFIGURED card"
        }));

        var dto = (controller.GetBuyerFee() as OkObjectResult)?.Value as BuyerFeeDisclosureDto;

        dto.Should().NotBeNull();
        dto!.BuyerFeeExplanation.Should().Be("CONFIGURED fee");
        dto.BalanceArrangement.Should().Be("CONFIGURED balance");
        dto.SavedCardExplanation.Should().Be("CONFIGURED card");
    }
}
