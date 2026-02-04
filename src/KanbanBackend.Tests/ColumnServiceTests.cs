using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace KanbanBackend.Tests;

public class ColumnServiceTests
{
    private readonly AppDbContext _context;
    private readonly Mock<IValidator<AddColumnInput>> _addValidatorMock;
    private readonly Mock<IValidator<UpdateColumnInput>> _updateValidatorMock;
    private readonly Mock<IPermissionService> _permissionServiceMock;
    private readonly ColumnService _service;

    public ColumnServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        _addValidatorMock = new Mock<IValidator<AddColumnInput>>();
        _updateValidatorMock = new Mock<IValidator<UpdateColumnInput>>();
        _permissionServiceMock = new Mock<IPermissionService>();

        _service = new ColumnService(_context, _addValidatorMock.Object, _updateValidatorMock.Object, _permissionServiceMock.Object);
    }

    private void SetupAddValidatorSuccess()
    {
        _addValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    private void SetupUpdateValidatorSuccess()
    {
        _updateValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    private void SetupUpdateValidatorThrows(List<ValidationFailure> failures)
    {
        // ValidateAndThrowAsync sets ThrowOnFailures=true internally, so the validator
        // throws directly instead of returning an invalid result
        _updateValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException(failures));
    }

    [Fact]
    public async Task AddColumnAsync_Should_CreateColumn_When_InputIsValidAndBoardExists()
    {
        // Arrange
        var userId = "user-123";
        var boardId = Guid.NewGuid();
        var input = new AddColumnInput(boardId, "Todo", 0);

        _context.Boards.Add(new Board { Id = boardId, OwnerId = userId, Name = "Test Board" });
        await _context.SaveChangesAsync();

        SetupAddValidatorSuccess();

        // Act
        var result = await _service.AddColumnAsync(input, userId);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(input.Name);
        result.BoardId.Should().Be(input.BoardId);

        var dbColumn = await _context.Columns.FindAsync(result.Id);
        dbColumn.Should().NotBeNull();
    }

    [Fact]
    public async Task AddColumnAsync_Should_ThrowEntityNotFound_When_BoardDoesNotExist()
    {
        // Arrange
        var userId = "user-123";
        var input = new AddColumnInput(Guid.NewGuid(), "Todo", 0);

        SetupAddValidatorSuccess();
        _permissionServiceMock.Setup(x => x.EnsureBoardOwnershipAsync(input.BoardId, userId))
            .ThrowsAsync(new EntityNotFoundException("Board", input.BoardId));

        // Act & Assert
        var act = () => _service.AddColumnAsync(input, userId);
        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task UpdateColumnAsync_Should_UpdateWipLimit_When_Provided()
    {
        // Arrange
        var userId = "user-123";
        var columnId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        // Seed Board & Column
        _context.Boards.Add(new Board { Id = boardId, OwnerId = userId, Name = "Test Board" });
        _context.Columns.Add(new Column { Id = columnId, BoardId = boardId, Name = "Todo", Order = 0, WipLimit = 5 });
        await _context.SaveChangesAsync();

        var input = new UpdateColumnInput(columnId, 10); // Update WIP to 10

        SetupUpdateValidatorSuccess();

        // Act
        var result = await _service.UpdateColumnAsync(input, userId);

        // Assert
        result.WipLimit.Should().Be(10);

        var dbColumn = await _context.Columns.FindAsync(columnId);
        dbColumn!.WipLimit.Should().Be(10);
    }

    [Fact]
    public async Task UpdateColumnAsync_Should_ThrowEntityNotFound_When_NotOwner()
    {
        // Arrange
        var userId = "user-123";
        var otherUser = "other-user";
        var columnId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        // Seed Board owned by OTHER user
        _context.Boards.Add(new Board { Id = boardId, OwnerId = otherUser, Name = "Other Board" });
        _context.Columns.Add(new Column { Id = columnId, BoardId = boardId, Name = "Todo", Order = 0 });
        await _context.SaveChangesAsync();

        var input = new UpdateColumnInput(columnId, null);

        SetupUpdateValidatorSuccess();

        // Act & Assert
        var act = () => _service.UpdateColumnAsync(input, userId);
        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task UpdateColumnAsync_Should_Throw_When_InputIsInvalid()
    {
        // Arrange
        var userId = "user-123";
        var input = new UpdateColumnInput(Guid.NewGuid(), -1);
        var failures = new List<ValidationFailure>
        {
            new("WipLimit", "WIP limit must be greater than 0.")
        };

        SetupUpdateValidatorThrows(failures);

        // Act & Assert
        var act = () => _service.UpdateColumnAsync(input, userId);
        await act.Should().ThrowAsync<ValidationException>();
    }
}
