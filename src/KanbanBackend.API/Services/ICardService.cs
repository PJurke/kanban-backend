using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;

namespace KanbanBackend.API.Services;

public interface ICardService
{
    Task<Card> MoveCardAsync(Guid cardId, MoveCardInput input, string userId);
}
