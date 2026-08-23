using System.Collections.Concurrent;
using app_dev_assignment.Models;

namespace app_dev_assignment.Services;

public sealed class HistoryService : IHistoryService
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<HistoryItem>> _historyByVisitor = new();
    private const int MaxItems = 30;

    public void Add(string visitorId, HistoryItem item)
    {
        var history = _historyByVisitor.GetOrAdd(visitorId, _ => new ConcurrentQueue<HistoryItem>());
        history.Enqueue(item);

        while (history.Count > MaxItems && history.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<HistoryItem> GetAll(string visitorId)
    {
        return _historyByVisitor.TryGetValue(visitorId, out var history)
            ? history.Reverse().ToArray()
            : Array.Empty<HistoryItem>();
    }
}