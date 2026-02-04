using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using KanbanBackend.API.Extensions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using System.Security.Claims;
using KanbanBackend.API.Services;

namespace KanbanBackend.API.GraphQL.Mutations;

[ExtendObjectType("Mutation")]
public class BoardMutations
{
    [Authorize]
    public async Task<Board> AddBoard(
        AddBoardInput input,
        [Service] IBoardService boardService,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.GetRequiredUserId();

        return await boardService.AddBoardAsync(input, userId);
    }
}
