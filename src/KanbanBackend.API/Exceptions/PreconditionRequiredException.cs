namespace KanbanBackend.API.Exceptions;

public class PreconditionRequiredException : Exception
{
    public PreconditionRequiredException(string message) : base(message) { }
}
