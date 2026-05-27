using System.Text.Json.Serialization;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Shared;

/// <summary>
/// Source-generated STJ context for trim-safe JSON serialisation in Blazor WASM.
///
/// Without this, <see cref="System.Text.Json.JsonSerializer"/> falls back to reflection
/// to discover polymorphic derived types (<see cref="AuctionListingDto"/>,
/// <see cref="FixedPriceListingDto"/>). IL linking strips those reflection members in a
/// Release Blazor WASM build, causing JsonException at runtime.
///
/// Including the polymorphic base type <see cref="ListingDto"/> here causes the source
/// generator to emit the discriminator-aware serializer/deserializer at compile time —
/// no reflection needed at runtime.
/// </summary>
[JsonSerializable(typeof(ListingDto))]
[JsonSerializable(typeof(AuctionListingDto))]
[JsonSerializable(typeof(FixedPriceListingDto))]
[JsonSerializable(typeof(List<ListingCardDto>))]
[JsonSerializable(typeof(ListingCardDto))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class StallionsJsonContext : JsonSerializerContext
{
}
