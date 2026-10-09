using System.Text.Json;
using FluentAssertions;
using Stallions.Shared;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Services;

/// <summary>
/// The server writes "listingType" as a plain property; the client's [JsonPolymorphic] reader
/// consumes it as the type discriminator. The ListingType property must still be populated
/// afterwards, because pages display it (My Listings "Type" column, listing badges).
/// Runs on the client's System.Text.Json (9.x), the same as the browser.
/// </summary>
public class ListingDtoClientDeserializationTests
{
    private const string ServerJson =
        "{\"listingType\":\"Auction\",\"id\":\"2f1c1d6e-0000-4000-8000-000000000001\",\"stallionName\":\"Maurice (JPN)\"," +
        "\"seasonName\":\"2026 Season\",\"status\":\"Active\",\"endDateTime\":\"2026-10-16T05:32:13\"}";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SingleListing_ReflectionWebOptions_KeepsListingType()
    {
        var dto = JsonSerializer.Deserialize<ListingDto>(ServerJson, Web)!;

        dto.Should().BeOfType<AuctionListingDto>();
        dto.ListingType.Should().Be("Auction");
        dto.SeasonName.Should().Be("2026 Season");
    }

    [Fact]
    public void ListingList_ReflectionWebOptions_KeepsListingType()
    {
        var list = JsonSerializer.Deserialize<List<ListingDto>>("[" + ServerJson + "]", Web)!;

        list.Should().ContainSingle().Which.ListingType.Should().Be("Auction");
    }

    [Fact]
    public void SingleListing_SourceGeneratedContext_KeepsListingType()
    {
        var dto = JsonSerializer.Deserialize(ServerJson, StallionsJsonContext.Default.ListingDto)!;

        dto.Should().BeOfType<AuctionListingDto>();
        dto.ListingType.Should().Be("Auction");
    }
}
