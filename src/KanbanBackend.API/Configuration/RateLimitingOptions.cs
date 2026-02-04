namespace KanbanBackend.API.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int RegisterLimit { get; set; } = 5;
    public int LoginLimit { get; set; } = 5;
    public int RefreshLimit { get; set; } = 30;
    public int WindowMinutes { get; set; } = 1;
}
