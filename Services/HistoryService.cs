using System.Collections.Concurrent;
using app_dev_assignment.Models;

namespace app_dev_assignment.Services;

public sealed class HistoryService : IHistoryService
{
    private readonly ConcurrentQueue<HistoryItem> _history = new();
    private const int MaxItems = 30;

    public void Add(HistoryItem item)
    {
        _history.Enqueue(item);
        while (_history.Count > MaxItems && _history.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<HistoryItem> GetAll()
    {
        return _history.Reverse().ToArray();
    }
}
