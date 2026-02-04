using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using KanbanBackend.API.Extensions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
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
        var userId = user.GetRequiredUserId();

        return await columnService.AddColumnAsync(input, userId);
    }

    [Authorize]
    public async Task<Column> UpdateColumn(
        UpdateColumnInput input,
        [Service] IColumnService columnService,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.GetRequiredUserId();

        return await columnService.UpdateColumnAsync(input, userId);
    }
}
