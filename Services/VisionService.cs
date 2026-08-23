using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace app_dev_assignment.Services;

public sealed class VisionService : IVisionService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly string _endpoint;
    private readonly string _subscriptionKey;

    public VisionService(HttpClient httpClient, IMemoryCache cache, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _cache = cache;
        _endpoint = configuration["AzureVision:Endpoint"]?.TrimEnd('/') ?? string.Empty;
        _subscriptionKey = configuration["AzureVision:Key"]?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_endpoint) || _endpoint.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AzureVision:Endpoint must be configured with a real endpoint.", nameof(configuration));
        if (string.IsNullOrWhiteSpace(_subscriptionKey) || _subscriptionKey.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AzureVision:Key must be configured with a real key.", nameof(configuration));

        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<VisionAnalysisResult> AnalyzeImageAsync(string imageUrl, byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(imageBytes);
        if (_cache.TryGetValue(cacheKey, out VisionAnalysisResult? cachedResult) && cachedResult is not null)
        {
            return new VisionAnalysisResult { Description = cachedResult.Description, Tags = cachedResult.Tags, IsCached = true };
        }

        var requestUri = new Uri($"{_endpoint}/vision/v3.2/analyze?visualFeatures=Description,Tags,Objects,Categories,Color,ImageType,Brands,Faces&details=Celebrities,Landmarks&language=en");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new { url = imageUrl }), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var result = new VisionAnalysisResult
        {
            Description = BuildScholarlyDescription(document),
            Tags = ParseTags(document),
            IsCached = false
        };
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) });
        return result;
    }

    private static string BuildScholarlyDescription(JsonDocument document)
    {
        var paragraphs = new List<string>();
        var caption = ParseCaption(document);
        var objects = ParseNames(document, "objects", 0.55);
        var categories = ParseNames(document, "categories", 0.45);
        var tags = ParseTags(document);
        var colors = ParseColorSummary(document);

        if (!string.IsNullOrWhiteSpace(caption))
            paragraphs.Add($"At a high level, the image presents {caption.TrimEnd('.')}.");

        if (objects.Count > 0)
            paragraphs.Add($"The most salient visible subjects are {JoinNatural(objects)}. These detections describe recognizable visual entities rather than asserting their purpose, identity, or activity beyond what the image evidence supports.");

        if (categories.Count > 0)
            paragraphs.Add($"In terms of setting and visual classification, the scene is consistent with {JoinNatural(categories)}.");

        if (!string.IsNullOrWhiteSpace(colors))
            paragraphs.Add(colors);

        if (tags.Count > 0)
            paragraphs.Add($"Additional visual concepts include {JoinNatural(tags.Take(8).ToList())}. Taken together, these signals suggest the image should be read as a composition organized around its principal subjects, surrounding context, and color relationships, while avoiding claims that cannot be directly established from pixels alone.");

        return paragraphs.Count > 0
            ? string.Join(" ", paragraphs)
            : "The analysis service did not return sufficient visual evidence to produce a reliable detailed description.";
    }

    private static string ParseCaption(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("description", out var description) && description.TryGetProperty("captions", out var captions) && captions.ValueKind == JsonValueKind.Array && captions.GetArrayLength() > 0 && captions[0].TryGetProperty("text", out var text))
            return text.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static IReadOnlyList<string> ParseTags(JsonDocument document) => ParseNames(document, "tags", 0.55);

    private static List<string> ParseNames(JsonDocument document, string propertyName, double minimumConfidence)
    {
        var names = new List<string>();
        if (!document.RootElement.TryGetProperty(propertyName, out var elements) || elements.ValueKind != JsonValueKind.Array) return names;
        foreach (var element in elements.EnumerateArray())
        {
            var confidence = element.TryGetProperty("confidence", out var confidenceElement) ? confidenceElement.GetDouble() : 1.0;
            if (confidence < minimumConfidence || !element.TryGetProperty("name", out var name)) continue;
            var value = name.GetString();
            if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value, StringComparer.OrdinalIgnoreCase)) names.Add(value);
        }
        return names;
    }

    private static string ParseColorSummary(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("color", out var color)) return string.Empty;
        var values = new[]
        {
            color.TryGetProperty("dominantColorForeground", out var foreground) ? foreground.GetString() : null,
            color.TryGetProperty("dominantColorBackground", out var background) ? background.GetString() : null
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return values.Count == 0 ? string.Empty : $"Chromatically, the dominant visual fields are {JoinNatural(values)}, which provides a useful account of the image's overall tonal balance.";
    }

    private static string JoinNatural(IReadOnlyList<string> values)
    {
        if (values.Count == 1) return values[0];
        if (values.Count == 2) return $"{values[0]} and {values[1]}";
        return $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}";
    }

    private static string GetCacheKey(byte[] imageBytes)
    {
        using var sha256 = SHA256.Create();
        return "vision-analysis-" + Convert.ToHexString(sha256.ComputeHash(imageBytes));
    }
}