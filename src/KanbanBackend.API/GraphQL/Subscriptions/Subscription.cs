using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using KanbanBackend.API.Data;
using KanbanBackend.API.Extensions;
using KanbanBackend.API.GraphQL.Payloads;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
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
        [Service] IPermissionService permissionService,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.GetRequiredUserId();
        await permissionService.EnsureBoardOwnershipAsync(boardId, userId);
        return await receiver.SubscribeAsync<CardPayload>($"Board_{boardId}");
    }

    [Subscribe(With = nameof(OnCardMovedStream))]
    public CardPayload OnCardMoved([EventMessage] CardPayload message) => message;

    [Authorize]
    [GraphQLIgnore]
    public async ValueTask<ISourceStream<ColumnRebalancedPayload>> OnColumnRebalancedStream(
        Guid boardId,
        [Service] ITopicEventReceiver receiver,
        [Service] IPermissionService permissionService,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.GetRequiredUserId();
        await permissionService.EnsureBoardOwnershipAsync(boardId, userId);
        return await receiver.SubscribeAsync<ColumnRebalancedPayload>($"BoardRebalance_{boardId}");
    }

    [Subscribe(With = nameof(OnColumnRebalancedStream))]
    public ColumnRebalancedPayload OnColumnRebalanced([EventMessage] ColumnRebalancedPayload message) => message;

}
