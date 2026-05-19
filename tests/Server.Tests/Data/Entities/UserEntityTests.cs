using FluentAssertions;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Data.Entities;

public class UserEntityTests
{
    [Fact]
    public void User_DefaultStatus_IsPendingVerification()
    {
        var user = new User();
        user.Status.Should().Be(UserStatus.PendingVerification);
    }

    [Fact]
    public void User_DefaultId_IsNotEmpty()
    {
        var user = new User();
        user.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void StudFarm_DefaultIsActive_IsTrue()
    {
        var farm = new StudFarm();
        farm.IsActive.Should().BeTrue();
    }
}
