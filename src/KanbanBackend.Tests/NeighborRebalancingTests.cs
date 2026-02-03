using FluentAssertions;
using FluentValidation;
using HotChocolate.Subscriptions;
using KanbanBackend.API.Configuration;
using KanbanBackend.API.Data;
using KanbanBackend.API.GraphQL.Inputs;
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
    private readonly Mock<ILogger<CardService>> _loggerMock = new();
    private readonly Mock<IValidator<AddCardInput>> _validatorMock = new();
    private readonly AppDbContext _context;
    private readonly CardService _service;

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

        _service = new CardService(_context, _eventSenderMock.Object, _optionsMock.Object, _loggerMock.Object, _validatorMock.Object);
    }

    [Fact]
    public async Task MoveCard_ShouldTriggerRebalance_WhenGapToPredecessorIsTooSmall()
    {
        // Assemble
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Existing Card
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="1" }; 
        // New Card will be moved to 100.5. Diff = 0.5 < MinGap(1.0)
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 5000, Name="2", RowVersion = new byte[]{1} };
        
        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        // Move C2 to 100.5
        var input = new KanbanBackend.API.GraphQL.Inputs.MoveCardInput { 
            CardId = c2.Id, 
            ColumnId = col.Id, 
            Rank = 100.5, 
            RowVersion = Convert.ToBase64String(c2.RowVersion) 
        };
        
        await _service.MoveCardAsync(c2.Id, input, "u");

        // Assert
        // Rebalance should trigger.
        // Ranks should be 1000, 2000.
        
        var cards = await _context.Cards.Where(c => c.ColumnId == col.Id).OrderBy(c => c.Rank).ToListAsync();
        cards[0].Rank.Should().Be(1000.0);
        cards[1].Rank.Should().Be(2000.0);

        // Feature E: Verify Notification
        _eventSenderMock.Verify(x => x.SendAsync(
            It.Is<string>(s => s == $"BoardRebalance_{board.Id}"), 
            It.IsAny<KanbanBackend.API.GraphQL.Payloads.ColumnRebalancedPayload>(), 
            default), 
            Times.Once);
    }

    [Fact]
    public async Task MoveCard_ShouldNotTriggerRebalance_WhenGapIsSufficient()
    {
        // Assemble
        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        _context.Boards.Add(board);
        _context.Columns.Add(col);

        // Gap check: MinGap = 1.0.
        // C1 at 100.
        // C2 moved to 102. Diff = 2.0 > MinGap.
        
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="1" }; 
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 5000, Name="2", RowVersion = new byte[]{1} };
        
        _context.Cards.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        // Act
        var input = new KanbanBackend.API.GraphQL.Inputs.MoveCardInput { 
            CardId = c2.Id, 
            ColumnId = col.Id, 
            Rank = 102.0, 
            RowVersion = Convert.ToBase64String(c2.RowVersion) 
        };
        
        await _service.MoveCardAsync(c2.Id, input, "u");

        // Assert
        var cards = await _context.Cards.Where(c => c.ColumnId == col.Id).OrderBy(c => c.Rank).ToListAsync();
        
        // No Rebalance: Ranks are preserved as is (Move just updates C2)
        cards[0].Rank.Should().Be(100.0);
        cards[1].Rank.Should().Be(102.0);
    }
}
