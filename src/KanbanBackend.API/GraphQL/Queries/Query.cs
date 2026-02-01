using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Data;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;
using KanbanBackend.API.Data;
using KanbanBackend.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

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
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return context.Boards.Where(b => b.OwnerId == userId);
    }
}
