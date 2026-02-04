using Microsoft.Extensions.Options;

namespace KanbanBackend.API.Configuration;

public class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        var errors = new List<string>();

        if (options.RegisterLimit <= 0)
        {
            errors.Add($"RegisterLimit must be > 0. Found: {options.RegisterLimit}");
        }

        if (options.LoginLimit <= 0)
        {
            errors.Add($"LoginLimit must be > 0. Found: {options.LoginLimit}");
        }

        if (options.RefreshLimit <= 0)
        {
            errors.Add($"RefreshLimit must be > 0. Found: {options.RefreshLimit}");
        }

        if (options.WindowMinutes <= 0)
        {
            errors.Add($"WindowMinutes must be > 0. Found: {options.WindowMinutes}");
        }

        if (errors.Count > 0)
        {
            return ValidateOptionsResult.Fail(string.Join("; ", errors));
        }

        return ValidateOptionsResult.Success;
    }
}
