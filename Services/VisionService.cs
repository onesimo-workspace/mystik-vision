using System.Diagnostics;
using System.Security.Cryptography;
using app_dev_assignment.Models;
using Microsoft.Extensions.Caching.Memory;

namespace app_dev_assignment.Services;

public sealed class VisionService : IVisionService
{
    private readonly IVisionAnalysisProvider _provider;
    private readonly IMemoryCache _cache;
    public VisionService(IVisionAnalysisProvider provider, IMemoryCache cache) { _provider = provider; _cache = cache; }
    public async Task<VisionAnalysisResult> AnalyzeImageAsync(string imageUrl, byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        var key = GetCacheKey(imageBytes);
        if (_cache.TryGetValue(key, out VisionAnalysisResult? cached) && cached is not null) return new VisionAnalysisResult { Description = cached.Description, Tags = cached.Tags, Analysis = cached.Analysis, IsCached = true };
        var timer = Stopwatch.StartNew();
        var evidence = await _provider.AnalyzeAsync(imageUrl, cancellationToken);
        var analysis = BuildAnalysis(evidence, imageBytes, timer.Elapsed);
        var result = new VisionAnalysisResult { Description = BuildDescription(evidence), Tags = evidence.Tags.Select(x => x.Name).ToArray(), Analysis = analysis };
        _cache.Set(key, result, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) });
        return result;
    }
    private static ImageAnalysisResult BuildAnalysis(VisionProviderResult evidence, byte[] bytes, TimeSpan duration)
    {
        var subjects = evidence.Objects.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new ImageAnalysisResult { Metadata = new AnalysisMetadata { AnalysisId = Guid.NewGuid(), ImageHash = GetCacheKey(bytes).Replace("vision-analysis-", String.Empty, StringComparison.Ordinal), CreatedAt = DateTimeOffset.UtcNow, Provider = evidence.Provider, ProviderApiVersion = evidence.ApiVersion, ProcessingDuration = duration }, Overview = evidence.Caption is null ? "No reliable global caption was returned by the provider." : evidence.Caption.TrimEnd('.') + ".", Objects = evidence.Objects, Tags = evidence.Tags, Scene = new SceneAnalysis { Categories = evidence.Categories, Environment = evidence.Categories.Count > 0 ? JoinNatural(evidence.Categories) : "Unavailable" }, Colors = evidence.Colors, Interpretations = subjects.Count > 0 ? new[] { new Interpretation { Statement = $"The frame appears visually organized around {JoinNatural(subjects)}.", EvidenceBasis = subjects, Confidence = 0.60 } } : Array.Empty<Interpretation>(), Uncertainties = new[] { new Uncertainty { Statement = "Identity, location, ownership, purpose, and activity are not established by these detections.", Reason = "The provider returns visual evidence rather than verified contextual provenance." } }, Limitations = new[] { "OCR is unavailable in the current analyze request.", "Dense captions are unavailable in the current Azure Vision v3.2 integration.", "Provider confidence is evidence quality, not factual certainty." } };
    }
    private static string BuildDescription(VisionProviderResult evidence) => string.Join(Environment.NewLine + Environment.NewLine, new[] { "OVERVIEW" + Environment.NewLine + (evidence.Caption ?? "No reliable global caption was returned by the provider."), "OBSERVED SUBJECTS" + Environment.NewLine + (evidence.Objects.Count > 0 ? $"The image contains {JoinNatural(evidence.Objects.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList())}. These are model detections of visible entities." : "No object detections met the configured confidence threshold."), "ENVIRONMENT" + Environment.NewLine + (evidence.Categories.Count > 0 ? $"The visual classification is consistent with {JoinNatural(evidence.Categories)}." : "The available evidence is insufficient to classify the environment with confidence."), "LIMITATIONS" + Environment.NewLine + "OCR and dense captions are not available through the current provider integration." });
    private static string JoinNatural(IReadOnlyList<string> values) => values.Count switch { 0 => "", 1 => values[0], 2 => $"{values[0]} and {values[1]}", _ => string.Join(", ", values.Take(values.Count - 1)) + ", and " + values[^1] };
    private static string GetCacheKey(byte[] bytes) { using var sha = SHA256.Create(); return "vision-analysis-" + Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant(); }
}
