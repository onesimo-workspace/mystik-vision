using app_dev_assignment.Models;

namespace app_dev_assignment.Services;

public sealed class VisionAnalysisResult { public string Description { get; init; } = string.Empty; public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>(); public bool IsCached { get; init; } public ImageAnalysisResult Analysis { get; init; } = new(); }

public interface IVisionService { Task<VisionAnalysisResult> AnalyzeImageAsync(string imageUrl, byte[] imageBytes, CancellationToken cancellationToken = default); }
