using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using KanbanBackend.API.Data;
using KanbanBackend.API.Extensions;
using KanbanBackend.API.GraphQL.Payloads;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Subscriptions;

public class Subscription
{

    [Authorize]
    [GraphQLIgnore]
    public async ValueTask<ISourceStream<CardPayload>> OnCardMovedStream(
        Guid boardId,
        [Service] ITopicEventReceiver receiver,
        [Service] AppDbContext context,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        await ValidateBoardOwnershipAsync(boardId, context, user);
        return await receiver.SubscribeAsync<CardPayload>($"Board_{boardId}");
    }

    [Subscribe(With = nameof(OnCardMovedStream))]
    public CardPayload OnCardMoved([EventMessage] CardPayload message) => message;

    [Authorize]
    [GraphQLIgnore]
    public async ValueTask<ISourceStream<ColumnRebalancedPayload>> OnColumnRebalancedStream(
        Guid boardId,
        [Service] ITopicEventReceiver receiver,
        [Service] AppDbContext context,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        await ValidateBoardOwnershipAsync(boardId, context, user);
        return await receiver.SubscribeAsync<ColumnRebalancedPayload>($"BoardRebalance_{boardId}");
    }

    [Subscribe(With = nameof(OnColumnRebalancedStream))]
    public ColumnRebalancedPayload OnColumnRebalanced([EventMessage] ColumnRebalancedPayload message) => message;

    /// <summary>
    /// Validates that the current user owns the specified board.
    /// Throws a GraphQLException with ACCESS_DENIED error code if the user is not the owner.
    /// </summary>
    /// <param name="boardId">The ID of the board to validate ownership for</param>
    /// <param name="context">The database context</param>
    /// <param name="user">The current user's claims principal</param>
    /// <exception cref="GraphQLException">Thrown when the user does not own the board</exception>
    private static async Task ValidateBoardOwnershipAsync(
        Guid boardId,
        AppDbContext context,
        ClaimsPrincipal user)
    {
        var userId = user.GetRequiredUserId();

        // Security: Check Board Ownership
        // We use AnyAsync for efficiency as we only need to know if the user owns this board
        var isOwner = await context.Boards
            .AnyAsync(b => b.Id == boardId && b.OwnerId == userId);

        if (!isOwner)
        {
            throw new GraphQLException(new Error("Access denied", "ACCESS_DENIED"));
        }
    }
}
