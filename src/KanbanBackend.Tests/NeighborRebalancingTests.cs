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

public class NeighborRebalancingTests
{
    private readonly Mock<ITopicEventSender> _eventSenderMock = new();
    private readonly Mock<IOptions<RankRebalancingOptions>> _optionsMock = new();
    private readonly Mock<ILogger<RankRebalancingService>> _loggerMock = new();
    private readonly AppDbContext _context;
    private readonly RankRebalancingService _service;

    public NeighborRebalancingTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        // MinGap = 1.0 for easy testing
        _optionsMock.Setup(o => o.Value).Returns(new RankRebalancingOptions {
            MinGap = 1.0,
            Spacing = 1000.0,
            MaxAttempts = 1
        });

        _service = new RankRebalancingService(_context, _eventSenderMock.Object, _optionsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldTriggerRebalance_WhenGapToPredecessorIsTooSmall()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Existing Card
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="1" };
        // Moved Card at 100.5. Diff = 0.5 < MinGap(1.0)
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100.5, Name="2" };

        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c2);

        // Assert
        // Rebalance should trigger.
        // Ranks should be 1000, 2000.
        var cards = await _context.Cards.Where(c => c.ColumnId == col.Id).OrderBy(c => c.Rank).ToListAsync();
        cards[0].Rank.Should().Be(1000.0);
        cards[1].Rank.Should().Be(2000.0);

        // Verify Notification
        _eventSenderMock.Verify(x => x.SendAsync(
            It.Is<string>(s => s == $"BoardRebalance_{board.Id}"),
            It.IsAny<ColumnRebalancedPayload>(),
            default),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldTriggerRebalance_WhenGapToSuccessorIsTooSmall()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Moved Card at 99.5, Successor at 100. Diff = 0.5 < MinGap(1.0)
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 99.5, Name="1" };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="2" };

        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c1);

        // Assert
        var cards = await _context.Cards.Where(c => c.ColumnId == col.Id).OrderBy(c => c.Rank).ToListAsync();
        cards[0].Rank.Should().Be(1000.0);
        cards[1].Rank.Should().Be(2000.0);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldTriggerRebalance_WhenCollisionExists()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Two cards with the same rank (collision)
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="1" };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="2" };

        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c2);

        // Assert
        var cards = await _context.Cards.Where(c => c.ColumnId == col.Id).OrderBy(c => c.Rank).ToListAsync();
        cards[0].Rank.Should().Be(1000.0);
        cards[1].Rank.Should().Be(2000.0);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldNotTriggerRebalance_WhenGapIsSufficient()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Gap check: MinGap = 1.0.
        // C1 at 100.
        // C2 at 102. Diff = 2.0 > MinGap.
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="1" };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 102, Name="2" };

        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c2);

        // Assert
        // No Rebalance: Ranks are preserved as is
        var cards = await _context.Cards.Where(c => c.ColumnId == col.Id).OrderBy(c => c.Rank).ToListAsync();
        cards[0].Rank.Should().Be(100.0);
        cards[1].Rank.Should().Be(102.0);

        // No notification sent
        _eventSenderMock.Verify(x => x.SendAsync(
            It.IsAny<string>(),
            It.IsAny<ColumnRebalancedPayload>(),
            default),
            Times.Never);
    }

    [Fact]
    public async Task CheckAndRebalanceIfNeededAsync_ShouldNotTriggerRebalance_WhenNoNeighbors()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Single card, no neighbors
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="1" };

        _context.Cards.Add(c1);
        await _context.SaveChangesAsync();

        // Act
        await _service.CheckAndRebalanceIfNeededAsync(col.Id, c1);

        // Assert
        var card = await _context.Cards.FindAsync(c1.Id);
        card!.Rank.Should().Be(100.0); // Unchanged

        _eventSenderMock.Verify(x => x.SendAsync(
            It.IsAny<string>(),
            It.IsAny<ColumnRebalancedPayload>(),
            default),
            Times.Never);
    }
}
