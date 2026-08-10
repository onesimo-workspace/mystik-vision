namespace app_dev_assignment.Models;

public sealed class HistoryItem
{
    public string ImageUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public bool IsCached { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
