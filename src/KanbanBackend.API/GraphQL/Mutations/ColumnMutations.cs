using FluentValidation;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Mutations;

[ExtendObjectType("Mutation")]
public class ColumnMutations
{
    [Authorize]
    public async Task<Column> AddColumn(
        AddColumnInput input,
        [Service] AppDbContext context,
        [Service] IValidator<AddColumnInput> validator,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        validator.ValidateAndThrow(input);

        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!await context.Boards.AnyAsync(b => b.Id == input.BoardId && b.OwnerId == userId))
        {
            throw new EntityNotFoundException("Board", input.BoardId);
        }

        var column = new Column
        {
            Id = Guid.NewGuid(),
            BoardId = input.BoardId,
            Name = input.Name,
            Order = input.Order
        };

        context.Columns.Add(column);
        await context.SaveChangesAsync();

        return column;
    }

    [Authorize]
    public async Task<Column> UpdateColumn(
        UpdateColumnInput input,
        [Service] AppDbContext context,
        [Service] IValidator<UpdateColumnInput> validator,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        validator.ValidateAndThrow(input);

        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        var column = await context.Columns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == input.Id);
            
        if (column == null)
        {
            throw new EntityNotFoundException("Column", input.Id);
        }

        if (column.Board?.OwnerId != userId)
        {
             throw new EntityNotFoundException("Column", input.Id);
        }

        if (input.WipLimit.HasValue)
        {
            column.WipLimit = input.WipLimit.Value;
        }

        await context.SaveChangesAsync();

        return column;
    }
}
