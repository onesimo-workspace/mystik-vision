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

        if (string.IsNullOrWhiteSpace(_endpoint))
        {
            throw new ArgumentException("AzureVision:Endpoint must be configured.", nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(_subscriptionKey))
        {
            throw new ArgumentException("AzureVision:Key must be configured.", nameof(configuration));
        }
    }

    public async Task<VisionAnalysisResult> AnalyzeImageAsync(string imageUrl, byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(imageBytes);

        if (_cache.TryGetValue(cacheKey, out VisionAnalysisResult? cachedResult) && cachedResult is not null)
        {
            return new VisionAnalysisResult
            {
                Description = cachedResult.Description,
                Tags = cachedResult.Tags,
                IsCached = true
            };
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
            Description = BuildDetailedDescription(document),
            Tags = ParseTags(document),
            IsCached = false
        };

        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1)
        });

        return result;
    }

    private static string BuildDetailedDescription(JsonDocument document)
    {
        var parts = new List<string>();
        var caption = ParseCaption(document);
        if (!string.IsNullOrWhiteSpace(caption))
        {
            parts.Add(caption.TrimEnd('.') + ".");
        }

        var objects = ParseNames(document, "objects");
        if (objects.Count > 0)
        {
            parts.Add($"The image contains {JoinNatural(objects)}.");
        }

        var categories = ParseNames(document, "categories");
        if (categories.Count > 0)
        {
            parts.Add($"It appears to show {JoinNatural(categories)}.");
        }

        var colors = ParseColorSummary(document);
        if (!string.IsNullOrWhiteSpace(colors))
        {
            parts.Add(colors);
        }

        var tags = ParseTags(document);
        if (tags.Count > 0)
        {
            parts.Add($"Notable concepts include {JoinNatural(tags.Take(8).ToList())}.");
        }

        return parts.Count > 0
            ? string.Join(" ", parts)
            : "No detailed description was available for this image.";
    }

    private static string ParseCaption(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("description", out var description) &&
            description.TryGetProperty("captions", out var captions) &&
            captions.ValueKind == JsonValueKind.Array && captions.GetArrayLength() > 0 &&
            captions[0].TryGetProperty("text", out var text))
        {
            return text.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> ParseTags(JsonDocument document)
    {
        return ParseNames(document, "tags");
    }

    private static List<string> ParseNames(JsonDocument document, string propertyName)
    {
        var names = new List<string>();
        if (!document.RootElement.TryGetProperty(propertyName, out var elements) || elements.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (element.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString()))
            {
                var value = name.GetString()!;
                if (!names.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(value);
                }
            }
        }

        return names;
    }

    private static string ParseColorSummary(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("color", out var color))
        {
            return string.Empty;
        }

        var dominant = color.TryGetProperty("dominantColorForeground", out var foreground)
            ? foreground.GetString()
            : null;
        var background = color.TryGetProperty("dominantColorBackground", out var backgroundElement)
            ? backgroundElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(foreground) && string.IsNullOrWhiteSpace(background))
        {
            return string.Empty;
        }

        return $"The dominant colors are {JoinNatural(new[] { foreground, background }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList())}.";
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
        var hash = sha256.ComputeHash(imageBytes);
        return "vision-analysis-" + Convert.ToHexString(hash);
    }
}