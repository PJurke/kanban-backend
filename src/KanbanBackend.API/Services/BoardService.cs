using FluentValidation;
using KanbanBackend.API.Data;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;

namespace KanbanBackend.API.Services;

public class BoardService : IBoardService
{
    private readonly AppDbContext _context;
    private readonly IValidator<AddBoardInput> _validator;

    public BoardService(AppDbContext context, IValidator<AddBoardInput> validator)
    {
        _context = context;
        _validator = validator;
    }

    public async Task<Board> AddBoardAsync(AddBoardInput input, string userId)
    {
        // 1. Validation
        await _validator.ValidateAndThrowAsync(input);

        // 2. Business Logic: Create Board
        var board = new Board
        {
            Id = Guid.NewGuid(),
            Name = input.Name,
            OwnerId = userId
        };

        // 3. Persistence
        _context.Boards.Add(board);
        await _context.SaveChangesAsync();

        return board;
    }
}
