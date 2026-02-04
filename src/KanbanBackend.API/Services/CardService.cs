using FluentValidation;
using HotChocolate.Subscriptions;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.GraphQL.Payloads;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBackend.API.Services;

public class CardService : ICardService
{
    private readonly AppDbContext _context;
    private readonly ITopicEventSender _eventSender;
    private readonly IRankRebalancingService _rebalancingService;
    private readonly IValidator<AddCardInput> _addCardValidator;

    public CardService(
        AppDbContext context,
        ITopicEventSender eventSender,
        IRankRebalancingService rebalancingService,
        IValidator<AddCardInput> addCardValidator)
    {
        _context = context;
        _eventSender = eventSender;
        _rebalancingService = rebalancingService;
        _addCardValidator = addCardValidator;
    }

    public async Task<Card> AddCardAsync(AddCardInput input, string userId)
    {
        await _addCardValidator.ValidateAndThrowAsync(input);

        var columnExists = await _context.Columns
            .AnyAsync(c => c.Id == input.ColumnId && c.Board.OwnerId == userId);

        if (!columnExists)
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

        _context.Cards.Add(card);
        await _context.SaveChangesAsync();

        return card;
    }

    public async Task<CardPayload> MoveCardAsync(Guid cardId, MoveCardInput input, string userId)
    {
        // 1. Fetch Card with Column & Board to check ownership & current state
        var card = await _context.Cards
            .AsTracking() // Explicitly enforce tracking for concurrency
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

        // 2. Fetch Target Column
        var targetColumn = await _context.Columns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == input.ColumnId);

        if (targetColumn == null)
        {
             throw new EntityNotFoundException("Column", input.ColumnId);
        }

        // Silo Check
        var currentBoardId = card.Column?.BoardId;

        if (targetColumn.BoardId != currentBoardId)
        {
            throw new DomainException("Cannot move card to a column on a different board.");
        }

        // 3. Concurrency Check (Strict & Mandatory)
        if (string.IsNullOrWhiteSpace(input.RowVersion))
        {
            throw new PreconditionRequiredException("RowVersion is required for this operation.");
        }

        try 
        {
            // Convert Base64 (transport) back to uint (EF Core Concurrency Token)
            var bytes = Convert.FromBase64String(input.RowVersion);
            if (bytes.Length != 4)
                throw new PreconditionRequiredException("Invalid RowVersion format (expected 4 bytes for uint).");
            
            var clientVersion = BitConverter.ToUInt32(bytes);

            // Set OriginalValue for Concurrency Check
            _context.Entry(card).OriginalValues["RowVersion"] = clientVersion;
        }
        catch (FormatException)
        {
            throw new PreconditionRequiredException("Invalid RowVersion format (Base64 expected).");
        }

        // 4. Update State
        card.ColumnId = input.ColumnId;
        card.Rank = input.Rank;

        // 5. Persist
        await _context.SaveChangesAsync();

        // 6. Check for Rebalancing Needs (Post-Save)
        await _rebalancingService.CheckAndRebalanceIfNeededAsync(targetColumn.Id, card);

        // Map to Payload
        var payload = new CardPayload(card);

        // 7. Publish Event
        string topic = $"Board_{currentBoardId}";
        await _eventSender.SendAsync(topic, payload);

        return payload;
    }
}
