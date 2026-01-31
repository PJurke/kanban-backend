using System.Threading;
using KanbanBackend.API.Data;
using Microsoft.EntityFrameworkCore;

namespace KanbanBackend.API.Services;

public class TokenCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TokenCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(24);

    public TokenCleanupService(IServiceProvider serviceProvider, ILogger<TokenCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TokenCleanupService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DoCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during token cleanup.");
            }

            // Wait for next cycle
            await Task.Delay(_cleanupInterval, stoppingToken);
        }
    }

    private async Task DoCleanupAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _logger.LogInformation("Cleaning up expired refresh tokens...");

        // Efficient batch delete (EF Core 7+)
        var cutoff = DateTimeOffset.UtcNow;
        var deletedCount = await context.RefreshTokens
            .Where(t => t.Expires < cutoff)
            .ExecuteDeleteAsync(stoppingToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation("Deleted {Count} expired tokens.", deletedCount);
        }
        else
        {
            _logger.LogInformation("No expired tokens found.");
        }
    }
}
