using FluentAssertions;
using FluentValidation;
using HotChocolate.Subscriptions;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.GraphQL.Payloads;
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
    private readonly Mock<IRankRebalancingService> _rebalancingServiceMock;
    private readonly CardService _cardService;
    private readonly string _dummyVersion = Convert.ToBase64String(BitConverter.GetBytes(1u));

    public CardServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Unique DB per test
            .Options;

        _context = new AppDbContext(options);
        _eventSenderMock = new Mock<ITopicEventSender>();
        _rebalancingServiceMock = new Mock<IRankRebalancingService>();

        var validatorMock = new Mock<IValidator<AddCardInput>>();

        _cardService = new CardService(_context, _eventSenderMock.Object, _rebalancingServiceMock.Object, validatorMock.Object);
    }

    [Fact]
    public async Task MoveCard_ShouldThrow_WhenCardNotFound()
    {
        // Act
        var act = () => _cardService.MoveCardAsync(Guid.NewGuid(),
            new MoveCardInput { CardId = Guid.NewGuid(), ColumnId = Guid.NewGuid(), Rank = 0, RowVersion = _dummyVersion },
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
            new MoveCardInput { CardId = card.Id, ColumnId = column.Id, Rank = 1, RowVersion = _dummyVersion },
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
            new MoveCardInput { CardId = card.Id, ColumnId = Guid.NewGuid(), Rank = 1, RowVersion = _dummyVersion },
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
            new MoveCardInput { CardId = card.Id, ColumnId = col2.Id, Rank = 1, RowVersion = _dummyVersion },
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

        // Ensure RowVersion is set in DB to match what we assume
        var card = new Card { Id = Guid.NewGuid(), ColumnId = col1.Id, Name = "Card1", Rank = 0, RowVersion = 1 };

        _context.Boards.Add(board);
        _context.Columns.AddRange(col1, col2);
        _context.Cards.Add(card);
        await _context.SaveChangesAsync();

        // Act
        var result = await _cardService.MoveCardAsync(card.Id,
            new MoveCardInput { CardId = card.Id, ColumnId = col2.Id, Rank = 5, RowVersion = _dummyVersion },
            "owner");

        // Assert
        result.Should().BeOfType<CardPayload>();
        result.ColumnId.Should().Be(col2.Id);
        result.Rank.Should().Be(5);
        result.RowVersion.Should().NotBeNullOrEmpty();

        var dbCard = await _context.Cards.FindAsync(card.Id);
        dbCard!.ColumnId.Should().Be(col2.Id);
        dbCard.Rank.Should().Be(5);

        _eventSenderMock.Verify(x => x.SendAsync($"Board_{board.Id}", It.Is<CardPayload>(c => c.Id == card.Id), default), Times.Once);
        _rebalancingServiceMock.Verify(x => x.CheckAndRebalanceIfNeededAsync(col2.Id, It.Is<Card>(c => c.Id == card.Id)), Times.Once);
    }

    [Fact]
    public async Task MoveCard_ShouldCallRebalancingService_AfterSuccessfulMove()
    {
        // Arrange
        var board = new Board { Id = Guid.NewGuid(), Name = "Board1", OwnerId = "owner" };
        var column = new Column { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Col1", Order = 0 };
        var card = new Card { Id = Guid.NewGuid(), ColumnId = column.Id, Name = "Card1", Rank = 0, RowVersion = 1 };

        _context.Boards.Add(board);
        _context.Columns.Add(column);
        _context.Cards.Add(card);
        await _context.SaveChangesAsync();

        // Act
        await _cardService.MoveCardAsync(card.Id,
            new MoveCardInput { CardId = card.Id, ColumnId = column.Id, Rank = 100, RowVersion = _dummyVersion },
            "owner");

        // Assert
        _rebalancingServiceMock.Verify(
            x => x.CheckAndRebalanceIfNeededAsync(column.Id, It.Is<Card>(c => c.Id == card.Id)),
            Times.Once);
    }
}
