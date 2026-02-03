using FluentValidation;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using KanbanBackend.API.GraphQL.Inputs;
using KanbanBackend.API.GraphQL.Payloads;
using KanbanBackend.API.Models;
using KanbanBackend.API.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace KanbanBackend.API.GraphQL.Mutations;

[ExtendObjectType("Mutation")]
public class CardMutations
{
    [Authorize]
    public async Task<Card> AddCard(
        AddCardInput input,
        [Service] ICardService cardService,
        [GlobalState("ClaimsPrincipal")] ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
        {
            throw new GraphQLException(new Error("User ID not found in token", "AUTH_INVALID_TOKEN"));
        }

        return await cardService.AddCardAsync(input, userId);
    }

    [Authorize]
    public async Task<CardPayload> MoveCard(
        MoveCardInput input,
        [Service] ICardService cardService,
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
