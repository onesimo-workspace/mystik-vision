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

        var requestUri = new Uri($"{_endpoint}/vision/v3.2/analyze?visualFeatures=Description,Tags&language=en");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new { url = imageUrl }), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var description = ParseDescription(document);
        var tags = ParseTags(document);

        var result = new VisionAnalysisResult
        {
            Description = description,
            Tags = tags,
            IsCached = false
        };

        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1)
        });

        return result;
    }

    private static string ParseDescription(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("description", out var descriptionElement) &&
            descriptionElement.TryGetProperty("captions", out var captions) &&
            captions.GetArrayLength() > 0 &&
            captions[0].TryGetProperty("text", out var textElement))
        {
            return textElement.GetString() ?? "No description available.";
        }

        return "No description available.";
    }

    private static IReadOnlyList<string> ParseTags(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
        {
            var tags = new List<string>();
            foreach (var tag in tagsElement.EnumerateArray())
            {
                if (tag.TryGetProperty("name", out var nameElement))
                {
                    var tagName = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(tagName))
                    {
                        tags.Add(tagName);
                    }
                }
            }

            return tags;
        }

        return Array.Empty<string>();
    }

    private static string GetCacheKey(byte[] imageBytes)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(imageBytes);
        return "vision-analysis-" + Convert.ToHexString(hash);
    }
}
