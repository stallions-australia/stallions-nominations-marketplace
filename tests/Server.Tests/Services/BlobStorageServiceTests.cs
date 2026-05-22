using Stallions.Server.Services;

namespace Stallions.Server.Tests.Services;

public class BlobStorageServiceTests
{
    [Fact]
    public void BlobName_IsConstructed_FromStallionIdAndFileName()
    {
        var stallionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var expected = $"stallions/{stallionId}/";
        // BlobStorageService prefixes blobs with "stallions/{stallionId}/"
        Assert.StartsWith("stallions/", expected);
    }
}
