using System.Text;
using System.Text.Json;
using app_dev_assignment.Models;

namespace app_dev_assignment.Services;

public sealed class VisionProviderResult
{
    public string Provider { get; init; } = "Azure Computer Vision";
    public string ApiVersion { get; init; } = "v3.2";
    public string? Caption { get; init; }
    public IReadOnlyList<EvidenceTag> Tags { get; init; } = Array.Empty<EvidenceTag>();
    public IReadOnlyList<DetectedObject> Objects { get; init; } = Array.Empty<DetectedObject>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public ColorAnalysis Colors { get; init; } = new();
}

public interface IVisionAnalysisProvider
{
    Task<VisionProviderResult> AnalyzeAsync(string imageUrl, CancellationToken cancellationToken = default);
}

public sealed class AzureVisionAnalysisProvider : IVisionAnalysisProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _subscriptionKey;

    public AzureVisionAnalysisProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _endpoint = configuration["AzureVision:Endpoint"]?.TrimEnd('/') ?? string.Empty;
        _subscriptionKey = configuration["AzureVision:Key"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_endpoint) || _endpoint.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("AzureVision:Endpoint must be configured with a real endpoint.", nameof(configuration));
        if (string.IsNullOrWhiteSpace(_subscriptionKey) || _subscriptionKey.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("AzureVision:Key must be configured with a real key.", nameof(configuration));
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<VisionProviderResult> AnalyzeAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        var uri = new Uri($"{_endpoint}/vision/v3.2/analyze?visualFeatures=Description,Tags,Objects,Categories,Color,ImageType,Brands,Faces&details=Celebrities,Landmarks&language=en");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new { url = imageUrl }), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return Parse(document);
    }

    internal static VisionProviderResult Parse(JsonDocument document)
    {
        var root = document.RootElement;
        string? caption = null;
        if (root.TryGetProperty("description", out var description) && description.TryGetProperty("captions", out var captions) && captions.ValueKind == JsonValueKind.Array)
            caption = captions.EnumerateArray().Select(x => x.TryGetProperty("text", out var text) ? text.GetString() : null).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return new VisionProviderResult { Caption = caption, Tags = ParseTags(root), Categories = ParseNames(root, "categories", 0.45), Objects = ParseObjects(root), Colors = ParseColors(root) };
    }

    private static IReadOnlyList<DetectedObject> ParseObjects(JsonElement root)
    {
        JsonElement array;
        if (root.TryGetProperty("objects", out var direct) && direct.ValueKind == JsonValueKind.Array) array = direct;
        else if (root.TryGetProperty("objectsResult", out var wrapped) && wrapped.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array) array = values;
        else return Array.Empty<DetectedObject>();
        var result = new List<DetectedObject>();
        foreach (var item in array.EnumerateArray())
        {
            var name = StringProperty(item, "object") ?? StringProperty(item, "name");
            var confidence = NumberProperty(item, "confidence");
            if (string.IsNullOrWhiteSpace(name) || confidence < 0.55) continue;
            BoundingBox? location = null;
            if (item.TryGetProperty("rectangle", out var rectangle)) location = new BoundingBox { X = IntProperty(rectangle, "x"), Y = IntProperty(rectangle, "y"), Width = IntProperty(rectangle, "w"), Height = IntProperty(rectangle, "h") };
            if (!result.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Location?.X == location?.X && x.Location?.Y == location?.Y)) result.Add(new DetectedObject { Name = name, Confidence = confidence, Location = location });
        }
        return result.OrderByDescending(x => x.Confidence).ToArray();
    }

    private static IReadOnlyList<EvidenceTag> ParseTags(JsonElement root)
    {
        if (!root.TryGetProperty("tags", out var array) || array.ValueKind != JsonValueKind.Array) return Array.Empty<EvidenceTag>();
        return array.EnumerateArray().Where(x => NumberProperty(x, "confidence") >= 0.55).Select(x => new EvidenceTag { Name = StringProperty(x, "name") ?? String.Empty, Confidence = NumberProperty(x, "confidence") }).Where(x => !String.IsNullOrWhiteSpace(x.Name)).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderByDescending(y => y.Confidence).First()).ToArray();
    }

    private static IReadOnlyList<string> ParseNames(JsonElement root, string property, double threshold)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return array.EnumerateArray().Where(x => NumberProperty(x, "confidence") >= threshold).Select(x => StringProperty(x, "name") ?? StringProperty(x, "object")).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ColorAnalysis ParseColors(JsonElement root)
    {
        if (!root.TryGetProperty("color", out var color)) return new ColorAnalysis();
        return new ColorAnalysis { DominantForegroundColor = StringProperty(color, "dominantColorForeground"), DominantBackgroundColor = StringProperty(color, "dominantColorBackground"), IsBlackAndWhite = BooleanProperty(color, "isBWImg") };
    }

    private static bool? BooleanProperty(JsonElement item, string property) => item.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) ? value.GetBoolean() : null;
    private static string? StringProperty(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static double NumberProperty(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;
    private static int IntProperty(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
