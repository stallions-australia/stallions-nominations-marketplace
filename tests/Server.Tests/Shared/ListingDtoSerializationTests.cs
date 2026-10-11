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
        result.ListingType.Should().Be("Auction");
    }

    [Fact]
    public void AuctionListingDto_RoundTrips_DescriptionAndTerms()
    {
        var dto = new AuctionListingDto
        {
            Id = Guid.NewGuid(),
            ListingType = "Auction",
            Description = "Premium service, live foal guarantee.",
            TermsAndConditions = "45-day payment required on live foal.",
            EndDateTime = DateTime.UtcNow.AddDays(3)
        };
        // Serialize via the base type so the polymorphic discriminator is written (same pattern as existing tests)
        var json = JsonSerializer.Serialize<ListingDto>(dto, new JsonSerializerOptions { WriteIndented = false });
        var back = JsonSerializer.Deserialize<ListingDto>(json);
        var fp = back.Should().BeOfType<AuctionListingDto>().Subject;
        fp.Description.Should().Be("Premium service, live foal guarantee.");
        fp.TermsAndConditions.Should().Be("45-day payment required on live foal.");
    }

    [Fact]
    // Server-side type mapping only; the real client read path (STJ 9 + StallionsJsonContext) is covered in Client.Tests ListingApiServiceTests.
    public void MakeAnOfferListingDto_RoundTrips_AsAListingDto()
    {
        var expires = new DateTime(2027, 1, 31, 12, 59, 59, DateTimeKind.Utc);
        var dto = new MakeAnOfferListingDto { Id = Guid.NewGuid(), ExpiresAt = expires, SourceAuctionListingId = Guid.NewGuid() };

        var json = JsonSerializer.Serialize<ListingDto>(dto);
        var back = JsonSerializer.Deserialize<ListingDto>(json);

        json.Should().Contain("\"MakeAnOffer\"");
        var mao = back.Should().BeOfType<MakeAnOfferListingDto>().Subject;
        mao.ExpiresAt.Should().Be(expires);
        mao.SourceAuctionListingId.Should().Be(dto.SourceAuctionListingId);
    }
}
