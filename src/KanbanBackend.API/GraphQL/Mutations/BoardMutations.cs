using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using System.IdentityModel.Tokens.Jwt;
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
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
        {
             throw new GraphQLException(new Error("User ID not found in token", "AUTH_INVALID_TOKEN"));
        }

        return await boardService.AddBoardAsync(input, userId);
    }
}
