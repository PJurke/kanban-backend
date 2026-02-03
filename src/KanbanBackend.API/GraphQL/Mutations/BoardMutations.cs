using FluentValidation;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using KanbanBackend.API.Data;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Mutations;

[ExtendObjectType("Mutation")]
public class BoardMutations
{
    [Authorize]
    public async Task<Board> AddBoard(
        AddBoardInput input,
        [Service] AppDbContext context,
        [Service] IValidator<AddBoardInput> validator,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        validator.ValidateAndThrow(input);

        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
        {
             throw new GraphQLException(new Error("User ID not found in token", "AUTH_INVALID_TOKEN"));
        }

        var board = new Board
        {
            Id = Guid.NewGuid(),
            Name = input.Name,
            OwnerId = userId
        };

        context.Boards.Add(board);
        await context.SaveChangesAsync();

        return board;
    }
}
