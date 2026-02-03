using HotChocolate.Subscriptions;
using KanbanBackend.API.Configuration;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Payloads;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbanBackend.API.Services;

public class RankRebalancingService : IRankRebalancingService
{
    private readonly AppDbContext _context;
    private readonly ITopicEventSender _eventSender;
    private readonly RankRebalancingOptions _options;
    private readonly ILogger<RankRebalancingService> _logger;

    public RankRebalancingService(
        AppDbContext context,
        ITopicEventSender eventSender,
        IOptions<RankRebalancingOptions> options,
        ILogger<RankRebalancingService> logger)
    {
        _context = context;
        _eventSender = eventSender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task CheckAndRebalanceIfNeededAsync(Guid columnId, Card movedCard)
    {
        if (!await NeedsRebalancingAsync(columnId, movedCard))
        {
            return;
        }

        await ExecuteRebalanceWithRetryAsync(columnId);
        await NotifyRebalanceCompletedAsync(columnId);
    }

    private async Task<bool> NeedsRebalancingAsync(Guid columnId, Card movedCard)
    {
        // Performance optimization: Single query to get all neighbor ranks
        var neighborRanks = await _context.Cards
            .Where(c => c.ColumnId == columnId && c.Id != movedCard.Id)
            .OrderBy(c => c.Rank)
            .Select(c => c.Rank)
            .ToListAsync();

        // Check for direct collision (another card with exact same rank)
        if (neighborRanks.Contains(movedCard.Rank))
        {
            return true;
        }

        // Find predecessor (largest rank smaller than movedCard)
        double? prevRank = neighborRanks
            .Where(r => r < movedCard.Rank)
            .Cast<double?>()
            .LastOrDefault();

        // Find successor (smallest rank larger than movedCard)
        double? nextRank = neighborRanks
            .Where(r => r > movedCard.Rank)
            .Cast<double?>()
            .FirstOrDefault();

        // Check gaps against MinGap threshold
        if (prevRank.HasValue && Math.Abs(movedCard.Rank - prevRank.Value) < _options.MinGap)
        {
            return true;
        }

        if (nextRank.HasValue && Math.Abs(nextRank.Value - movedCard.Rank) < _options.MinGap)
        {
            return true;
        }

        return false;
    }

    private async Task ExecuteRebalanceWithRetryAsync(Guid columnId)
    {
        var cards = await _context.Cards
            .Where(c => c.ColumnId == columnId)
            .OrderBy(c => c.Rank)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync();

        _logger.LogInformation(
            "Rebalancing column {ColumnId} (Count: {Count}). Gap < MinGap ({MinGap}). Spacing used: {Spacing}.",
            columnId, cards.Count, _options.MinGap, _options.Spacing);

        for (int attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try
            {
                double currentRank = _options.Spacing;
                foreach (var card in cards)
                {
                    // Reload to get latest version before modification
                    await _context.Entry(card).ReloadAsync();
                    card.Rank = currentRank;
                    currentRank += _options.Spacing;
                }

                await _context.SaveChangesAsync();
                return; // Success
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Concurrency conflict during rebalancing (Column: {ColumnId}). Attempt {Attempt} of {MaxAttempts}.",
                    columnId, attempt, _options.MaxAttempts);

                if (attempt == _options.MaxAttempts)
                {
                    throw new RebalanceFailedException(columnId, _options.MaxAttempts);
                }

                await Task.Delay(50 * attempt);
            }
        }
    }

    private async Task NotifyRebalanceCompletedAsync(Guid columnId)
    {
        var boardId = await _context.Columns
            .Where(c => c.Id == columnId)
            .Select(c => c.BoardId)
            .FirstOrDefaultAsync();

        if (boardId != Guid.Empty)
        {
            string topic = $"BoardRebalance_{boardId}";
            await _eventSender.SendAsync(topic, new ColumnRebalancedPayload(columnId, DateTimeOffset.UtcNow));
        }
    }
}
