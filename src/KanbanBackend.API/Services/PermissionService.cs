using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using Microsoft.EntityFrameworkCore;
using HotChocolate; // For GraphQLException if needed, but per request using custom exceptions

namespace KanbanBackend.API.Services;

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _context;

    public PermissionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task EnsureBoardOwnershipAsync(Guid boardId, string userId)
    {
        var isOwner = await _context.Boards
            .AnyAsync(b => b.Id == boardId && b.OwnerId == userId);

        if (!isOwner)
        {
            // For security, if board exists but not owned, or doesn't exist, we usually handle it.
            // But per previous code in Subscription.cs: "Access denied"
            // Per previous code in Services: "EntityNotFoundException" or "Access denied"
            // "Access denied" is appropriate for explicit ownership checks where ID is known.
            // However, CardService used EntityNotFoundException to prevent enumeration.
            // Let's use EntityNotFoundException to be consistent with "Prevent enumeration".
            // But wait, Subscription used "Access denied".
            // If I use EntityNotFoundException, the error code becomes NOT_FOUND. 
            // If I use custom "AccessDeniedException", I need to map it.
            // Let's look at `GraphQLErrorFilter`. It has no "FORBIDDEN" or "ACCESS_DENIED" mapping from Exception.
            // The Subscription code manually threw `new GraphQLException(new Error("Access denied", "ACCESS_DENIED"))`.
            // Generic services should avoid `GraphQLException`.
            // I will throw `EntityNotFoundException` for now as it's the safest default (404).
            // UNLESS it's strictly an access check where existence is known (e.g. from a parent).
            // But here we query by ID. 
            // Checking Subscription.cs again: it threw "ACCESS_DENIED".
            // Checking CardService.cs: it threw "EntityNotFoundException".
            
            // I will stick to EntityNotFoundException ("Board", boardId) to align with CRUD services.
            // For Subscription, "ACCESS_DENIED" is also fine, but "Board not found" is safer.
            // Actually, let's use a new `AccessDeniedException` and map it? 
            // No, let's stick to what's used in Services for consistency.
            throw new EntityNotFoundException("Board", boardId);
        }
    }

    public async Task EnsureColumnBelongsToUserBoardAsync(Guid columnId, string userId)
    {
        var exists = await _context.Columns
            .AnyAsync(c => c.Id == columnId && c.Board != null && c.Board.OwnerId == userId);

        if (!exists)
        {
            throw new EntityNotFoundException("Column", columnId);
        }
    }

    public async Task EnsureCardBelongsToUserBoardAsync(Guid cardId, string userId)
    {
        var exists = await _context.Cards
             .AnyAsync(c => c.Id == cardId && c.Column != null && c.Column.Board != null && c.Column.Board.OwnerId == userId);

        if (!exists)
        {
            throw new EntityNotFoundException("Card", cardId); 
        }
    }
}
