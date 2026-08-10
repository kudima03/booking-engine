namespace BookingEngine.Api.Endpoints.Bookings.Models;

/// <summary>
/// Request body for creating a booking.
/// </summary>
/// <remarks>
/// The period must be exactly one of the slots returned by
/// <c>GET /resources/{resourceId}/availability</c>; anything else is rejected with
/// 409 Conflict. The status is assigned by the server, never by the caller.
/// </remarks>
/// <param name="ResourceId">Reference to the resource to book.</param>
/// <param name="UserId">
/// The user the booking is for. Administrators may book on behalf of any user; for everyone
/// else this field is ignored and the booking is placed for the caller.
/// </param>
/// <param name="StartsAt">Instant the booking begins, inclusive. Stored as UTC.</param>
/// <param name="EndsAt">Instant the booking ends, exclusive. Stored as UTC.</param>
public sealed record BookingWithNoId(
    Guid ResourceId,
    Guid? UserId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt
);
