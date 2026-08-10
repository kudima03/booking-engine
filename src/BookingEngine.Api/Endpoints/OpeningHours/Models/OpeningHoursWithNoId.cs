namespace BookingEngine.Api.Endpoints.OpeningHours.Models;

/// <summary>
/// Request body for creating or fully replacing a weekly opening window.
/// </summary>
/// <param name="ResourceId">
/// Reference to the resource these hours apply to. Must reference an existing resource;
/// returns 404 Not Found otherwise.
/// </param>
/// <param name="DayOfWeek">Day of the week, <c>0</c> for Sunday through <c>6</c> for Saturday.</param>
/// <param name="StartTime">
/// UTC time the resource opens, e.g. <c>"09:00:00"</c>. Must be earlier than
/// <paramref name="EndTime" />: a window may not cross midnight.
/// </param>
/// <param name="EndTime">UTC time the resource closes, e.g. <c>"17:00:00"</c>.</param>
public sealed record OpeningHoursWithNoId(
    Guid ResourceId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime
);
