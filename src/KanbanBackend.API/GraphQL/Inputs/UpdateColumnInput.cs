using HotChocolate;

namespace KanbanBackend.API.GraphQL.Inputs;

public record UpdateColumnInput(Guid Id, Optional<int?> WipLimit);
