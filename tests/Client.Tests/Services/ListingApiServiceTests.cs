using System.Net;
using System.Net.Http.Json;
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
            new() { Id = Guid.NewGuid(), StallionName = "Fastnet Rock", ListingType = "FixedPrice" }
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
}

// Minimal fake HTTP handler — reused across service tests
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly object _body;

    public FakeHttpMessageHandler(HttpStatusCode status, object body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(_status)
        {
            Content = JsonContent.Create(_body)
        };
        return Task.FromResult(response);
    }
}
