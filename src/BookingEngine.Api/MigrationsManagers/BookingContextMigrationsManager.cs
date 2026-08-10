using BookingEngine.Infrastructure.Bookings;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.Api.MigrationsManagers;

/// <summary>
/// Applies pending booking-database migrations before the host starts serving.
/// </summary>
public sealed record BookingContextMigrationsManager : IHostedLifecycleService
{
    private readonly IServiceProvider _serviceProvider;

    public BookingContextMigrationsManager(IServiceProvider serviceProvider)
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

        BookingDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<BookingDbContext>();

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
