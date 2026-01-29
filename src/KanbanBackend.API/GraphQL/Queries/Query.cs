using HotChocolate;
using HotChocolate.Data;
using KanbanBackend.API.Data;
using KanbanBackend.API.Models;

namespace KanbanBackend.API.GraphQL.Queries;

public class Query
{
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Board> GetBoards([Service] AppDbContext context) =>
        context.Boards;
}
