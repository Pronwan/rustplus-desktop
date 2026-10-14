using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RustPlusWeb.Data;
using RustPlusWeb.Realtime;

namespace RustPlusWeb.Services;

/// <summary>Fires durable server timers once and fans the event out to connected browser sessions.</summary>
public sealed class ServerTimerService(
    IServiceScopeFactory scopeFactory,
    IHubContext<ServerHub> hub,
    ILogger<ServerTimerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<ServerTimer> expired;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;
                    expired = await db.ServerTimers
                        .Where(timer => !timer.Fired && timer.ExpiresAtUtc <= now)
                        .OrderBy(timer => timer.ExpiresAtUtc)
                        .ToListAsync(stoppingToken);

                    if (expired.Count > 0)
                    {
                        foreach (var timer in expired)
                            timer.Fired = true;
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }

                foreach (var timer in expired)
                {
                    try
                    {
                        await hub.Clients.All.SendAsync("TimerExpired", new
                        {
                            serverId = timer.PairedServerCredentialId,
                            timerId = timer.Id,
                            name = timer.Name,
                            expiresAtUtc = timer.ExpiresAtUtc,
                            firedAtUtc = DateTime.UtcNow
                        }, stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        logger.LogDebug("Timer notification delivery failed ({ExceptionType}).",
                            exception.GetType().Name);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Server timer sweep failed ({ExceptionType}).", exception.GetType().Name);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
