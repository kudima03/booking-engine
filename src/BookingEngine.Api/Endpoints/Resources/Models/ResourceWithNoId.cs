namespace BookingEngine.Api.Endpoints.Resources.Models;

/// <summary>
/// Request body for creating or fully replacing a resource.
/// </summary>
/// <param name="TypeId">
/// Reference to the category this resource belongs to. Must reference an existing resource
/// type; returns 400 Bad Request otherwise.
/// </param>
/// <param name="Name">Short display name of the resource.</param>
/// <param name="Description">Longer description of the resource.</param>
/// <param name="MinNotice">
/// How far in advance a booking must be made, e.g. <c>"01:00:00"</c>. Must not be negative.
/// </param>
/// <param name="MaxHorizon">
/// How far into the future bookings are accepted, e.g. <c>"30.00:00:00"</c>. Must be greater
/// than <paramref name="MinNotice" />.
/// </param>
/// <param name="SlotDuration">
/// Length of a single bookable slot, e.g. <c>"00:30:00"</c>. Must be positive.
/// </param>
public sealed record ResourceWithNoId(
    Guid TypeId,
    string Name,
    string Description,
    TimeSpan MinNotice,
    TimeSpan MaxHorizon,
    TimeSpan SlotDuration
);
