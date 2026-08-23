using app_dev_assignment.Models;

namespace app_dev_assignment.Services;

public interface IHistoryService
{
    void Add(string visitorId, HistoryItem item);
    IReadOnlyList<HistoryItem> GetAll(string visitorId);
}