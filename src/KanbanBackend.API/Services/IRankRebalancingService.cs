using KanbanBackend.API.Models;

namespace KanbanBackend.API.Services;

public interface IRankRebalancingService
{
    Task CheckAndRebalanceIfNeededAsync(Guid columnId, Card movedCard);
}
