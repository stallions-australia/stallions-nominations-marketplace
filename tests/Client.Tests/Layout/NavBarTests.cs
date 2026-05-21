using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Stallions.Client.Layout;

namespace Stallions.Client.Tests.Layout;

public class NavBarTests : TestContext
{
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
}
