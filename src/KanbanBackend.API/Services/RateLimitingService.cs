using KanbanBackend.API.Configuration;
using KanbanBackend.API.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace KanbanBackend.API.Services;

public class RateLimitingService : IRateLimitingService
{
    private readonly IMemoryCache _cache;
    private readonly RateLimitingOptions _options;

    public RateLimitingService(IMemoryCache cache, IOptions<RateLimitingOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public void CheckRegisterLimit(string ipAddress)
    {
        CheckRateLimit($"Register_{ipAddress}", _options.RegisterLimit, TimeSpan.FromMinutes(_options.WindowMinutes));
    }

    public void CheckLoginLimit(string ipAddress)
    {
        CheckRateLimit($"Login_{ipAddress}", _options.LoginLimit, TimeSpan.FromMinutes(_options.WindowMinutes));
    }

    public void CheckRefreshLimit(string ipAddress)
    {
        CheckRateLimit($"Refresh_{ipAddress}", _options.RefreshLimit, TimeSpan.FromMinutes(_options.WindowMinutes));
    }

    private void CheckRateLimit(string key, int limit, TimeSpan window)
    {
        var count = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = window;
            return 0;
        });

        if (count >= limit)
        {
            throw new RateLimitExceededException("Rate limit exceeded");
        }

        _cache.Set(key, count + 1, window);
    }
}
