using HotChocolate.Subscriptions;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBackend.API.Services;

public class CardService : ICardService
{
    private readonly AppDbContext _context;
    private readonly ITopicEventSender _eventSender;

    public CardService(AppDbContext context, ITopicEventSender eventSender)
    {
        _context = context;
        _eventSender = eventSender;
    }

    public async Task<Card> MoveCardAsync(Guid cardId, MoveCardInput input, string userId)
    {
        // 1. Fetch Card with Column & Board to check ownership & current state
        var card = await _context.Cards
            .Include(c => c.Column)
            .ThenInclude(col => col!.Board)
            .FirstOrDefaultAsync(c => c.Id == cardId);

        if (card == null)
        {
             throw new EntityNotFoundException("Card", cardId);
        }
        
        // Ownership Check (Strict: Must be Owner)
        if (card.Column?.Board?.OwnerId != userId)
        {
             // Per requirements: Return NOT_FOUND to prevent enumeration
             throw new EntityNotFoundException("Card", cardId);
        }

        // 2. Fetch Target Column to ensure it exists and belongs to the SAME BOARD
        var targetColumn = await _context.Columns
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
        await _context.SaveChangesAsync();

        // 5. Publish Event
        // Topic: Board_{BoardId}
        string topic = $"Board_{currentBoardId}";
        await _eventSender.SendAsync(topic, card);

        return card;
    }
}
