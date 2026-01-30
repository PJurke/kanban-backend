using FluentValidation;
using KanbanBackend.API.GraphQL.Inputs;

namespace KanbanBackend.API.GraphQL.Validators;

public class AddColumnInputValidator : AbstractValidator<AddColumnInput>
{
    public AddColumnInputValidator()
    {
        RuleFor(x => x.BoardId).NotEmpty().WithMessage("Board ID is required.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Column name is required.")
            .MaximumLength(100).WithMessage("Column name must not exceed 100 characters.");
    }
}
