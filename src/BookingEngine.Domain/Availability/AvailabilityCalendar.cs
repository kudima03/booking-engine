using BookingEngine.Domain.Models;

namespace BookingEngine.Domain.Availability;

/// <summary>
/// Computes which slots of a resource are free, from data already loaded by the caller.
/// </summary>
/// <remarks>
/// <para>
/// This is a pure value object: it reads no database, holds no clock and takes no dependencies.
/// The current instant is passed in explicitly, so the whole calculation is deterministic and
/// unit-testable.
/// </para>
/// <para>
/// Slots are produced by slicing each opening block into a fixed grid anchored at the block's
/// start, then discarding slots that overlap a blackout or a confirmed booking. Slicing before
/// filtering is what keeps the grid stable: subtracting busy periods first would re-anchor the
/// grid to whatever time a blackout happened to end.
/// </para>
/// <para>
/// Everything is UTC. Opening hours are UTC wall clock; blackouts and bookings are UTC instants.
/// </para>
/// </remarks>
/// <param name="Resource">The resource whose grid is being computed.</param>
/// <param name="OpeningHours">Weekly windows of the resource. Entries for other resources are ignored.</param>
/// <param name="Blackouts">One-off unavailable periods overlapping the query window.</param>
/// <param name="ConfirmedBookings">Bookings currently holding a period on the resource.</param>
public sealed record AvailabilityCalendar(
    Resource Resource,
    IReadOnlyCollection<OpeningHours> OpeningHours,
    IReadOnlyCollection<Blackout> Blackouts,
    IReadOnlyCollection<Booking> ConfirmedBookings
)
{
    /// <summary>
    /// Returns every free slot that starts and ends within the requested window.
    /// </summary>
    /// <param name="from">Earliest UTC instant of interest.</param>
    /// <param name="to">Latest UTC instant of interest.</param>
    /// <param name="now">The current UTC instant, used for notice and horizon limits.</param>
    /// <returns>Free slots ordered by start, or an empty collection if none exist.</returns>
    public IReadOnlyCollection<AvailabilitySlot> Slots(
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now
    )
    {
        DateTimeOffset windowStart = Max(from, now + Resource.MinNotice);
        DateTimeOffset windowEnd = Min(to, now + Resource.MaxHorizon);

        if ((windowStart >= windowEnd) || (Resource.SlotDuration <= TimeSpan.Zero))
        {
            return [];
        }

        List<AvailabilitySlot> slots = [];

        for (
            DateOnly date = DateOnly.FromDateTime(windowStart.UtcDateTime);
            date <= DateOnly.FromDateTime(windowEnd.UtcDateTime);
            date = date.AddDays(1)
        )
        {
            foreach (OpeningHours hours in OpeningHours)
            {
                if ((hours.DayOfWeek != date.DayOfWeek) || (hours.StartTime >= hours.EndTime))
                {
                    continue;
                }

                AppendBlockSlots(slots, date, hours, windowStart, windowEnd);
            }
        }

        return [.. slots.DistinctBy(x => x.StartsAt).OrderBy(x => x.StartsAt)];
    }

    /// <summary>
    /// Determines whether the exact period requested is one of the free slots.
    /// </summary>
    /// <param name="startsAt">UTC instant the requested period begins.</param>
    /// <param name="endsAt">UTC instant the requested period ends.</param>
    /// <param name="now">The current UTC instant, used for notice and horizon limits.</param>
    /// <returns>
    /// <c>true</c> when the period is on the grid, within the notice and horizon limits, and
    /// free; otherwise <c>false</c>.
    /// </returns>
    public bool IsAvailable(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset now
    )
    {
        return Slots(startsAt, endsAt, now)
            .Any(x => (x.StartsAt == startsAt) && (x.EndsAt == endsAt));
    }

    /// <summary>
    /// Returns a calendar identical to this one but ignoring the given booking.
    /// </summary>
    /// <remarks>
    /// Used when rescheduling, so that a booking's own current period does not make its new
    /// period look occupied.
    /// </remarks>
    /// <param name="bookingId">Identifier of the booking to disregard.</param>
    /// <returns>A calendar without that booking.</returns>
    public AvailabilityCalendar Excluding(Guid bookingId)
    {
        return this with
        {
            ConfirmedBookings = [.. ConfirmedBookings.Where(x => x.Id != bookingId)],
        };
    }

    private void AppendBlockSlots(
        List<AvailabilitySlot> slots,
        DateOnly date,
        OpeningHours hours,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd
    )
    {
        DateTimeOffset opensAt = new(date.ToDateTime(hours.StartTime), TimeSpan.Zero);
        DateTimeOffset closesAt = new(date.ToDateTime(hours.EndTime), TimeSpan.Zero);

        for (
            DateTimeOffset cursor = opensAt;
            (cursor + Resource.SlotDuration) <= closesAt;
            cursor += Resource.SlotDuration
        )
        {
            DateTimeOffset slotEnd = cursor + Resource.SlotDuration;

            if ((cursor < windowStart) || (slotEnd > windowEnd))
            {
                continue;
            }

            if (!IsBusy(cursor, slotEnd))
            {
                slots.Add(new AvailabilitySlot(cursor, slotEnd));
            }
        }
    }

    private bool IsBusy(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        bool blacked = Blackouts.Any(x =>
            (startsAt < x.EndsAt) && (x.StartsAt < endsAt)
        );

        return blacked
            || ConfirmedBookings.Any(x =>
                (x.Status == BookingStatus.Confirmed)
                && (startsAt < x.EndsAt)
                && (x.StartsAt < endsAt)
            );
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }
}
