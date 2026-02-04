using FluentValidation;
using FluentValidation.Results;
using KanbanBackend.API.Data;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace KanbanBackend.Tests;

public class BoardServiceTests
{
    private readonly AppDbContext _context;
    private readonly Mock<IValidator<AddBoardInput>> _validatorMock;
    private readonly BoardService _service;

    public BoardServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        _validatorMock = new Mock<IValidator<AddBoardInput>>();
        _service = new BoardService(_context, _validatorMock.Object);
    }

    private void SetupValidatorSuccess()
    {
        _validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    private void SetupValidatorThrows(List<ValidationFailure> failures)
    {
        // ValidateAndThrowAsync sets ThrowOnFailures=true internally, so the validator
        // throws directly instead of returning an invalid result
        _validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException(failures));
    }

    [Fact]
    public async Task AddBoardAsync_Should_CreateBoard_When_InputIsValid()
    {
        // Arrange
        var userId = "user-123";
        var input = new AddBoardInput("New Board");
        SetupValidatorSuccess();

        // Act
        var result = await _service.AddBoardAsync(input, userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(input.Name, result.Name);
        Assert.Equal(userId, result.OwnerId);
        Assert.NotEqual(Guid.Empty, result.Id);

        var dbBoard = await _context.Boards.FindAsync(result.Id);
        Assert.NotNull(dbBoard);
        Assert.Equal(input.Name, dbBoard.Name);
    }

    [Fact]
    public async Task AddBoardAsync_Should_ValidateInput()
    {
        // Arrange
        var userId = "user-123";
        var input = new AddBoardInput("New Board");
        SetupValidatorSuccess();

        // Act
        await _service.AddBoardAsync(input, userId);

        // Assert - verify that validation was called
        _validatorMock.Verify(v => v.ValidateAsync(
            It.Is<ValidationContext<AddBoardInput>>(ctx => ctx.InstanceToValidate == input),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddBoardAsync_Should_Throw_When_InputIsInvalid()
    {
        // Arrange
        var userId = "user-123";
        var input = new AddBoardInput("");
        var failures = new List<ValidationFailure>
        {
            new("Name", "Board name is required.")
        };
        SetupValidatorThrows(failures);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => _service.AddBoardAsync(input, userId));
    }
}
