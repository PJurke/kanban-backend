using HotChocolate;
using KanbanBackend.API.Data;
using KanbanBackend.API.Models;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using HotChocolate.Authorization;
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Mutations;

[Authorize]
public class Mutation
{
    public async Task<Board> AddBoard(
        AddBoardInput input,
        [Service] AppDbContext context,
        [Service] IValidator<AddBoardInput> validator,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        validator.ValidateAndThrow(input);

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
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

    public async Task<Column> AddColumn(
        AddColumnInput input,
        [Service] AppDbContext context,
        [Service] IValidator<AddColumnInput> validator)
    {
        validator.ValidateAndThrow(input);

        if (!await context.Boards.AnyAsync(b => b.Id == input.BoardId))
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

    public async Task<Card> AddCard(
        AddCardInput input,
        [Service] AppDbContext context,
        [Service] IValidator<AddCardInput> validator)
    {
        validator.ValidateAndThrow(input);

        if (!await context.Columns.AnyAsync(c => c.Id == input.ColumnId))
        {
            throw new EntityNotFoundException("Column", input.ColumnId);
        }

        var card = new Card
        {
            Id = Guid.NewGuid(),
            ColumnId = input.ColumnId,
            Name = input.Name,
            Rank = input.Rank
        };

        context.Cards.Add(card);
        await context.SaveChangesAsync();

        return card;
    }
}
