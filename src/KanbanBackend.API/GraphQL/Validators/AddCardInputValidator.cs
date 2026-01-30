using FluentValidation;
using KanbanBackend.API.GraphQL.Inputs;

namespace KanbanBackend.API.GraphQL.Validators;

public class AddCardInputValidator : AbstractValidator<AddCardInput>
{
    public AddCardInputValidator()
    {
        RuleFor(x => x.ColumnId).NotEmpty().WithMessage("Column ID is required.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Card name is required.")
            .MaximumLength(200).WithMessage("Card name must not exceed 200 characters.");
        RuleFor(x => x.Rank).GreaterThanOrEqualTo(0).WithMessage("Rank must be 0 or greater.");
    }
}
