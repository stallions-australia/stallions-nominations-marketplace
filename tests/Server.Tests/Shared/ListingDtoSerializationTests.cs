using System.Text.Json;
using FluentAssertions;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Server.Tests.Shared;

// Note: the task spec uses Assert.IsType / Assert.Equal (xUnit style) — kept consistent
// with the existing file's FluentAssertions style instead.

/// <summary>
/// Verifies that the JsonPolymorphic discriminator property name ("listingType") matching
/// the existing ListingDto.ListingType property does not cause STJ to throw, and that
/// round-trip serialization preserves the correct derived type.
/// </summary>
public class ListingDtoSerializationTests
{
    [Fact]
    public void ListingDto_AuctionListingDto_RoundTripsWithoutException()
    {
        var dto = new AuctionListingDto
        {
            Id = Guid.NewGuid(),
            StallionId = Guid.NewGuid(),
            StallionName = "Snitzel",
            SeasonId = Guid.NewGuid(),
            SeasonName = "2025 Season",
            StudFarmId = Guid.NewGuid(),
            StudFarmName = "Arrowfield Stud",
            ListingType = "Auction",   // matches the JsonDerivedType discriminator value
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            StartingPrice = 5000m,
            IsNoReserve = false,
            MinimumBidIncrement = 25m,
            EndDateTime = DateTime.UtcNow.AddDays(3)
        };

        // Serialize via the base type to exercise the polymorphic discriminator
        string json = JsonSerializer.Serialize<ListingDto>(dto);
        json.Should().Contain("\"listingType\"");
        json.Should().Contain("\"Auction\"");

        // Deserialize back — should return an AuctionListingDto
        var deserialized = JsonSerializer.Deserialize<ListingDto>(json);
        deserialized.Should().BeOfType<AuctionListingDto>();
        var result = (AuctionListingDto)deserialized!;
        result.StallionName.Should().Be("Snitzel");
        result.StartingPrice.Should().Be(5000m);
        result.ListingType.Should().Be("Auction");
    }

    [Fact]
    public void ListingDto_FixedPriceListingDto_RoundTripsWithoutException()
    {
        var dto = new FixedPriceListingDto
        {
            Id = Guid.NewGuid(),
            StallionId = Guid.NewGuid(),
            StallionName = "Fastnet Rock",
            SeasonId = Guid.NewGuid(),
            SeasonName = "2025 Season",
            StudFarmId = Guid.NewGuid(),
            StudFarmName = "Coolmore Australia",
            ListingType = "FixedPrice",   // matches the JsonDerivedType discriminator value
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            PriceIncGst = 8000m,
            Quantity = 5,
            QuantityRemaining = 4
        };

        string json = JsonSerializer.Serialize<ListingDto>(dto);
        json.Should().Contain("\"listingType\"");
        json.Should().Contain("\"FixedPrice\"");

        var deserialized = JsonSerializer.Deserialize<ListingDto>(json);
        deserialized.Should().BeOfType<FixedPriceListingDto>();
        var result = (FixedPriceListingDto)deserialized!;
        result.StallionName.Should().Be("Fastnet Rock");
        result.PriceIncGst.Should().Be(8000m);
        result.ListingType.Should().Be("FixedPrice");
    }

    [Fact]
    public void FixedPriceListingDto_RoundTrips_DescriptionAndTerms()
    {
        var dto = new FixedPriceListingDto
        {
            Id = Guid.NewGuid(),
            ListingType = "FixedPrice",
            Description = "Premium service, live foal guarantee.",
            TermsAndConditions = "45-day payment required on live foal.",
            PriceIncGst = 10000m,
            Quantity = 20,
            QuantityRemaining = 20
        };
        // Serialize via the base type so the polymorphic discriminator is written (same pattern as existing tests)
        var json = JsonSerializer.Serialize<ListingDto>(dto, new JsonSerializerOptions { WriteIndented = false });
        var back = JsonSerializer.Deserialize<ListingDto>(json);
        var fp = back.Should().BeOfType<FixedPriceListingDto>().Subject;
        fp.Description.Should().Be("Premium service, live foal guarantee.");
        fp.TermsAndConditions.Should().Be("45-day payment required on live foal.");
    }
}
