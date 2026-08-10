namespace BookingEngine.Api.Endpoints.Resources.Models;

/// <summary>
/// Request body for a partial update (PATCH) of a resource.
/// Only non-null fields are applied; omitted or null fields retain their current values.
/// </summary>
/// <param name="TypeId">
/// New category reference. Must reference an existing resource type when provided.
/// </param>
/// <param name="Name">New display name. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="Description">New description. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="MinNotice">New minimum notice. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="MaxHorizon">New maximum horizon. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="SlotDuration">New slot length. Pass <c>null</c> or omit to keep the existing value.</param>
public sealed record ResourceWithPartialUpdate(
    Guid? TypeId,
    string? Name,
    string? Description,
    TimeSpan? MinNotice,
    TimeSpan? MaxHorizon,
    TimeSpan? SlotDuration
);
