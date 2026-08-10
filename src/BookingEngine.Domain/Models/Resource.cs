namespace BookingEngine.Domain.Models;

/// <summary>
/// A bookable resource.
/// </summary>
/// <remarks>
/// The three durations together define the booking grid: a resource is bookable only on
/// <paramref name="SlotDuration" /> boundaries anchored at the start of each opening block,
/// no sooner than <paramref name="MinNotice" /> from now, and no later than
/// <paramref name="MaxHorizon" /> from now.
/// </remarks>
/// <param name="Id">Unique identifier of the resource (UUID v4).</param>
/// <param name="TypeId">Reference to the category this resource belongs to.</param>
/// <param name="Name">Short display name of the resource.</param>
/// <param name="Description">Longer description of the resource.</param>
/// <param name="MinNotice">
/// How far in advance a booking must be made, e.g. <c>"01:00:00"</c> for one hour.
/// </param>
/// <param name="MaxHorizon">
/// How far into the future bookings are accepted, e.g. <c>"30.00:00:00"</c> for thirty days.
/// </param>
/// <param name="SlotDuration">Length of a single bookable slot, e.g. <c>"00:30:00"</c>.</param>
public sealed record Resource(
    Guid Id,
    Guid TypeId,
    string Name,
    string Description,
    TimeSpan MinNotice,
    TimeSpan MaxHorizon,
    TimeSpan SlotDuration
);
