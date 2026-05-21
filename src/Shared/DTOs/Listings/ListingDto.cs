using System.Text.Json.Serialization;

namespace Stallions.Shared.DTOs.Listings;

// STJ (.NET 9) intentionally allows the TypeDiscriminatorPropertyName ("listingType") to match
// an existing property (ListingType below). STJ reuses that property as the discriminator — it
// does NOT throw. The values written by [JsonDerivedType] ("Auction", "FixedPrice") match the
// enum .ToString() values set in MapToDto, so serialization and deserialization round-trip correctly.
// See: ListingDtoSerializationTests for verification.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "listingType")]
[JsonDerivedType(typeof(AuctionListingDto), "Auction")]
[JsonDerivedType(typeof(FixedPriceListingDto), "FixedPrice")]
public class ListingDto
{
    public Guid Id { get; set; }
    public Guid StallionId { get; set; }
    public string StallionName { get; set; } = string.Empty;
    public Guid SeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public Guid StudFarmId { get; set; }
    public string StudFarmName { get; set; } = string.Empty;
    // This property doubles as the STJ polymorphic discriminator — see comment above the class.
    public string ListingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? PlatformFeePercent { get; set; }  // null when caller is not Staff
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
