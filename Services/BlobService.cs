using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;

namespace app_dev_assignment.Services;

public sealed class BlobService : IBlobService
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private readonly string _connectionString;
    private readonly string _containerName;

    public BlobService(IConfiguration configuration)
    {
        _connectionString = configuration["BlobStorage:ConnectionString"] ?? string.Empty;
        _containerName = configuration["BlobStorage:ContainerName"] ?? "images";
    }

    public async Task<string> UploadFileAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        await ValidateImageAsync(file, cancellationToken);

        var containerClient = new BlobContainerClient(_connectionString, _containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

        var (extension, contentType) = GetImageFormat(file.ContentType);
        var blobName = $"{Guid.NewGuid():N}{extension}";
        var blobClient = containerClient.GetBlobClient(blobName);

        var blobHttpHeaders = new BlobHttpHeaders { ContentType = contentType };
        await using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, blobHttpHeaders, cancellationToken: cancellationToken);

        return blobClient.Uri.ToString();
    }

    private static async Task ValidateImageAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxFileSize)
        {
            throw new InvalidDataException("Images must be between 1 byte and 10 MB.");
        }

        var (extension, _) = GetImageFormat(file.ContentType);
        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);

        var validSignature = extension switch
        {
            ".jpg" => bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => bytesRead >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" => bytesRead >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };

        if (!validSignature)
        {
            throw new InvalidDataException("The uploaded file is not a valid JPEG, PNG, or WebP image.");
        }
    }

    private static (string Extension, string ContentType) GetImageFormat(string? contentType)
    {
        return contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => (".jpg", "image/jpeg"),
            "image/png" => (".png", "image/png"),
            "image/webp" => (".webp", "image/webp"),
            _ => throw new InvalidDataException("Only JPEG, PNG, and WebP images are supported.")
        };
    }
}