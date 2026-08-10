using BookingEngine.Domain.Models;

namespace BookingEngine.Api.Endpoints.Bookings.Models;

/// <summary>
/// Request body for a partial update (PATCH) of a booking.
/// Only non-null fields are applied; omitted or null fields retain their current values.
/// </summary>
/// <remarks>
/// This is also how a booking is cancelled: send <c>{ "status": "Cancelled" }</c>. Every
/// booking mutation goes through the same transactional path, so there is no separate
/// cancellation route to keep in step.
/// </remarks>
/// <param name="StartsAt">New start instant. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="EndsAt">New end instant. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="Status">
/// New status, <c>Confirmed</c> or <c>Cancelled</c>. Moving a booking back to
/// <c>Confirmed</c> re-checks availability and may fail with 409 Conflict.
/// </param>
public sealed record BookingWithPartialUpdate(
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    BookingStatus? Status
);
