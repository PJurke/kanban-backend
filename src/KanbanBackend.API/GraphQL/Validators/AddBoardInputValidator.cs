using FluentValidation;
using KanbanBackend.API.GraphQL.Inputs;

namespace KanbanBackend.API.GraphQL.Validators;

public class AddBoardInputValidator : AbstractValidator<AddBoardInput>
{
    public AddBoardInputValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Board name is required.")
            .MaximumLength(100).WithMessage("Board name must not exceed 100 characters.");
    }
}
