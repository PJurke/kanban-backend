using FluentValidation;
using KanbanBackend.API.Exceptions;

namespace KanbanBackend.API.GraphQL;

public class GraphQLErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
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

        if (error.Exception != null)
        {
            // Unhandled exception
            return error.WithCode("INTERNAL_SERVER_ERROR")
                        .WithMessage("An unexpected error occurred.");
        }

        return error;
    }
}
