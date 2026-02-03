using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.GraphQL.Payloads;
using KanbanBackend.API.Models;

namespace KanbanBackend.API.Services;

public interface ICardService
{
    Task<Card> AddCardAsync(AddCardInput input, string userId);
    Task<CardPayload> MoveCardAsync(Guid cardId, MoveCardInput input, string userId);
}
