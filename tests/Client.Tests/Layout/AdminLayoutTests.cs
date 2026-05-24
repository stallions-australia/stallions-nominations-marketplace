using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Stallions.Client.Layout;
using Stallions.Client.Services;

namespace Stallions.Client.Tests.Layout;

public class AdminLayoutTests : TestContext
{
    public AdminLayoutTests()
    {
        Services.AddHttpClient<AdminApiService>(c =>
            c.BaseAddress = new Uri("https://localhost/"));
    }

    [Fact]
    public void AdminLayout_Renders_SidebarNavLinks()
    {
        var cut = RenderComponent<AdminLayout>(parameters => parameters
            .Add(p => p.Body, (Microsoft.AspNetCore.Components.RenderFragment)(builder =>
            {
                builder.AddMarkupContent(0, "<p>content</p>");
            })));

        Assert.Contains("My Stallions", cut.Markup);
        Assert.Contains("My Listings", cut.Markup);
        Assert.Contains("Enquiries", cut.Markup);
    }
}
