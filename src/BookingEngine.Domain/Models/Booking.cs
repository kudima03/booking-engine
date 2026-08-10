namespace BookingEngine.Domain.Models;

/// <summary>
/// A reservation of one slot on a resource, held by a user.
/// </summary>
/// <remarks>
/// <paramref name="UserId" /> refers to a user in the separate authentication database, so it
/// is a plain identifier rather than a foreign key.
/// </remarks>
/// <param name="Id">Unique identifier of the booking (UUID v4).</param>
/// <param name="ResourceId">Reference to the booked resource.</param>
/// <param name="UserId">Identifier of the user the booking belongs to.</param>
/// <param name="StartsAt">UTC instant the booking begins, inclusive.</param>
/// <param name="EndsAt">UTC instant the booking ends, exclusive.</param>
/// <param name="Status">Whether the booking currently holds its period.</param>
public sealed record Booking(
    Guid Id,
    Guid ResourceId,
    Guid UserId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    BookingStatus Status
);
