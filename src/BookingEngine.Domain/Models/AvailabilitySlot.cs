namespace BookingEngine.Domain.Models;

/// <summary>
/// A period on a resource's booking grid that is free and may be booked.
/// </summary>
/// <param name="StartsAt">UTC instant the slot begins, inclusive.</param>
/// <param name="EndsAt">UTC instant the slot ends, exclusive.</param>
public sealed record AvailabilitySlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt);
