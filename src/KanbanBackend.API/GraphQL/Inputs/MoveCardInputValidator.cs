using FluentValidation;

namespace KanbanBackend.API.GraphQL.Inputs;

public class MoveCardInputValidator : AbstractValidator<MoveCardInput>
{
    public MoveCardInputValidator()
    {
        RuleFor(x => x.CardId).NotEmpty();
        RuleFor(x => x.ColumnId).NotEmpty();
        RuleFor(x => x.Rank).GreaterThanOrEqualTo(0);
    }
}
