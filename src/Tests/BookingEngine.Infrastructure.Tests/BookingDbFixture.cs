using BookingEngine.Infrastructure.Bookings;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BookingEngine.Infrastructure.Tests;

/// <summary>
/// Runs a throwaway PostgreSQL instance with the booking schema migrated onto it.
/// </summary>
public sealed class BookingDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        "postgres:18-alpine"
    ).Build();

    public BookingDbContext NewContext()
    {
        DbContextOptions<BookingDbContext> options =
            new DbContextOptionsBuilder<BookingDbContext>()
                .UseNpgsql(_container.GetConnectionString())
                .Options;

        return new BookingDbContext(options);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _container.StartAsync();

        await using BookingDbContext dbContext = NewContext();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
