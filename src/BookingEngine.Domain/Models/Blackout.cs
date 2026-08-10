namespace BookingEngine.Domain.Models;

/// <summary>
/// A one-off period during which a resource is unavailable despite its opening hours.
/// </summary>
/// <param name="Id">Unique identifier of the blackout (UUID v4).</param>
/// <param name="ResourceId">Reference to the resource that is unavailable.</param>
/// <param name="StartsAt">UTC instant the blackout begins, inclusive.</param>
/// <param name="EndsAt">UTC instant the blackout ends, exclusive.</param>
/// <param name="Reason">Human-readable explanation, e.g. <c>"Annual maintenance"</c>.</param>
public sealed record Blackout(
    Guid Id,
    Guid ResourceId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Reason
);
