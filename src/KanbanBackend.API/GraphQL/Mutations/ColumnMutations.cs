using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Mutations;

[ExtendObjectType("Mutation")]
public class ColumnMutations
{
    [Authorize]
    public async Task<Column> AddColumn(
        AddColumnInput input,
        [Service] IColumnService columnService,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
        {
            throw new GraphQLException(new Error("User ID not found in token", "AUTH_INVALID_TOKEN"));
        }

        return await columnService.AddColumnAsync(input, userId);
    }

    [Authorize]
    public async Task<Column> UpdateColumn(
        UpdateColumnInput input,
        [Service] IColumnService columnService,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
        {
            throw new GraphQLException(new Error("User ID not found in token", "AUTH_INVALID_TOKEN"));
        }

        return await columnService.UpdateColumnAsync(input, userId);
    }
}
