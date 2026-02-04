using FluentValidation;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBackend.API.Services;

public class ColumnService : IColumnService
{
    private readonly AppDbContext _context;
    private readonly IValidator<AddColumnInput> _addValidator;
    private readonly IValidator<UpdateColumnInput> _updateValidator;
    private readonly IPermissionService _permissionService;

    public ColumnService(
        AppDbContext context,
        IValidator<AddColumnInput> addValidator,
        IValidator<UpdateColumnInput> updateValidator,
        IPermissionService permissionService)
    {
        _context = context;
        _addValidator = addValidator;
        _updateValidator = updateValidator;
        _permissionService = permissionService;
    }

    public async Task<Column> AddColumnAsync(AddColumnInput input, string userId)
    {
        await _addValidator.ValidateAndThrowAsync(input);
        await _permissionService.EnsureBoardOwnershipAsync(input.BoardId, userId);

        var column = new Column
        {
            Id = Guid.NewGuid(),
            BoardId = input.BoardId,
            Name = input.Name,
            Order = input.Order
        };

        _context.Columns.Add(column);
        await _context.SaveChangesAsync();

        return column;
    }

    public async Task<Column> UpdateColumnAsync(UpdateColumnInput input, string userId)
    {
        await _updateValidator.ValidateAndThrowAsync(input);

        var column = await _context.Columns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == input.Id);
            
        if (column == null)
        {
            throw new EntityNotFoundException("Column", input.Id);
        }

        if (column.Board?.OwnerId != userId)
        {
             throw new EntityNotFoundException("Column", input.Id);
        }

        if (input.WipLimit.HasValue)
        {
            column.WipLimit = input.WipLimit.Value;
        }

        await _context.SaveChangesAsync();

        return column;
    }
}
