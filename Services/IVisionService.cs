namespace app_dev_assignment.Services;

public sealed class VisionAnalysisResult
{
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public bool IsCached { get; set; }
}

public interface IVisionService
{
    Task<VisionAnalysisResult> AnalyzeImageAsync(string imageUrl, byte[] imageBytes, CancellationToken cancellationToken = default);
}
