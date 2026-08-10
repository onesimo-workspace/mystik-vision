using Microsoft.AspNetCore.Http;

namespace app_dev_assignment.Models;

public sealed class ImageUploadViewModel
{
    public IFormFile? ImageFile { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
    public bool IsCached { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    public string CacheStatus => IsCached ? "Cached" : "Live";
}
