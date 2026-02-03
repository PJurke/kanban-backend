using FluentValidation;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using KanbanBackend.API.Data;
using KanbanBackend.API.Exceptions;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.Models;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Mutations;

using KanbanBackend.API.GraphQL.Payloads;
using KanbanBackend.API.Services;

[ExtendObjectType("Mutation")]
public class CardMutations
{
    [Authorize]
    public async Task<Card> AddCard(
        AddCardInput input,
        [Service] AppDbContext context,
        [Service] IValidator<AddCardInput> validator,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        validator.ValidateAndThrow(input);

        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!await context.Columns.AnyAsync(c => c.Id == input.ColumnId && c.Board.OwnerId == userId))
        {
            throw new EntityNotFoundException("Column", input.ColumnId);
        }

        var card = new Card
        {
            Id = Guid.NewGuid(),
            ColumnId = input.ColumnId,
            Name = input.Name,
            Rank = input.Rank
        };

        context.Cards.Add(card);
        await context.SaveChangesAsync();

        return card;
    }
    


    [Authorize]
    public async Task<CardPayload> MoveCard(
        MoveCardInput input,
        [Service] ICardService cardService, // Injected Service
        [Service] IValidator<MoveCardInput> validator,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        validator.ValidateAndThrow(input);

        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
        {
             throw new GraphQLException(new Error("User ID not found in token", "AUTH_INVALID_TOKEN"));
        }

        return await cardService.MoveCardAsync(input.CardId, input, userId);
    }
}
