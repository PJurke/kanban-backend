namespace KanbanBackend.API.Exceptions;

public class RebalanceFailedException : Exception
{
    public Guid ColumnId { get; }
    public int MaxAttempts { get; }

    public RebalanceFailedException(Guid columnId, int maxAttempts)
        : base($"Rank rebalance failed after {maxAttempts} attempts; please retry.")
    {
        ColumnId = columnId;
        MaxAttempts = maxAttempts;
    }
}
