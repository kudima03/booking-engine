using System.Data;
using BookingEngine.Domain.Availability;
using BookingEngine.Domain.Exceptions;
using BookingEngine.Domain.Models;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookingEngine.ApplicationCore;

/// <summary>
/// Places and changes bookings without ever letting two of them overlap.
/// </summary>
/// <remarks>
/// <para>
/// Every mutation runs the whole read-check-write — load the calendar, confirm the period is
/// free, insert or update — inside a single <c>SERIALIZABLE</c> transaction. Two callers
/// racing for the same slot form a read/write conflict, and PostgreSQL aborts one of them.
/// </para>
/// <para>
/// The transaction is opened inside the execution strategy rather than around it. That is
/// mandatory: <c>EnableRetryOnFailure</c> installs a retrying strategy, and EF refuses a
/// user transaction under one unless the strategy owns it. It is also what we want, because
/// the retry then re-runs the entire check rather than just the final save.
/// </para>
/// </remarks>
public sealed record BookingService(
    BookingDbContext DbContext,
    AvailabilityService Availability
)
{
    /// <summary>
    /// Confirms a booking for the requested period.
    /// </summary>
    /// <param name="candidate">
    /// The booking to place. Its status is ignored; a created booking is always confirmed.
    /// </param>
    /// <param name="now">The current instant, used for notice and horizon limits.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The confirmed booking.</returns>
    /// <exception cref="EntityNotFoundException">The resource does not exist.</exception>
    /// <exception cref="BookingConflictException">The period is not an available slot.</exception>
    public Task<Booking> CreateAsync(
        Booking candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        return InTransactionAsync(
            async () =>
            {
                await RequireAvailableAsync(
                    candidate with
                    {
                        Status = BookingStatus.Confirmed,
                    },
                    excluding: null,
                    now,
                    cancellationToken
                );

                Booking confirmed = candidate with { Status = BookingStatus.Confirmed };

                _ = DbContext.Bookings.Add(confirmed);
                _ = await DbContext.SaveChangesAsync(cancellationToken);

                return confirmed;
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Applies a change to an existing booking, re-checking availability when it stays
    /// confirmed.
    /// </summary>
    /// <remarks>
    /// A booking never conflicts with itself: the period it currently holds is excluded from
    /// the check, so changing only a customer detail, or moving a booking onto a slot that
    /// overlaps its old one, both succeed. Cancelling skips the check entirely.
    /// </remarks>
    /// <param name="id">Identifier of the booking to change.</param>
    /// <param name="change">Produces the new state from the stored one.</param>
    /// <param name="now">The current instant, used for notice and horizon limits.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated booking.</returns>
    /// <exception cref="EntityNotFoundException">The booking or its resource does not exist.</exception>
    /// <exception cref="BookingConflictException">The new period is not an available slot.</exception>
    public Task<Booking> ChangeAsync(
        Guid id,
        Func<Booking, Booking> change,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        return InTransactionAsync(
            async () =>
            {
                Booking existing =
                    await DbContext.Bookings.AsNoTracking()
                        .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                    ?? throw new EntityNotFoundException();

                Booking updated = change(existing) with { Id = existing.Id };

                if (updated.Status == BookingStatus.Confirmed)
                {
                    await RequireAvailableAsync(
                        updated,
                        excluding: existing.Id,
                        now,
                        cancellationToken
                    );
                }

                _ = DbContext.Bookings.Update(updated);
                _ = await DbContext.SaveChangesAsync(cancellationToken);

                return updated;
            },
            cancellationToken
        );
    }

    private async Task RequireAvailableAsync(
        Booking booking,
        Guid? excluding,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        AvailabilityCalendar calendar = await Availability.CalendarAsync(
            booking.ResourceId,
            booking.StartsAt,
            booking.EndsAt,
            cancellationToken
        );

        if (excluding is not null)
        {
            calendar = calendar.Excluding(excluding.Value);
        }

        if (!calendar.IsAvailable(booking.StartsAt, booking.EndsAt, now))
        {
            throw new BookingConflictException(
                "The requested period is not an available slot."
            );
        }
    }

    private Task<TResult> InTransactionAsync<TResult>(
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        IExecutionStrategy strategy = DbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            // A retried attempt would otherwise re-send the previous attempt's pending
            // insert and write the booking twice.
            DbContext.ChangeTracker.Clear();

            await using IDbContextTransaction transaction =
                await DbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken
                );

            TResult result = await operation();

            await transaction.CommitAsync(cancellationToken);

            return result;
        });
    }
}
