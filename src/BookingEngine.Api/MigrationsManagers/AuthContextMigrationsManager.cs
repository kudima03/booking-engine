using BookingEngine.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.Api.MigrationsManagers;

/// <summary>
/// Applies pending authentication-database migrations before the host starts serving.
/// </summary>
public sealed record AuthContextMigrationsManager : IHostedLifecycleService
{
    private readonly IServiceProvider _serviceProvider;

    public AuthContextMigrationsManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();

        AuthDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
