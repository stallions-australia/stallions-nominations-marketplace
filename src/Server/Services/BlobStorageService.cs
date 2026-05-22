using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Stallions.Server.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _serviceClient;
    private const string ContainerName = "stallion-images";

    public BlobStorageService(IConfiguration config)
    {
        // Uses DefaultAzureCredential: in production this is the App Service managed identity.
        // In local dev, run `az login` and ensure the developer has the
        // "Storage Blob Data Contributor" role on the storage account.
        var accountName = config["AZURE_STORAGE_ACCOUNT_NAME"]
            ?? throw new InvalidOperationException("AZURE_STORAGE_ACCOUNT_NAME is not configured.");

        var serviceUri = new Uri($"https://{accountName}.blob.core.windows.net");
        _serviceClient = new BlobServiceClient(serviceUri, new DefaultAzureCredential());
    }

    public async Task<string> UploadStallionImageAsync(
        Guid stallionId, string fileName, Stream content, string contentType)
    {
        var container = _serviceClient.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blobName = $"stallions/{stallionId}/{Guid.NewGuid()}-{Path.GetFileName(fileName)}";
        var blobClient = container.GetBlobClient(blobName);

        await blobClient.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType });
        return blobClient.Uri.ToString();
    }

    public async Task DeleteAsync(string blobUrl)
    {
        if (string.IsNullOrWhiteSpace(blobUrl)) return;
        var uri = new Uri(blobUrl);
        // Path starts with /container-name/blob-name
        var parts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
        if (parts.Length < 2) return;

        var container = _serviceClient.GetBlobContainerClient(parts[0]);
        await container.GetBlobClient(parts[1]).DeleteIfExistsAsync();
    }
}
