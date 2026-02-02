using FluentValidation;
using KanbanBackend.API.GraphQL.Inputs;

namespace KanbanBackend.API.GraphQL.Validators;

public class UpdateColumnInputValidator : AbstractValidator<UpdateColumnInput>
{
    public UpdateColumnInputValidator()
    {
        RuleFor(x => x.WipLimit.Value)
            .GreaterThan(0)
            .When(x => x.WipLimit.HasValue)
            .WithMessage("WIP limit must be greater than 0.");
    }
}
