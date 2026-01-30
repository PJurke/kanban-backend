namespace KanbanBackend.API.GraphQL.Inputs;

public record AddColumnInput(Guid BoardId, string Name, int Order);
