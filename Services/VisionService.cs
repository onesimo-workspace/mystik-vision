using System.Diagnostics;
using System.Security.Cryptography;
using app_dev_assignment.Models;
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
        var started = Stopwatch.StartNew();
        var cacheKey = GetCacheKey(imageBytes);
        if (_cache.TryGetValue(cacheKey, out VisionAnalysisResult? cachedResult) && cachedResult is not null)
        {
            return new VisionAnalysisResult { Description = cachedResult.Description, Tags = cachedResult.Tags, Analysis = cachedResult.Analysis, IsCached = true };
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
        var objects = ParseObjects(document);
        var structured = BuildStructuredAnalysis(document, imageBytes, objects, tags, started.Elapsed);
        var result = new VisionAnalysisResult
        {
            Description = BuildScholarlyDescription(document, tags),
            Tags = tags,
            Analysis = structured,
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
        var separator = Environment.NewLine + Environment.NewLine;

        sections.Add("OVERVIEW" + Environment.NewLine + (!string.IsNullOrWhiteSpace(caption)
            ? $"{caption.TrimEnd('.')}."
            : "The service did not return a reliable global caption."));

        sections.Add("OBSERVED SUBJECTS" + Environment.NewLine + (objects.Count > 0
            ? $"The image contains {JoinNatural(objects)}. These are model detections of visible entities; they do not establish ownership, identity, purpose, or activity."
            : "No object detections met the configured confidence threshold."));

        sections.Add("ENVIRONMENT" + Environment.NewLine + (categories.Count > 0
            ? $"The visual classification is consistent with {JoinNatural(categories)}. This describes the apparent scene category, not a verified location."
            : "The available evidence is insufficient to classify the environment with confidence."));

        sections.Add("COMPOSITION" + Environment.NewLine + (objects.Count > 0
            ? "The detected subjects form the principal visual structure of the frame, with the surrounding setting providing contextual information. Precise foreground, midground, and background relationships require region-level evidence and are not asserted here."
            : "A reliable compositional account could not be derived from the available detections."));

        if (!string.IsNullOrWhiteSpace(colors))
            sections.Add("COLOUR" + Environment.NewLine + colors);

        sections.Add("INTERPRETATION" + Environment.NewLine + (objects.Count > 0
            ? $"Taken together, the evidence suggests an image organized around {JoinNatural(objects.Take(5).ToList())}. The exact purpose, ownership, organization, and circumstances represented cannot be established from the image alone."
            : "The image cannot be interpreted beyond the limited evidence returned by the vision provider."));

        sections.Add("UNCERTAINTY" + Environment.NewLine + "Model confidence is evidence quality, not factual certainty. Names, identities, locations, intentions, and relationships are treated as unknown unless independently supported by explicit visual evidence.");

        if (tags.Count > 0)
            sections.Add("ADDITIONAL EVIDENCE" + Environment.NewLine + $"Detected concepts above threshold: {JoinNatural(tags.Take(8).ToList())}.");

        return string.Join(separator, sections);
    }


    private static ImageAnalysisResult BuildStructuredAnalysis(JsonDocument document, byte[] imageBytes, IReadOnlyList<DetectedObject> objects, IReadOnlyList<string> tags, TimeSpan duration)
    {
        var categories = ParseNames(document, "categories", 0.45);
        var color = ParseColors(document);
        var caption = ParseCaption(document);
        var hash = GetCacheKey(imageBytes).Replace("vision-analysis-", string.Empty, StringComparison.Ordinal);
        var overview = !string.IsNullOrWhiteSpace(caption) ? caption.TrimEnd('.') + "." : "No reliable global caption was returned by the provider.";
        var interpretations = objects.Count > 0 ? new[] { new Interpretation { Statement = $"The arrangement appears consistent with a visual scene organized around {JoinNatural(objects.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList())}.", EvidenceBasis = objects.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Confidence = 0.60 } } : Array.Empty<Interpretation>();
        return new ImageAnalysisResult
        {
            Metadata = new AnalysisMetadata { AnalysisId = Guid.NewGuid(), ImageHash = hash, CreatedAt = DateTimeOffset.UtcNow, ProcessingDuration = duration },
            Overview = overview,
            Objects = objects,
            Tags = tags.Select((name, index) => new EvidenceTag { Name = name, Confidence = 0.55 }).ToArray(),
            Scene = new SceneAnalysis { Categories = categories, Environment = categories.Count > 0 ? JoinNatural(categories) : "Unavailable" },
            Colors = color,
            Interpretations = interpretations,
            Uncertainties = new[] { new Uncertainty { Statement = "The exact identity, location, ownership, and purpose represented cannot be established from the image alone.", Reason = "The current provider supplies visual detections, not verified contextual provenance." } },
            Limitations = new[] { "OCR is unavailable in the current analyze request.", "Dense captions are unavailable in the current Azure Vision v3.2 integration.", "Model confidence indicates evidence quality, not factual certainty." }
        };
    }

    private static IReadOnlyList<DetectedObject> ParseObjects(JsonDocument document)
    {
        var results = new List<DetectedObject>();
        JsonElement elements;
        if (document.RootElement.TryGetProperty("objects", out var direct) && direct.ValueKind == JsonValueKind.Array) elements = direct;
        else if (document.RootElement.TryGetProperty("objectsResult", out var wrapped) && wrapped.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array) elements = values;
        else return results;
        foreach (var element in elements.EnumerateArray())
        {
            var name = GetStringProperty(element, "object") ?? GetStringProperty(element, "name");
            var confidence = element.TryGetProperty("confidence", out var score) && score.ValueKind == JsonValueKind.Number ? score.GetDouble() : 0;
            if (string.IsNullOrWhiteSpace(name) || confidence < 0.55) continue;
            BoundingBox? box = null;
            if (element.TryGetProperty("rectangle", out var rectangle)) box = new BoundingBox { X = GetInt(rectangle, "x"), Y = GetInt(rectangle, "y"), Width = GetInt(rectangle, "w"), Height = GetInt(rectangle, "h") };
            if (!results.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Location?.X == box?.X && x.Location?.Y == box?.Y)) results.Add(new DetectedObject { Name = name, Confidence = confidence, Location = box });
        }
        return results.OrderByDescending(x => x.Confidence).ToArray();
    }

    private static int GetInt(JsonElement element, string propertyName) => element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static ColorAnalysis ParseColors(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("color", out var color)) return new ColorAnalysis();
        var foreground = color.TryGetProperty("dominantColorForeground", out var f) ? f.GetString() : null;
        var background = color.TryGetProperty("dominantColorBackground", out var b) ? b.GetString() : null;
        return new ColorAnalysis { DominantForegroundColor = foreground, DominantBackgroundColor = background, IsBlackAndWhite = color.TryGetProperty("isBWImg", out var bw) ? bw.GetBoolean() : null };
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

            var value = GetStringProperty(element, "name") ?? GetStringProperty(element, "object");
            if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value, StringComparer.OrdinalIgnoreCase)) names.Add(value);
        }
        return names;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
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