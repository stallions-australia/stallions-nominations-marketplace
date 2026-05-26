using Stallions.Shared.DTOs.Admin;

namespace Stallions.Server.Tests.Shared;

public class AdminDtoTests
{
    [Fact]
    public void StudFarmSummaryDto_DefaultsAreCorrect()
    {
        var dto = new StudFarmSummaryDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.LinkedUserDisplayName);
        Assert.Equal(string.Empty, dto.LinkedUserEmail);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void CreateStudFarmRequest_DefaultsAreCorrect()
    {
        var req = new CreateStudFarmRequest();
        Assert.Equal(Guid.Empty, req.UserId);
        Assert.Equal(string.Empty, req.Name);
    }

    [Fact]
    public void ForceListingStatusRequest_DefaultsAreCorrect()
    {
        var req = new ForceListingStatusRequest();
        Assert.Equal(string.Empty, req.Status);
        Assert.Null(req.Reason);
    }

    [Fact]
    public void ListingStaffSummaryDto_DefaultsAreCorrect()
    {
        var dto = new ListingStaffSummaryDto();
        Assert.Equal(string.Empty, dto.StallionName);
        Assert.Equal(string.Empty, dto.StudFarmName);
    }
}
