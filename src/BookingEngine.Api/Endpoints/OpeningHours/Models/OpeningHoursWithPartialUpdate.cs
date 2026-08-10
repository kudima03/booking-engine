namespace BookingEngine.Api.Endpoints.OpeningHours.Models;

/// <summary>
/// Request body for a partial update (PATCH) of a weekly opening window.
/// Only non-null fields are applied; omitted or null fields retain their current values.
/// </summary>
/// <param name="ResourceId">New resource reference. Must reference an existing resource when provided.</param>
/// <param name="DayOfWeek">New day of the week. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="StartTime">New opening time. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="EndTime">New closing time. Pass <c>null</c> or omit to keep the existing value.</param>
public sealed record OpeningHoursWithPartialUpdate(
    Guid? ResourceId,
    DayOfWeek? DayOfWeek,
    TimeOnly? StartTime,
    TimeOnly? EndTime
);
