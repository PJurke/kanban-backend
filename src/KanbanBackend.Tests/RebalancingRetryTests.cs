using FluentAssertions;
using FluentValidation;
using HotChocolate.Subscriptions;
using KanbanBackend.API.Configuration;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace KanbanBackend.Tests;

public class RebalancingRetryTests
{
    private readonly Mock<ITopicEventSender> _eventSenderMock = new();
    private readonly Mock<IOptions<RankRebalancingOptions>> _optionsMock = new();
    private readonly Mock<ILogger<CardService>> _loggerMock = new();
    private readonly Mock<IValidator<AddCardInput>> _validatorMock = new();

    // Custom Context to simulate faults
    public class FaultyDbContext : AppDbContext
    {
        private int _successfulSaves = 0;
        public int FailAfterSaves { get; set; } = int.MaxValue;
        
        public FaultyDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_successfulSaves >= FailAfterSaves)
            {
                throw new DbUpdateConcurrencyException("Simulated Concurrency Conflict");
            }
            _successfulSaves++;
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task MoveCard_ShouldFail_WhenRebalanceExceedsMaxAttempts()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
            
        var db = new FaultyDbContext(options);

        // Config
        _optionsMock.Setup(o => o.Value).Returns(new RankRebalancingOptions { 
            MinGap = 100, // Force trigger
            Spacing = 1000.0, 
            MaxAttempts = 3 
        });

        // Setup Logger to verify warnings
        // _loggerMock.Setup... verify later.

        var service = new CardService(db, _eventSenderMock.Object, _optionsMock.Object, _loggerMock.Object, _validatorMock.Object);

        var board = new Board { Id = Guid.NewGuid(), Name = "B", OwnerId = "u" };
        var col = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "C", Order = 0 };
        var c1 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 100, Name="1" };
        var c2 = new Card { Id = Guid.NewGuid(), ColumnId = col.Id, Rank = 101, Name="2", RowVersion = new byte[]{0,0,0,1} };
        
        db.Boards.Add(board);
        db.Columns.Add(col);
        db.Cards.AddRange(c1, c2);
        
        // Initial setup save (1)
        await db.SaveChangesAsync();
        
        // We want MoveCard(success) -> Count becomes 2.
        // Then Rebalance attempt 1 fails (Count 2 >= FailAfterSaves?)
        // So FailAfterSaves should be 2. (0-based? No. save call 1 succeeds, increments to 1. Call 2 succeeds, inc to 2. Call 3 fails?)
        // Wait logic:
        // if (_successfulSaves >= FailAfterSaves) throw
        // Currently _successfulSaves = 1.
        // Make FailAfterSaves = 2.
        // MoveCard (Save 2): 1 >= 2 is False. Succeeds. increment to 2.
        // Rebalance (Save 3): 2 >= 2 is True. Throws.
        
        db.FailAfterSaves = 2; // Setup(1) + Move(1) = 2. Rebalance should fail.

        // Act
        var act = () => service.MoveCardAsync(c2.Id, 
            new KanbanBackend.API.GraphQL.Inputs.MoveCardInput { CardId = c2.Id, ColumnId = col.Id, Rank = 100.5, RowVersion = Convert.ToBase64String(c2.RowVersion) }, 
            "u");

        // Assert
        // Logic will retry 3 times. All fail. Should throw GraphQLException.
        await act.Should().ThrowAsync<RebalanceFailedException>()
            .WithMessage("*rebalance failed*");

        // Verify Log
        // Should log warning 3 times.
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Concurrency conflict")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(3));
    }
}
