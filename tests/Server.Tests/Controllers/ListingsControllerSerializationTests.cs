using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Stallions.Server.Controllers;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Server.Tests.Controllers;

/// <summary>
/// ListingDto uses its own ListingType property as the polymorphic discriminator ("listingType").
/// The server's System.Text.Json (10.x, pulled in by Microsoft.Identity.Web) refuses to serialize
/// anything declared as ListingDto under the API's camelCase options, because the property clashes
/// with the discriminator. So listing endpoints must serialize each listing as its concrete type —
/// which writes "listingType" as a plain property that the client reads as the discriminator.
/// These tests serialize controller results exactly as MVC does (Web options, runtime type).
/// </summary>
public class ListingsControllerSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string SerializeLikeMvc(IActionResult result)
    {
        var value = result.Should().BeAssignableTo<ObjectResult>().Subject.Value!;
        return JsonSerializer.Serialize(value, value.GetType(), Web);
    }

    private static AuctionListingDto Auction(string name) => new()
    {
        Id = Guid.NewGuid(), StallionName = name, ListingType = "Auction", Status = "Active",
        ReservePrice = 20000m, BuyerFeeIncGst = 150m, EndDateTime = DateTime.UtcNow.AddDays(3)
    };

    [Fact]
    public async Task GetMine_SerializesEachListingAsItsConcreteTypeWithTheTypeTag()
    {
        var service = new Mock<IListingService>();
        service.Setup(s => s.GetMineAsync()).ReturnsAsync(
            ServiceResult<IReadOnlyList<ListingDto>>.Ok(new List<ListingDto> { Auction("Maurice"), Auction("Snitzel") }));

        var json = SerializeLikeMvc(await new ListingsController(service.Object).GetMine());

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(2);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            // First property is the tag the client's [JsonPolymorphic] reader needs.
            element.EnumerateObject().First().Name.Should().Be("listingType");
            element.GetProperty("listingType").GetString().Should().Be("Auction");
            element.Deserialize<AuctionListingDto>(Web)!.ReservePrice.Should().Be(20000m);
        }
    }

    [Fact]
    public async Task GetMineById_SerializesAsConcreteTypeWithTheTypeTag()
    {
        var dto = Auction("Maurice");
        var service = new Mock<IListingService>();
        service.Setup(s => s.GetMineByIdAsync(dto.Id)).ReturnsAsync(ServiceResult<ListingDto>.Ok(dto));

        var json = SerializeLikeMvc(await new ListingsController(service.Object).GetMineById(dto.Id));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().First().Name.Should().Be("listingType");
        doc.RootElement.GetProperty("listingType").GetString().Should().Be("Auction");
    }
}
