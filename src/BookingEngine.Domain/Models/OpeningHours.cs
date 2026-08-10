namespace BookingEngine.Domain.Models;

/// <summary>
/// A weekly recurring window during which a resource can be booked.
/// </summary>
/// <remarks>
/// Times are UTC wall clock: the whole service works in UTC and performs no timezone
/// conversion. A window never crosses midnight, because
/// <paramref name="StartTime" /> is required to be earlier than <paramref name="EndTime" />.
/// </remarks>
/// <param name="Id">Unique identifier of the opening hours entry (UUID v4).</param>
/// <param name="ResourceId">Reference to the resource these hours apply to.</param>
/// <param name="DayOfWeek">Day of the week, <c>"Sunday"</c> through <c>"Saturday"</c>.</param>
/// <param name="StartTime">UTC time the resource opens, e.g. <c>"09:00:00"</c>.</param>
/// <param name="EndTime">UTC time the resource closes, e.g. <c>"17:00:00"</c>.</param>
public sealed record OpeningHours(
    Guid Id,
    Guid ResourceId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime
);
