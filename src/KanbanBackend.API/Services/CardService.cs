using HotChocolate;
using HotChocolate.Subscriptions;
using KanbanBackend.API.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly RankRebalancingOptions _rebalancingOptions;
    private readonly ILogger<CardService> _logger;

    public CardService(
        AppDbContext context, 
        ITopicEventSender eventSender,
        IOptions<RankRebalancingOptions> rebalancingOptions,
        ILogger<CardService> logger)
    {
        _context = context;
        _eventSender = eventSender;
        _rebalancingOptions = rebalancingOptions.Value;
        _logger = logger;
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

        byte[] clientVersion;
        try
        {
            clientVersion = Convert.FromBase64String(input.RowVersion);
        }
        catch (FormatException)
        {
            throw new PreconditionRequiredException("Invalid RowVersion format (Base64 expected).");
        }

        _context.Entry(card).OriginalValues["RowVersion"] = clientVersion;

        // 4. Update State
        card.ColumnId = input.ColumnId;
        card.Rank = input.Rank;

        // 5. Persist
        await _context.SaveChangesAsync();

        // 6. Check for Rebalancing Needs (Post-Save)
        // If ranks deeply collide or are too close, rebalance asynchronously.
        // We do this check AFTER implicit success to not block the user move logic heavily.
        await CheckAndRebalanceColumnAsync(targetColumn.Id, card);

        // Map to Payload
        var payload = new CardPayload(card);

        // 7. Publish Event
        string topic = $"Board_{currentBoardId}";
        await _eventSender.SendAsync(topic, payload);

        return payload;
    }

    private async Task CheckAndRebalanceColumnAsync(Guid columnId, Card movedCard)
    {
        // Feature B: Check local neighbors instead of full scan
        double minGap = _rebalancingOptions.MinGap;
        bool needsRebalance = false;

        // 0. Check for Direct Collision (Gap = 0)
        // If any other card has the exact same rank, we MUST rebalance/fix it.
        bool hasCollision = await _context.Cards
            .AnyAsync(c => c.ColumnId == columnId && c.Id != movedCard.Id && c.Rank == movedCard.Rank);

        if (hasCollision)
        {
            needsRebalance = true;
        }
        else
        {
            // 1. Get Predecessor Rank
            var prevRank = await _context.Cards
                .Where(c => c.ColumnId == columnId && c.Rank < movedCard.Rank)
                .OrderByDescending(c => c.Rank)
                .Select(c => (double?)c.Rank)
                .FirstOrDefaultAsync();

            // 2. Get Successor Rank
            var nextRank = await _context.Cards
                .Where(c => c.ColumnId == columnId && c.Rank > movedCard.Rank)
                .OrderBy(c => c.Rank)
                .Select(c => (double?)c.Rank)
                .FirstOrDefaultAsync();

            // 3. Check Gaps
            if (prevRank.HasValue && Math.Abs(movedCard.Rank - prevRank.Value) < minGap)
            {
                needsRebalance = true;
            }
            else if (nextRank.HasValue && Math.Abs(nextRank.Value - movedCard.Rank) < minGap)
            {
                needsRebalance = true;
            }
        }

        if (!needsRebalance) return;

        // ---- Start Rebalancing Logic (Feature D / C) ----
        
        // Fetch ALL cards now that we know we need to rebalance
        var cards = await _context.Cards
             .Where(c => c.ColumnId == columnId)
             .OrderBy(c => c.Rank)
             .ThenBy(c => c.CreatedAt)
             .ToListAsync();

        _logger.LogInformation("Rebalancing column {ColumnId} (Count: {Count}). Gap < MinGap ({MinGap}). Spacing used: {Spacing}.", 
            columnId, cards.Count, _rebalancingOptions.MinGap, _rebalancingOptions.Spacing);

        for (int attempt = 1; attempt <= _rebalancingOptions.MaxAttempts; attempt++)
        {
            try
            {
                double currentRank = _rebalancingOptions.Spacing;
                foreach (var c in cards)
                {
                    // Reload to get latest version before modification
                    await _context.Entry(c).ReloadAsync();
                    c.Rank = currentRank;
                    currentRank += _rebalancingOptions.Spacing;
                }

                await _context.SaveChangesAsync();
                
                // Feature E: Notify clients to refetch
                var boardId = await _context.Columns
                    .Where(c => c.Id == columnId)
                    .Select(c => c.BoardId)
                    .FirstOrDefaultAsync();

                if (boardId != Guid.Empty)
                {
                    string topic = $"BoardRebalance_{boardId}";
                    await _eventSender.SendAsync(topic, new ColumnRebalancedPayload(columnId, DateTimeOffset.UtcNow));
                }

                return; // Success
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Concurrency conflict during rebalancing (Column: {ColumnId}). Attempt {Attempt} of {MaxAttempts}.", 
                    columnId, attempt, _rebalancingOptions.MaxAttempts);

                if (attempt == _rebalancingOptions.MaxAttempts)
                {
                     throw new GraphQLException(new Error("Rank rebalance failed; please retry.", "REBALANCE_FAILED"));
                }
                
                await Task.Delay(50 * attempt);
            }
        }
    }
}
