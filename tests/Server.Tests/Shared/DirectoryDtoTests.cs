using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Tests.Shared;

public class DirectoryDtoTests
{
    [Fact]
    public void StudDirectorySummaryDto_DefaultsAreCorrect()
    {
        var dto = new StudDirectorySummaryDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.True(dto.IsActive);
        Assert.Equal(0, dto.StallionCount);
    }

    [Fact]
    public void AuthorizedStallionsDto_DefaultIsNotLinked()
    {
        var dto = new AuthorizedStallionsDto();
        Assert.False(dto.IsLinked);
        Assert.Empty(dto.Available);
    }
}
