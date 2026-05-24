// tests/Client.Tests/Layout/AdminLayoutTests.cs
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Stallions.Client.Services;
using Stallions.Client.Layout;
using Stallions.Shared.DTOs.Enquiries;
using Xunit;

namespace Stallions.Client.Tests.Layout;

public class AdminLayoutTests : TestContext
{
    private sealed class StubAdminApiService : AdminApiService
    {
        public StubAdminApiService() : base(new HttpClient()) { }

        public override Task<List<EnquirySummaryDto>> GetMyEnquiriesAsync()
            => Task.FromResult(new List<EnquirySummaryDto>());
    }

    public AdminLayoutTests()
    {
        Services.AddScoped<AdminApiService, StubAdminApiService>();
    }

    [Fact]
    public void AdminLayout_Renders_SidebarNavLinks()
    {
        var cut = RenderComponent<AdminLayout>(parameters => parameters
            .Add(p => p.Body, builder => builder.AddMarkupContent(0, "<p>content</p>")));

        Assert.Contains("My Stallions", cut.Markup);
        Assert.Contains("My Listings", cut.Markup);
        Assert.Contains("Enquiries", cut.Markup);
    }
}
