namespace BookingEngine.Api.Endpoints.Blackouts.Models;

/// <summary>
/// Request body for creating or fully replacing a blackout.
/// </summary>
/// <param name="ResourceId">
/// Reference to the resource that is unavailable. Must reference an existing resource;
/// returns 404 Not Found otherwise.
/// </param>
/// <param name="StartsAt">
/// Instant the blackout begins, inclusive. Stored as UTC; a non-UTC offset is converted.
/// </param>
/// <param name="EndsAt">
/// Instant the blackout ends, exclusive. Must be later than <paramref name="StartsAt" />.
/// </param>
/// <param name="Reason">Human-readable explanation, e.g. <c>"Annual maintenance"</c>.</param>
public sealed record BlackoutWithNoId(
    Guid ResourceId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Reason
);
