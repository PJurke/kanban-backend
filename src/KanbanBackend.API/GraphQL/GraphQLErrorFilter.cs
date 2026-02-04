using FluentValidation;
using HotChocolate;
using KanbanBackend.API.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KanbanBackend.API.GraphQL;

public class GraphQLErrorFilter : IErrorFilter
{
    private readonly ILogger<GraphQLErrorFilter> _logger;
    
    public GraphQLErrorFilter(ILogger<GraphQLErrorFilter> logger) // Added this constructor
    {
        _logger = logger;
    }

    public IError OnError(IError error)
    {
        if (error.Exception != null) // Added this block for logging
        {
             _logger.LogError(error.Exception, "GraphQL Error: {Message}", error.Exception.Message);
        }

        if (error.Exception is EntityNotFoundException)
        {
            return error.WithCode("NOT_FOUND")
                        .WithMessage(error.Exception.Message);
        }

        if (error.Exception is DomainException)
        {
            return error.WithCode("BAD_REQUEST")
                        .WithMessage(error.Exception.Message);
        }

        if (error.Exception is PreconditionRequiredException)
        {
            return error.WithCode("PRECONDITION_REQUIRED")
                        .WithMessage(error.Exception.Message);
        }

        if (error.Exception is RebalanceFailedException)
        {
            return error.WithCode("REBALANCE_FAILED")
                        .WithMessage(error.Exception.Message);
        }

        if (error.Exception is ValidationException validationException)
        {
            var extensions = new Dictionary<string, object?>
            {
                { "errors", validationException.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }) }
            };

            return error.WithCode("VALIDATION_ERROR")
                        .WithMessage("Validation failed.")
                        .WithExtensions(extensions);
        }

        if (error.Exception is DbUpdateConcurrencyException)
        {
             return error.WithCode("CONFLICT")
                         .WithMessage("Card was modified by another operation. Please reload.");
        }

        if (error.Exception is RateLimitExceededException)
        {
            return error.WithCode("AUTH_RATE_LIMIT")
                        .WithMessage(error.Exception.Message);
        }

        if (error.Exception != null)
        {
            // Unhandled exception
            return error.WithCode("INTERNAL_SERVER_ERROR")
                        .WithMessage("An unexpected error occurred.");
        }

        return error;
    }
}
