namespace KanbanBackend.API.Configuration;

public class RankRebalancingOptions
{
    public const string SectionName = "RankRebalancing";
    
    public double MinGap { get; set; } = 1e-6; // 0.000001
    public double Spacing { get; set; } = 1000.0;
    public int MaxAttempts { get; set; } = 3;
}
