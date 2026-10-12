using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Stallions.Server.Controllers;

namespace Stallions.Server.Tests.Controllers;

public class AuditControllerTests
{
    [Fact]
    public void TheChangeLog_IsStaffOnly() =>
        typeof(AuditController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("StaffOnly");
}
