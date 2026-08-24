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

        var tags = ParseTags(document);
        var result = new VisionAnalysisResult
        {
            Description = BuildScholarlyDescription(document, tags),
            Tags = tags,
            IsCached = false
        };
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) });
        return result;
    }

    private static string BuildScholarlyDescription(JsonDocument document, IReadOnlyList<string> tags)
    {
        var sections = new List<string>();
        var caption = ParseCaption(document);
        var objects = ParseNames(document, "objects", 0.55);
        var categories = ParseNames(document, "categories", 0.45);
        var colors = ParseColorSummary(document);

        sections.Add("OVERVIEW");
        sections.Add(!string.IsNullOrWhiteSpace(caption)
            ? $"{caption.TrimEnd('.')}.")
            : "The service did not return a reliable global caption.");

        sections.Add("
OBSERVED SUBJECTS");
        sections.Add(objects.Count > 0
            ? $"The image contains {JoinNatural(objects)}. These are model detections of visible entities; they do not establish ownership, identity, purpose, or activity."
            : "No object detections met the configured confidence threshold.");

        sections.Add("
ENVIRONMENT");
        sections.Add(categories.Count > 0
            ? $"The visual classification is consistent with {JoinNatural(categories)}. This describes the apparent scene category, not a verified location."
            : "The available evidence is insufficient to classify the environment with confidence.");

        sections.Add("
COMPOSITION");
        sections.Add(objects.Count > 0
            ? "The detected subjects form the principal visual structure of the frame, with the surrounding setting providing contextual information. Precise foreground, midground, and background relationships require region-level evidence and are not asserted here."
            : "A reliable compositional account could not be derived from the available detections.");

        if (!string.IsNullOrWhiteSpace(colors))
        {
            sections.Add("
COLOUR");
            sections.Add(colors);
        }

        sections.Add("
INTERPRETATION");
        sections.Add(objects.Count > 0
            ? $"Taken together, the evidence suggests an image organized around {JoinNatural(objects.Take(5).ToList())}. The exact purpose, ownership, organization, and circumstances represented cannot be established from the image alone."
            : "The image cannot be interpreted beyond the limited evidence returned by the vision provider.");

        sections.Add("
UNCERTAINTY");
        sections.Add("Model confidence is evidence quality, not factual certainty. Names, identities, locations, intentions, and relationships are treated as unknown unless independently supported by explicit visual evidence.");

        if (tags.Count > 0)
        {
            sections.Add("
ADDITIONAL EVIDENCE");
            sections.Add($"Detected concepts above threshold: {JoinNatural(tags.Take(8).ToList())}.");
        }

        return string.Join(" ", sections);
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
            var confidence = element.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.ValueKind == JsonValueKind.Number
                ? confidenceElement.GetDouble()
                : 1.0;
            if (confidence < minimumConfidence) continue;

            // Azure Vision v3.2 uses "object" for object detections and "name" for tags/categories.
            var value = GetStringProperty(element, "name") ?? GetStringProperty(element, "object");
            if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value, StringComparer.OrdinalIgnoreCase)) names.Add(value);
        }
        return names;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string ParseColorSummary(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("color", out var color)) return string.Empty;
        var values = new[]
        {
            color.TryGetProperty("dominantColorForeground", out var foreground) ? foreground.GetString() : null,
            color.TryGetProperty("dominantColorBackground", out var background) ? background.GetString() : null
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return values.Count == 0 ? string.Empty : $"The dominant visual fields are {JoinNatural(values)}, creating the principal tonal contrast recorded by the provider.";
    }

    private static string JoinNatural(IReadOnlyList<string> values)
    {
        if (values.Count == 0) return "no clearly identified subjects";
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