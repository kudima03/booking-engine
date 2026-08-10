using BookingEngine.Domain.Availability;
using BookingEngine.Domain.Exceptions;
using BookingEngine.Domain.Models;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.ApplicationCore;

/// <summary>
/// Loads what a resource's booking grid depends on and computes its free slots.
/// </summary>
/// <remarks>
/// The arithmetic itself lives in <see cref="AvailabilityCalendar" />, which is pure and
/// knows nothing about persistence. This type exists only to fetch its inputs.
/// </remarks>
public sealed record AvailabilityService(BookingDbContext DbContext)
{
    /// <summary>
    /// Returns the free slots of a resource within the requested window.
    /// </summary>
    /// <param name="resourceId">Identifier of the resource to inspect.</param>
    /// <param name="from">Earliest instant of interest.</param>
    /// <param name="to">Latest instant of interest.</param>
    /// <param name="now">The current instant, used for notice and horizon limits.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>Free slots ordered by start.</returns>
    /// <exception cref="EntityNotFoundException">The resource does not exist.</exception>
    public async Task<IReadOnlyCollection<AvailabilitySlot>> SlotsAsync(
        Guid resourceId,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        AvailabilityCalendar calendar = await CalendarAsync(
            resourceId,
            from,
            to,
            cancellationToken
        );

        return calendar.Slots(from, to, now);
    }

    /// <summary>
    /// Builds the calendar of a resource over the given window.
    /// </summary>
    /// <remarks>
    /// Blackouts and bookings are narrowed to those overlapping the window, so the calendar
    /// never carries the resource's whole history. Reads are untracked, which does not weaken
    /// serializable isolation: PostgreSQL takes predicate locks regardless.
    /// </remarks>
    /// <param name="resourceId">Identifier of the resource to inspect.</param>
    /// <param name="from">Earliest instant of interest.</param>
    /// <param name="to">Latest instant of interest.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A calendar over the loaded data.</returns>
    /// <exception cref="EntityNotFoundException">The resource does not exist.</exception>
    public async Task<AvailabilityCalendar> CalendarAsync(
        Guid resourceId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken
    )
    {
        Resource resource =
            await DbContext
                .Resources.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == resourceId, cancellationToken)
            ?? throw new EntityNotFoundException();

        // Widen by the horizon so a window clamped later still sees the blackouts and
        // bookings that could suppress its slots.
        DateTimeOffset windowStart = from;
        DateTimeOffset windowEnd = to;

        List<OpeningHours> openingHours = await DbContext
            .OpeningHours.AsNoTracking()
            .Where(x => x.ResourceId == resourceId)
            .ToListAsync(cancellationToken);

        List<Blackout> blackouts = await DbContext
            .Blackouts.AsNoTracking()
            .Where(x =>
                (x.ResourceId == resourceId)
                && (x.StartsAt < windowEnd)
                && (windowStart < x.EndsAt)
            )
            .ToListAsync(cancellationToken);

        List<Booking> bookings = await DbContext
            .Bookings.AsNoTracking()
            .Where(x =>
                (x.ResourceId == resourceId)
                && (x.Status == BookingStatus.Confirmed)
                && (x.StartsAt < windowEnd)
                && (windowStart < x.EndsAt)
            )
            .ToListAsync(cancellationToken);

        return new AvailabilityCalendar(resource, openingHours, blackouts, bookings);
    }
}
