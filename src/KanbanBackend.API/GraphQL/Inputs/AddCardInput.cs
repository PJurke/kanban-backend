namespace KanbanBackend.API.GraphQL.Inputs;

public record AddCardInput(Guid ColumnId, string Name, double Rank);
