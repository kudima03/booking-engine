using BookingEngine.Domain.Models;
using BookingEngine.Infrastructure.Bookings.Configuration;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.Infrastructure.Bookings;

/// <summary>
/// Persistence context for the booking catalogue and the bookings themselves.
/// </summary>
public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options)
    : DbContext(options)
{
    public DbSet<ResourceType> ResourceTypes => Set<ResourceType>();

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<OpeningHours> OpeningHours => Set<OpeningHours>();

    public DbSet<Blackout> Blackouts => Set<Blackout>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.ApplyConfiguration(new ResourceTypeConfiguration());
        _ = modelBuilder.ApplyConfiguration(new ResourceConfiguration());
        _ = modelBuilder.ApplyConfiguration(new OpeningHoursConfiguration());
        _ = modelBuilder.ApplyConfiguration(new BlackoutConfiguration());
        _ = modelBuilder.ApplyConfiguration(new BookingConfiguration());
    }
}
