using HotChocolate;
using HotChocolate.Data;
using KanbanBackend.API.Data;
using KanbanBackend.API.Models;

using HotChocolate.Authorization;
using System.Security.Claims;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;

namespace KanbanBackend.API.GraphQL.Queries;

public class Query
{
    [Authorize]
    [UseOffsetPaging(IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Board> GetBoards(
        [Service] AppDbContext context,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return context.Boards.Where(b => b.OwnerId == userId);
    }
}
