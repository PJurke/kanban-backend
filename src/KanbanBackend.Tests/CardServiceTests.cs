using FluentAssertions;
using HotChocolate.Subscriptions;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace KanbanBackend.Tests;

public class CardServiceTests
{
    private readonly AppDbContext _context;
    private readonly Mock<ITopicEventSender> _eventSenderMock;
    private readonly CardService _cardService;

    public CardServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Unique DB per test
            .Options;

        _context = new AppDbContext(options);
        _eventSenderMock = new Mock<ITopicEventSender>();
        _cardService = new CardService(_context, _eventSenderMock.Object);
    }

    [Fact]
    public async Task MoveCard_ShouldThrow_WhenCardNotFound()
    {
        // Act
        var act = () => _cardService.MoveCardAsync(Guid.NewGuid(), 
            new MoveCardInput { CardId = Guid.NewGuid(), ColumnId = Guid.NewGuid(), Rank = 0 }, 
            "user1");

        // Assert
        await act.Should().ThrowAsync<EntityNotFoundException>()
            .WithMessage("*Card*");
    }

    [Fact]
    public async Task MoveCard_ShouldThrow_WhenUserNotOwner()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "Board1", OwnerId = "owner" };
        var column = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Col1", Order = 0 };
        var card = new Card { Id = Guid.NewGuid(), ColumnId = column.Id, Name = "Card1", Rank = 0 };

        _context.Boards.Add(board);
        _context.Columns.Add(column);
        _context.Cards.Add(card);
        await _context.SaveChangesAsync();

        // Act
        var act = () => _cardService.MoveCardAsync(card.Id, 
            new MoveCardInput { CardId = card.Id, ColumnId = column.Id, Rank = 1 }, 
            "other-user");

        // Assert
        await act.Should().ThrowAsync<EntityNotFoundException>()
             .WithMessage("*Card*"); // Should behave as if not found for security
    }

    [Fact]
    public async Task MoveCard_ShouldThrow_WhenTargetColumnNotFound()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "Board1", OwnerId = "owner" };
        var column = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Col1", Order = 0 };
        var card = new Card { Id = Guid.NewGuid(), ColumnId = column.Id, Name = "Card1", Rank = 0 };

        _context.Boards.Add(board);
        _context.Columns.Add(column);
        _context.Cards.Add(card);
        await _context.SaveChangesAsync();

        // Act
        var act = () => _cardService.MoveCardAsync(card.Id, 
            new MoveCardInput { CardId = card.Id, ColumnId = Guid.NewGuid(), Rank = 1 }, 
            "owner");

        // Assert
        await act.Should().ThrowAsync<EntityNotFoundException>()
            .WithMessage("*Column*");
    }
    
    [Fact]
    public async Task MoveCard_ShouldThrow_WhenTargetColumnOnDifferentBoard()
    {
        // Arrange
        var board1 = new Board { Id = Guid.NewGuid(), Name = "Board1", OwnerId = "owner" };
        var col1 = new Column { Id = Guid.NewGuid(), BoardId = board1.Id, Name = "Col1", Order = 0 };
        var card = new Card { Id = Guid.NewGuid(), ColumnId = col1.Id, Name = "Card1", Rank = 0 };

        var board2 = new Board { Id = Guid.NewGuid(), Name = "Board2", OwnerId = "owner" };
        var col2 = new Column { Id = Guid.NewGuid(), BoardId = board2.Id, Name = "Col2", Order = 0 };

        _context.Boards.AddRange(board1, board2);
        _context.Columns.AddRange(col1, col2);
        _context.Cards.Add(card);
        await _context.SaveChangesAsync();

        // Act
        var act = () => _cardService.MoveCardAsync(card.Id, 
            new MoveCardInput { CardId = card.Id, ColumnId = col2.Id, Rank = 1 }, 
            "owner");

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*different board*");
    }

    [Fact]
    public async Task MoveCard_ShouldUpdateAndPublish_WhenValid()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "Board1", OwnerId = "owner" };
        var col1 = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Col1", Order = 0 };
        var col2 = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Col2", Order = 1 };
        var card = new Card { Id = Guid.NewGuid(), ColumnId = col1.Id, Name = "Card1", Rank = 0 };

        _context.Boards.Add(board);
        _context.Columns.AddRange(col1, col2);
        _context.Cards.Add(card);
        await _context.SaveChangesAsync();

        // Act
        var result = await _cardService.MoveCardAsync(card.Id, 
            new MoveCardInput { CardId = card.Id, ColumnId = col2.Id, Rank = 5 }, 
            "owner");

        // Assert
        result.ColumnId.Should().Be(col2.Id);
        result.Rank.Should().Be(5);

        var dbCard = await _context.Cards.FindAsync(card.Id);
        dbCard!.ColumnId.Should().Be(col2.Id);
        dbCard.Rank.Should().Be(5);

        _eventSenderMock.Verify(x => x.SendAsync($"Board_{board.Id}", It.Is<Card>(c => c.Id == card.Id), default), Times.Once);
    }
}
