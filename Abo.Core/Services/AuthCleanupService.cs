using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Abo.Core.Services;

/// <summary>
/// Background service that periodically cleans up expired sessions.
/// </summary>
public class AuthCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuthCleanupService> _logger;

    /// <summary>
    /// How often to run session cleanup (default: every 15 minutes).
    /// </summary>
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(15);

    public AuthCleanupService(
        IServiceProvider serviceProvider,
        ILogger<AuthCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuthCleanupService started. Session cleanup will run every {Interval} minutes",
            _cleanupInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);
                await CleanupSessionsAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }
        }

        _logger.LogInformation("AuthCleanupService stopped");
    }

    private async Task CleanupSessionsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
            
            await authService.CleanupExpiredSessionsAsync();
            
            _logger.LogDebug("Session cleanup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired sessions");
        }
    }
}
