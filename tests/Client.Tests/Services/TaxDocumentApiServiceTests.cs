using System.Net;
using FluentAssertions;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Client.Tests.Services;

public class TaxDocumentApiServiceTests
{
    [Fact]
    public void BuildQuery_IncludesOnlyTheFiltersSet()
    {
        TaxDocumentApiService.BuildQuery(new TaxDocumentFilter()).Should().BeEmpty();
        TaxDocumentApiService.BuildQuery(new TaxDocumentFilter
        {
            Kind = "BuyerFeeInvoice", Status = "Paid", From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31)
        }).Should().Be("?kind=BuyerFeeInvoice&status=Paid&from=2026-10-01&to=2026-10-31");
    }

    [Fact]
    public async Task Get_WhenTheApiSays404_ReturnsNull()
    {
        var http = new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.NotFound, "Tax document not found."))
            { BaseAddress = new Uri("https://localhost/") };

        (await new TaxDocumentApiService(http).GetAsync(Guid.NewGuid())).Should().BeNull();
    }
}
