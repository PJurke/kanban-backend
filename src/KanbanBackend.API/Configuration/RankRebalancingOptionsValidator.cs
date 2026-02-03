using Microsoft.Extensions.Options;

namespace KanbanBackend.API.Configuration;

public class RankRebalancingOptionsValidator : IValidateOptions<RankRebalancingOptions>
{
    public ValidateOptionsResult Validate(string? name, RankRebalancingOptions options)
    {
        var errors = new List<string>();

        if (options.MinGap <= 0)
        {
            errors.Add($"MinGap must be > 0. Found: {options.MinGap}");
        }

        if (options.Spacing <= 0)
        {
            errors.Add($"Spacing must be > 0. Found: {options.Spacing}");
        }

        if (options.MinGap >= options.Spacing)
        {
            errors.Add($"MinGap must be less than Spacing. Found MinGap: {options.MinGap}, Spacing: {options.Spacing}");
        }

        if (options.MaxAttempts <= 0)
        {
            errors.Add($"MaxAttempts must be > 0. Found: {options.MaxAttempts}");
        }

        if (errors.Count > 0)
        {
            return ValidateOptionsResult.Fail(string.Join("; ", errors));
        }

        return ValidateOptionsResult.Success;
    }
}
