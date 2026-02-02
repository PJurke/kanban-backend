using FluentValidation;
using HotChocolate;
using HotChocolate.Authorization;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HotChocolate.Subscriptions;

namespace KanbanBackend.API.GraphQL.Mutations;

public class Mutation
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
    public async Task<Card> AddCard(
        AddCardInput input,
        [Service] AppDbContext context,
        [Service] IValidator<AddCardInput> validator,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        validator.ValidateAndThrow(input);

        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!await context.Columns.AnyAsync(c => c.Id == input.ColumnId && c.Board.OwnerId == userId))
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
    
    [Authorize]
    public async Task<Card> MoveCard(
        MoveCardInput input,
        [Service] AppDbContext context,
        [Service] IValidator<MoveCardInput> validator,
        [Service] ITopicEventSender eventSender,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        validator.ValidateAndThrow(input);

        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        // 1. Fetch Card with Column & Board to check ownership & current state
        var card = await context.Cards
            .Include(c => c.Column)
            .ThenInclude(col => col!.Board)
            .FirstOrDefaultAsync(c => c.Id == input.CardId);

        if (card == null)
        {
             throw new EntityNotFoundException("Card", input.CardId);
        }
        
        // Ownership Check (Strict: Must be Owner)
        if (card.Column?.Board?.OwnerId != userId)
        {
             // Per requirements: Return NOT_FOUND to prevent enumeration
             throw new EntityNotFoundException("Card", input.CardId);
        }

        // 2. Fetch Target Column to ensure it exists and belongs to the SAME BOARD
        var targetColumn = await context.Columns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == input.ColumnId);

        if (targetColumn == null)
        {
             throw new EntityNotFoundException("Column", input.ColumnId);
        }

        // Silo Check: Target column must be on the same board as the card's current column
        // Note: card.Column should not be null for a valid existing card, but good to be safe.
        // If card.Column is null (orphaned card?), we at least check targetColumn ownership.
        var currentBoardId = card.Column?.BoardId;
        
        if (targetColumn.BoardId != currentBoardId)
        {
            throw new DomainException("Cannot move card to a column on a different board.");
        }

        // 3. Update State
        card.ColumnId = input.ColumnId;
        card.Rank = input.Rank;

        // 4. Persist
        await context.SaveChangesAsync();

        // 5. Publish Event
        // Topic: Board_{BoardId}
        string topic = $"Board_{currentBoardId}";
        await eventSender.SendAsync(topic, card);

        return card;
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
