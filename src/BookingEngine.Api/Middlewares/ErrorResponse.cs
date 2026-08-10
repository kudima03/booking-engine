namespace BookingEngine.Api.Middlewares;

/// <summary>
/// Standard error envelope returned by all API endpoints on failure.
/// </summary>
/// <param name="TraceId">
/// Correlation identifier for this request, matching the <c>traceparent</c> / <c>Activity.Id</c> value.
/// Use this value to locate the corresponding log entries.
/// </param>
/// <param name="Message">Human-readable description of the error suitable for display to an API consumer.</param>
/// <param name="Detail">
/// Extended diagnostic detail such as an exception stack trace.
/// Only populated in the Development environment; <c>null</c> in production.
/// </param>
public sealed record ErrorResponse(string TraceId, string Message, string? Detail);
