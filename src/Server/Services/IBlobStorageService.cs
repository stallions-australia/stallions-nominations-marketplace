namespace Stallions.Server.Services;

public interface IBlobStorageService
{
    /// <summary>Uploads a stream to the stallion-images container and returns the blob URL.</summary>
    Task<string> UploadStallionImageAsync(Guid stallionId, string fileName, Stream content, string contentType);

    /// <summary>Deletes a blob by its full URL. No-ops if the blob does not exist.</summary>
    Task DeleteAsync(string blobUrl);
}
