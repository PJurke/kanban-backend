using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;

namespace KanbanBackend.API.Services;

public interface IBoardService
{
    Task<Board> AddBoardAsync(AddBoardInput input, string userId);
}
