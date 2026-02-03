using FluentAssertions;
using HotChocolate.Subscriptions;
using KanbanBackend.API.Configuration;
using KanbanBackend.API.Data;
using KanbanBackend.API.GraphQL.Payloads;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace KanbanBackend.Tests;

public class RankRebalancingServiceTests
{
    private readonly Mock<ITopicEventSender> _eventSenderMock = new();
    private readonly Mock<ILogger<RankRebalancingService>> _loggerMock = new();
    private readonly AppDbContext _context;
    private readonly RankRebalancingService _service;

    public RankRebalancingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        var optionsMock = new Mock<IOptions<RankRebalancingOptions>>();
        optionsMock.Setup(o => o.Value).Returns(new RankRebalancingOptions
        {
            MinGap = 1.0,
            Spacing = 1000.0,
            MaxAttempts = 3
        });

        _service = new RankRebalancingService(_context, _eventSenderMock.Object, optionsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldRebalanceAllCards_InCorrectOrder()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Cards with small gaps
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 10.0, Name = "Card1" };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 10.3, Name = "Card2" };
        var c3 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 10.6, Name = "Card3" };

        _context.Cards.AddRange(c1, c2, c3);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c2);

        // Assert
        var cards = await _context.Cards
            .Where(c => c.ColumnId == col.Id)
            .OrderBy(c => c.Rank)
            .ToListAsync();

        cards.Should().HaveCount(3);
        cards[0].Rank.Should().Be(1000.0);
        cards[1].Rank.Should().Be(2000.0);
        cards[2].Rank.Should().Be(3000.0);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldPreserveCardOrder_BasedOnRankAndCreatedAt()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Cards with same rank but different creation times
        var olderTime = DateTime.UtcNow.AddMinutes(-10);
        var newerTime = DateTime.UtcNow;

        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100.0, Name = "Older", CreatedAt = olderTime };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100.0, Name = "Newer", CreatedAt = newerTime };

        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c2);

        // Assert
        var cards = await _context.Cards
            .Where(c => c.ColumnId == col.Id)
            .OrderBy(c => c.Rank)
            .ToListAsync();

        // Older card should be first due to ThenBy(CreatedAt) ordering
        cards[0].Name.Should().Be("Older");
        cards[1].Name.Should().Be("Newer");
        cards[0].Rank.Should().Be(1000.0);
        cards[1].Rank.Should().Be(2000.0);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldSendNotification_AfterSuccessfulRebalance()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100.0, Name = "1" };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100.5, Name = "2" };

        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c2);

        // Assert
        _eventSenderMock.Verify(x => x.SendAsync(
            $"BoardRebalance_{board.Id}",
            It.Is<ColumnRebalancedPayload>(p => p.ColumnId == col.Id),
            default),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldLogInformation_WhenRebalancing()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100.0, Name = "1" };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100.5, Name = "2" };

        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c2);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Rebalancing column")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldOnlyAffectCardsInTargetColumn()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col1 = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C1", Order = 0 };
        var col2 = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C2", Order = 1 };
        _context.Boards.Add(board);
        _context.Columns.AddRange(col1, col2);

        // Cards in col1 with small gap
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col1.Id, Rank = 100.0, Name = "1" };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col1.Id, Rank = 100.5, Name = "2" };

        // Card in col2 (should not be affected)
        var c3 = new Card { Id = Guid.NewGuid(), ColumnId = col2.Id, Rank = 50.0, Name = "3" };

        _context.Cards.AddRange(c1, c2, c3);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col1.Id, c2);

        // Assert
        var col1Cards = await _context.Cards.Where(c => c.ColumnId == col1.Id).ToListAsync();
        var col2Card = await _context.Cards.FirstAsync(c => c.ColumnId == col2.Id);

        col1Cards.Should().OnlyContain(c => c.Rank == 1000.0 || c.Rank == 2000.0);
        col2Card.Rank.Should().Be(50.0); // Unchanged
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldHandleLargeNumberOfCards()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Create 50 cards with very small gaps
        var cards = new List<Card>();
        for (int i = 0; i < 50; i++)
        {
            cards.Add(new Card
            {
                Id = Guid.NewGuid(),
                ColumnId = col.Id,
                Rank = 100.0 + (i * 0.01),
                Name = $"Card{i}"
            });
        }

        _context.Cards.AddRange(cards);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, cards[25]);

        // Assert
        var rebalancedCards = await _context.Cards
            .Where(c => c.ColumnId == col.Id)
            .OrderBy(c => c.Rank)
            .ToListAsync();

        rebalancedCards.Should().HaveCount(50);

        // All cards should have proper spacing
        for (int i = 0; i < rebalancedCards.Count; i++)
        {
            rebalancedCards[i].Rank.Should().Be((i + 1) * 1000.0);
        }
    }
}
