using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Listings;

namespace Stallions.Client.Tests.Services;

public class ListingApiServiceTests
{
    private static HttpClient FakeClient(HttpStatusCode status, object body)
    {
        var handler = new FakeHttpMessageHandler(status, body);
        return new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
    }

    [Fact]
    public async Task GetListingsAsync_ReturnsCards_OnSuccess()
    {
        var cards = new List<ListingCardDto>
        {
            new() { Id = Guid.NewGuid(), StallionName = "Fastnet Rock", ListingType = "Auction" }
        };
        var sut = new ListingApiService(FakeClient(HttpStatusCode.OK, cards));

        var result = await sut.GetListingsAsync();

        result.Should().HaveCount(1);
        result[0].StallionName.Should().Be("Fastnet Rock");
    }

    [Fact]
    public async Task GetListingsAsync_Throws_On500()
    {
        var sut = new ListingApiService(FakeClient(HttpStatusCode.InternalServerError, "error"));

        await Assert.ThrowsAsync<ApiException>(() => sut.GetListingsAsync());
    }

    // Body built like ASP.NET Core's Ok(value): the runtime type, camelCase, with its own listingType property.
    private static HttpClient RawJsonClient(object dto)
    {
        var json = JsonSerializer.Serialize<object>(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        // STJ 9 on this side writes the discriminator AND the property (a duplicate); the server emits it once
        // (see ListingDtoClientDeserializationTests.ServerJson), so drop the extra one.
        var dup = System.Text.RegularExpressions.Regex.Match(json, "\"listingType\":\"[A-Za-z]+\",");
        json = json.Remove(dup.Index, dup.Length);
        return new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("https://localhost/") };
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task GetByIdAsync_ReadsAServerMakeAnOfferListing()
    {
        var expires = new DateTime(2027, 1, 31, 12, 59, 59, DateTimeKind.Utc);
        var dto = new MakeAnOfferListingDto
        {
            Id = Guid.NewGuid(), StallionName = "Snitzel", Status = "Active", BuyerFeeIncGst = 150m,
            ExpiresAt = expires, SourceAuctionListingId = Guid.NewGuid()
        };

        var result = await new ListingApiService(RawJsonClient(dto)).GetByIdAsync(dto.Id);

        var mao = result.Should().BeOfType<MakeAnOfferListingDto>().Subject;
        mao.ExpiresAt.Should().Be(expires);
        mao.SourceAuctionListingId.Should().Be(dto.SourceAuctionListingId);
        mao.BuyerFeeIncGst.Should().Be(150m);
        mao.StallionName.Should().Be("Snitzel");
    }

    [Fact]
    public async Task GetByIdAsync_ReadsAServerAuctionListing()
    {
        var dto = new AuctionListingDto
        {
            Id = Guid.NewGuid(), ListingType = "Auction", StallionName = "Snitzel", Status = "Unsold",
            EndDateTime = new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc), MakeAnOfferListingId = Guid.NewGuid()
        };

        var result = await new ListingApiService(RawJsonClient(dto)).GetByIdAsync(dto.Id);

        var auction = result.Should().BeOfType<AuctionListingDto>().Subject;
        auction.EndDateTime.Should().Be(dto.EndDateTime);
        auction.MakeAnOfferListingId.Should().Be(dto.MakeAnOfferListingId);
    }
}
