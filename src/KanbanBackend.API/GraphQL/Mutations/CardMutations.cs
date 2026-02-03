using FluentValidation;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Subscriptions;
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
public class CardMutations
{
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
}
