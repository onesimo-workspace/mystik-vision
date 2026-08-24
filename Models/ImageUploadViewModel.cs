using Microsoft.AspNetCore.Http;

using app_dev_assignment.Models;

namespace app_dev_assignment.Models;

public sealed class ImageUploadViewModel
{
    public IFormFile? ImageFile { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
    public bool IsCached { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    public ImageAnalysisResult? Analysis { get; set; }
    public string CacheStatus => IsCached ? "Cached" : "Live";
}
