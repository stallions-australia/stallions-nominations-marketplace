using Moq;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.Users;

namespace Stallions.Client.Tests.Services;

public class UserStateServiceTests
{
    private readonly Mock<UserApiService> _api;
    private readonly UserStateService _svc;

    public UserStateServiceTests()
    {
        var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        _api = new Mock<UserApiService>(http);
        _svc = new UserStateService(_api.Object);
    }

    [Fact]
    public async Task LoadAsync_FetchesOnce_IdempotentOnSecondCall()
    {
        var dto = new UserDto { Id = Guid.NewGuid(), Role = "Staff" };
        _api.Setup(a => a.GetMeAsync()).ReturnsAsync(dto);

        await _svc.LoadAsync();
        await _svc.LoadAsync(); // second call should not hit API

        _api.Verify(a => a.GetMeAsync(), Times.Once);
    }

    [Fact]
    public async Task Clear_ResetsCurrentUser()
    {
        var dto = new UserDto { Id = Guid.NewGuid(), Role = "Buyer" };
        _api.Setup(a => a.GetMeAsync()).ReturnsAsync(dto);
        await _svc.LoadAsync();
        Assert.True(_svc.IsLoaded);

        _svc.Clear();

        Assert.Null(_svc.CurrentUser);
        Assert.False(_svc.IsLoaded);
    }

    [Fact]
    public async Task RoleProperties_ReflectLoadedUser()
    {
        _api.Setup(a => a.GetMeAsync()).ReturnsAsync(new UserDto { Role = "Staff" });
        await _svc.LoadAsync();
        Assert.True(_svc.IsStaff);
        Assert.False(_svc.IsStudFarmAdmin);
        Assert.False(_svc.IsBuyer);
    }

    [Fact]
    public async Task IsBuyer_True_WhenRoleIsBuyer()
    {
        _api.Setup(a => a.GetMeAsync()).ReturnsAsync(new UserDto { Role = "Buyer" });
        await _svc.LoadAsync();
        Assert.True(_svc.IsBuyer);
        Assert.False(_svc.IsStaff);
    }
}
