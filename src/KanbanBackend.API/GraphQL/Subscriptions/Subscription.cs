using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using KanbanBackend.API.Data;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

using KanbanBackend.API.GraphQL.Payloads;

namespace KanbanBackend.API.GraphQL.Subscriptions;

public class Subscription
{

    [Authorize]
    [SubscribeAndResolve]
    public async ValueTask<ISourceStream<CardPayload>> OnCardMoved(
        Guid boardId,
        [Service] ITopicEventReceiver receiver,
        [Service] AppDbContext context,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        // Security: Check Board Ownership
        // We use AnyAsync for efficiency as we only need to know if the user owns this board
        var isOwner = await context.Boards
            .AnyAsync(b => b.Id == boardId && b.OwnerId == userId);

        if (!isOwner)
        {
            throw new GraphQLException(new Error("Access denied", "ACCESS_DENIED"));
        }

        // Return the event stream for this specific board
        // Topic format matches the one in Mutation.cs: "Board_{BoardId}"
        // Realtime notification for UI updates.
        // Best-effort only: state is persisted in DB first.
        // If the event is missed, clients will receive the correct state on next fetch.
        return await receiver.SubscribeAsync<CardPayload>($"Board_{boardId}");
    }

    [Authorize]
    [SubscribeAndResolve]
    public async ValueTask<ISourceStream<ColumnRebalancedPayload>> OnColumnRebalanced(
        Guid boardId,
        [Service] ITopicEventReceiver receiver,
        [Service] AppDbContext context,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        var isOwner = await context.Boards
            .AnyAsync(b => b.Id == boardId && b.OwnerId == userId);

        if (!isOwner)
        {
             throw new GraphQLException(new Error("Access denied", "ACCESS_DENIED"));
        }

        return await receiver.SubscribeAsync<ColumnRebalancedPayload>($"BoardRebalance_{boardId}");
    }
}
