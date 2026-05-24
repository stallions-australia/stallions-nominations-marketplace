using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Stallions;
using Stallions.Shared.Enums;

namespace Stallions.Server.Tests.Services;

public class StallionServiceTests
{
    private readonly Mock<IStallionRepository> _stallionRepoMock = new();
    private readonly Mock<IStudFarmRepository> _farmRepoMock = new();
    private readonly Mock<IUserService> _usersMock = new();
    private readonly Mock<IBlobStorageService> _blobsMock = new();
    private readonly Guid _studFarmId = Guid.NewGuid();
    private StallionService CreateSut() => new(_stallionRepoMock.Object, _farmRepoMock.Object, _usersMock.Object, _blobsMock.Object);

    [Fact]
    public async Task UpdateStallion_WhenCallerDoesNotOwnStallion_ReturnsForbidden()
    {
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var callerFarm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id };
        var otherFarmId = Guid.NewGuid();
        var stallion = new Stallion { Id = Guid.NewGuid(), StudFarmId = otherFarmId, IsActive = true };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(callerFarm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var result = await CreateSut().UpdateAsync(stallion.Id, new UpdateStallionRequest { Name = "Test" });

        result.Succeeded.Should().BeFalse();
        result.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task UpdateStallion_WhenCallerOwnsStallion_UpdatesName()
    {
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id };
        var stallion = new Stallion { Id = Guid.NewGuid(), StudFarmId = farm.Id, Name = "Old", IsActive = true,
            Images = new List<StallionImage>() };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var result = await CreateSut().UpdateAsync(stallion.Id, new UpdateStallionRequest { Name = "New Name" });

        result.Succeeded.Should().BeTrue();
        result.Value!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateAsync_AllowsReactivation_OfInactiveStallion()
    {
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Name = "Test", IsActive = false,
            Images = new List<StallionImage>(), Listings = new List<Listing>()
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var result = await CreateSut().UpdateAsync(stallion.Id, new UpdateStallionRequest { Name = "Test", IsActive = true });

        result.Succeeded.Should().BeTrue();
        result.Value!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_AllowsDeactivation_OfActiveStallion()
    {
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Name = "Test", IsActive = true,
            Images = new List<StallionImage>(), Listings = new List<Listing>()
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var result = await CreateSut().UpdateAsync(stallion.Id, new UpdateStallionRequest { Name = "Test", IsActive = false });

        result.Succeeded.Should().BeTrue();
        result.Value!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_WhenIsActiveIsNull_DoesNotChangeIsActive()
    {
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, Name = "Test", IsActive = true,
            Images = new List<StallionImage>(), Listings = new List<Listing>()
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var result = await CreateSut().UpdateAsync(stallion.Id, new UpdateStallionRequest { Name = "Test", IsActive = null });

        result.Succeeded.Should().BeTrue();
        result.Value!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SetPrimaryImage_WhenImageBelongsToStallion_ClearsPreviousPrimaryAndSetsNew()
    {
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = Guid.NewGuid(), UserId = caller.Id };
        var oldPrimary = new StallionImage { Id = Guid.NewGuid(), IsPrimary = true, BlobPath = "a.jpg" };
        var newPrimary = new StallionImage { Id = Guid.NewGuid(), IsPrimary = false, BlobPath = "b.jpg" };
        var stallion = new Stallion
        {
            Id = Guid.NewGuid(), StudFarmId = farm.Id, IsActive = true,
            Images = new List<StallionImage> { oldPrimary, newPrimary }
        };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var result = await CreateSut().SetPrimaryImageAsync(stallion.Id, newPrimary.Id);

        result.Succeeded.Should().BeTrue();
        oldPrimary.IsPrimary.Should().BeFalse();
        newPrimary.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task UploadImageAsync_ReturnsError_WhenStallionNotFound()
    {
        // Arrange
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = _studFarmId, UserId = caller.Id };
        var unknownId = Guid.NewGuid();
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(unknownId)).ReturnsAsync((Stallion?)null);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.ContentType).Returns("image/jpeg");
        fileMock.Setup(f => f.Length).Returns(1024);
        fileMock.Setup(f => f.FileName).Returns("test.jpg");

        // Act
        var result = await CreateSut().UploadImageAsync(unknownId, fileMock.Object);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(404, result.HttpStatusCode);
    }

    [Fact]
    public async Task UploadImageAsync_ReturnsError_WhenContentTypeNotAllowed()
    {
        // Arrange
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = _studFarmId, UserId = caller.Id };
        var stallion = new Stallion { Id = Guid.NewGuid(), StudFarmId = _studFarmId, Name = "Test", IsActive = true };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.ContentType).Returns("application/pdf");
        fileMock.Setup(f => f.Length).Returns(1024);
        fileMock.Setup(f => f.FileName).Returns("doc.pdf");

        // Act
        var result = await CreateSut().UploadImageAsync(stallion.Id, fileMock.Object);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.HttpStatusCode);
    }

    [Fact]
    public async Task UploadImageAsync_ReturnsError_WhenFileTooLarge()
    {
        // Arrange
        var caller = new User { Id = Guid.NewGuid(), Role = UserRole.StudFarmAdmin, Status = UserStatus.Active };
        var farm = new StudFarm { Id = _studFarmId, UserId = caller.Id };
        var stallion = new Stallion { Id = Guid.NewGuid(), StudFarmId = _studFarmId, Name = "Test", IsActive = true };
        _usersMock.Setup(u => u.GetOrCreateCurrentUserAsync()).ReturnsAsync(caller);
        _farmRepoMock.Setup(r => r.GetByUserIdAsync(caller.Id)).ReturnsAsync(farm);
        _stallionRepoMock.Setup(r => r.GetByIdAsync(stallion.Id)).ReturnsAsync(stallion);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.ContentType).Returns("image/jpeg");
        fileMock.Setup(f => f.Length).Returns(11 * 1024 * 1024); // 11 MB
        fileMock.Setup(f => f.FileName).Returns("big.jpg");

        // Act
        var result = await CreateSut().UploadImageAsync(stallion.Id, fileMock.Object);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.HttpStatusCode);
    }
}
