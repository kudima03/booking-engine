using BookingEngine.Domain.Models;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingEngine.Infrastructure.Tests;

[Collection(nameof(BookingDbTestSet))]
public sealed record BookingDbContextTests
{
    private readonly BookingDbFixture _fixture;

    public BookingDbContextTests(BookingDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Resource> NewResourceAsync(BookingDbContext dbContext)
    {
        ResourceType type = new(Guid.NewGuid(), Guid.NewGuid().ToString(), "A category");
        Resource resource = new(
            Guid.NewGuid(),
            type.Id,
            "Room A",
            "A room",
            TimeSpan.FromHours(1),
            TimeSpan.FromDays(30),
            TimeSpan.FromMinutes(30)
        );

        _ = dbContext.ResourceTypes.Add(type);
        _ = dbContext.Resources.Add(resource);
        _ = await dbContext.SaveChangesAsync();

        return resource;
    }

    [Fact]
    public async Task ShouldRoundTripResourceDurationsAndIdentifiers()
    {
        await using BookingDbContext writeContext = _fixture.NewContext();
        Resource resource = await NewResourceAsync(writeContext);

        await using BookingDbContext readContext = _fixture.NewContext();
        Resource? stored = await readContext.Resources.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == resource.Id);

        Assert.Equal(resource, stored);
    }

    [Fact]
    public async Task ShouldRoundTripOpeningHoursAsLocalWallClock()
    {
        await using BookingDbContext writeContext = _fixture.NewContext();
        Resource resource = await NewResourceAsync(writeContext);

        OpeningHours hours = new(
            Guid.NewGuid(),
            resource.Id,
            DayOfWeek.Thursday,
            new TimeOnly(9, 0),
            new TimeOnly(17, 30)
        );

        _ = writeContext.OpeningHours.Add(hours);
        _ = await writeContext.SaveChangesAsync();

        await using BookingDbContext readContext = _fixture.NewContext();
        OpeningHours? stored = await readContext.OpeningHours.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == hours.Id);

        Assert.Equal(hours, stored);
    }

    [Fact]
    public async Task ShouldNormalizeNonUtcOffsetsToUtcOnWrite()
    {
        await using BookingDbContext writeContext = _fixture.NewContext();
        Resource resource = await NewResourceAsync(writeContext);

        // A caller supplying +02:00 must not break the timestamptz parameter, and
        // must read back as the same instant expressed in UTC.
        DateTimeOffset startsAt = new(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(2));
        Blackout blackout = new(
            Guid.NewGuid(),
            resource.Id,
            startsAt,
            startsAt.AddHours(1),
            "Maintenance"
        );

        _ = writeContext.Blackouts.Add(blackout);
        _ = await writeContext.SaveChangesAsync();

        await using BookingDbContext readContext = _fixture.NewContext();
        Blackout stored = await readContext.Blackouts.AsNoTracking()
            .SingleAsync(x => x.Id == blackout.Id);

        Assert.Equal(TimeSpan.Zero, stored.StartsAt.Offset);
        Assert.Equal(startsAt.ToUniversalTime(), stored.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), stored.StartsAt);
    }

    [Fact]
    public async Task ShouldPersistBookingStatusAsText()
    {
        await using BookingDbContext writeContext = _fixture.NewContext();
        Resource resource = await NewResourceAsync(writeContext);

        Booking booking = new(
            Guid.NewGuid(),
            resource.Id,
            Guid.NewGuid(),
            new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero),
            BookingStatus.Confirmed
        );

        _ = writeContext.Bookings.Add(booking);
        _ = await writeContext.SaveChangesAsync();

        await using BookingDbContext readContext = _fixture.NewContext();
        string status = await readContext
            .Database.SqlQuery<string>(
                $"""SELECT "Status" AS "Value" FROM "Bookings" WHERE "Id" = {booking.Id}"""
            )
            .SingleAsync();

        Assert.Equal("Confirmed", status);
    }

    [Fact]
    public async Task ShouldRejectResourceReferencingMissingResourceType()
    {
        await using BookingDbContext dbContext = _fixture.NewContext();

        _ = dbContext.Resources.Add(
            new Resource(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Orphan",
                string.Empty,
                TimeSpan.Zero,
                TimeSpan.FromDays(1),
                TimeSpan.FromMinutes(30)
            )
        );

        _ = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ShouldRestrictResourceDeletionWhenBookingsExist()
    {
        await using BookingDbContext dbContext = _fixture.NewContext();
        Resource resource = await NewResourceAsync(dbContext);

        Booking booking = new(
            Guid.NewGuid(),
            resource.Id,
            Guid.NewGuid(),
            new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 9, 30, 0, TimeSpan.Zero),
            BookingStatus.Confirmed
        );

        _ = dbContext.Bookings.Add(booking);
        _ = await dbContext.SaveChangesAsync();

        _ = await Assert.ThrowsAsync<PostgresException>(
            () => dbContext.Resources.Where(x => x.Id == resource.Id).ExecuteDeleteAsync()
        );

        Assert.True(await dbContext.Bookings.AnyAsync(x => x.Id == booking.Id));
    }

    [Fact]
    public async Task ShouldRejectDuplicateResourceTypeNames()
    {
        await using BookingDbContext dbContext = _fixture.NewContext();
        string name = Guid.NewGuid().ToString();

        _ = dbContext.ResourceTypes.Add(new ResourceType(Guid.NewGuid(), name, string.Empty));
        _ = await dbContext.SaveChangesAsync();

        _ = dbContext.ResourceTypes.Add(new ResourceType(Guid.NewGuid(), name, string.Empty));

        _ = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ShouldApplyEveryMigration()
    {
        await using BookingDbContext dbContext = _fixture.NewContext();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await dbContext.Database.GetAppliedMigrationsAsync());
    }
}
