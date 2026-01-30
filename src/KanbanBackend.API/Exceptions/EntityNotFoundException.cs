namespace KanbanBackend.API.Exceptions;

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entityName, object key) 
        : base($"Entity '{entityName}' with key '{key}' was not found.")
    {
    }
}
