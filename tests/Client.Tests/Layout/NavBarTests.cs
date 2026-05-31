using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stallions.Client.Layout;
using Stallions.Client.Services;

namespace Stallions.Client.Tests.Layout;

public class NavBarTests : TestContext
{
    public NavBarTests()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        Services.AddScoped(_ => new UserApiService(httpClient));
        Services.AddScoped<UserStateService>();
    }

    [Fact]
    public void NavBar_Unauthenticated_ShowsSignInLink()
    {
        this.AddTestAuthorization();  // anonymous by default

        var cut = RenderComponent<NavBar>();

        cut.Find("a[href='authentication/login']").Should().NotBeNull();
    }

    [Fact]
    public void NavBar_Authenticated_ShowsMyBidsLink()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("test@example.com");

        var cut = RenderComponent<NavBar>();

        cut.Find("a[href='/my-bids']").Should().NotBeNull();
    }

    [Fact]
    public void NavBar_HamburgerClick_OpensDrawer()
    {
        this.AddTestAuthorization();
        var cut = RenderComponent<NavBar>();

        cut.Find("button.navbar-hamburger").Click();

        cut.Find(".navbar-drawer").Should().NotBeNull();
    }

    [Fact]
    public void NavBar_BackdropClick_ClosesDrawer()
    {
        this.AddTestAuthorization();
        var cut = RenderComponent<NavBar>();
        cut.Find("button.navbar-hamburger").Click();  // open drawer

        cut.Find(".navbar-backdrop").Click();          // click backdrop

        cut.FindAll(".navbar-drawer").Should().BeEmpty();
    }
}
