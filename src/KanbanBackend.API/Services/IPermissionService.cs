namespace KanbanBackend.API.Services;

public interface IPermissionService
{
    Task EnsureBoardOwnershipAsync(Guid boardId, string userId);
    Task EnsureColumnBelongsToUserBoardAsync(Guid columnId, string userId);
    Task EnsureCardBelongsToUserBoardAsync(Guid cardId, string userId);
}
