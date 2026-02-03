using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;

namespace KanbanBackend.API.Services;

public interface IColumnService
{
    Task<Column> AddColumnAsync(AddColumnInput input, string userId);
    Task<Column> UpdateColumnAsync(UpdateColumnInput input, string userId);
}
