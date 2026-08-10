using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;

namespace app_dev_assignment.Services;

public sealed class BlobService : IBlobService
{
    private readonly string _connectionString;
    private readonly string _containerName;

    public BlobService(IConfiguration configuration)
    {
        _connectionString = configuration["BlobStorage:ConnectionString"] ?? string.Empty;
        _containerName = configuration["BlobStorage:ContainerName"] ?? "images";
    }

    public async Task<string> UploadFileAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var containerClient = new BlobContainerClient(_connectionString, _containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

        var extension = Path.GetExtension(file.FileName);
        var blobName = $"{Guid.NewGuid()}{extension}";
        var blobClient = containerClient.GetBlobClient(blobName);

        var blobHttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType };

        await using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, blobHttpHeaders, cancellationToken: cancellationToken);

        return blobClient.Uri.ToString();
    }
}
