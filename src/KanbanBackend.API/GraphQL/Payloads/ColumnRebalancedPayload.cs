namespace KanbanBackend.API.GraphQL.Payloads;

public record ColumnRebalancedPayload(Guid ColumnId, DateTimeOffset Timestamp);
