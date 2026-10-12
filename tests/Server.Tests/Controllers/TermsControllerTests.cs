using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Stallions.Server.Controllers;

namespace Stallions.Server.Tests.Controllers;

public class TermsControllerTests
{
    private static string? Policy(string method) =>
        typeof(TermsController).GetMethod(method)!.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

    [Fact]
    public void EachEndpoint_HasTheRightRole()
    {
        Policy(nameof(TermsController.Publish)).Should().Be("StaffOnly");
        Policy(nameof(TermsController.GetHistory)).Should().Be("StaffOnly");
        Policy(nameof(TermsController.GetStudStatus)).Should().Be("StudFarmOrStaff");
        Policy(nameof(TermsController.AcceptStud)).Should().Be("StudFarmAdminOnly");
        typeof(TermsController).GetMethod(nameof(TermsController.GetCurrent))!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }
}
