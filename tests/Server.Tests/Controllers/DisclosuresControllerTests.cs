// tests/Server.Tests/Controllers/DisclosuresControllerTests.cs
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Content;
using Stallions.Server.Controllers;
using Stallions.Server.Data;
using Stallions.Server.Tests.Helpers;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Tests.Controllers;

public class DisclosuresControllerTests : IDisposable
{
    private readonly AppDbContext _db = DbContextFactory.Create(Guid.NewGuid().ToString());

    public DisclosuresControllerTests() => _db.Database.EnsureCreated();

    public void Dispose() => _db.Dispose();

    private async Task<BuyerFeeDisclosureDto?> GetAsync() =>
        (await new DisclosuresController(ContentTestData.Service(_db)).GetBuyerFee() as OkObjectResult)?.Value as BuyerFeeDisclosureDto;

    [Fact]
    public async Task GetBuyerFee_ReturnsTheStoredWording()
    {
        ContentTestData.SetWording(_db, ContentKeys.BuyerFeeExplanation, null, "CONFIGURED fee");
        ContentTestData.SetWording(_db, ContentKeys.StudFarmBalanceArrangement, null, "CONFIGURED balance");
        ContentTestData.SetWording(_db, ContentKeys.SavedCardExplanation, null, "CONFIGURED card");

        var dto = await GetAsync();

        dto.Should().NotBeNull();
        dto!.BuyerFeeExplanation.Should().Be("CONFIGURED fee");
        dto.BalanceArrangement.Should().Be("CONFIGURED balance");
        dto.SavedCardExplanation.Should().Be("CONFIGURED card");
    }

    [Fact]
    public async Task GetBuyerFee_BeforeAnyEdit_ReturnsTheSeededDefaults()
    {
        var dto = await GetAsync();

        dto!.BuyerFeeExplanation.Should().StartWith("The buyer fee is paid to Stallions Australia and forms part of the price");
        dto.BalanceArrangement.Should().StartWith("The stud farm will contact you separately");
        dto.SavedCardExplanation.Should().StartWith("Your saved card is charged the buyer fee automatically");
    }

    [Fact]
    public void GetBuyerFee_IsPublic() =>
        typeof(DisclosuresController).GetMethod(nameof(DisclosuresController.GetBuyerFee))!
            .GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
}
