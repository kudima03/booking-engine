namespace BookingEngine.Api.Endpoints.Blackouts.Models;

/// <summary>
/// Request body for a partial update (PATCH) of a blackout.
/// Only non-null fields are applied; omitted or null fields retain their current values.
/// </summary>
/// <param name="ResourceId">New resource reference. Must reference an existing resource when provided.</param>
/// <param name="StartsAt">New start instant. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="EndsAt">New end instant. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="Reason">New explanation. Pass <c>null</c> or omit to keep the existing value.</param>
public sealed record BlackoutWithPartialUpdate(
    Guid? ResourceId,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Reason
);
