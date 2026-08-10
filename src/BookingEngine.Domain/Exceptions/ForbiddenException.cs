namespace BookingEngine.Domain.Exceptions;

/// <summary>
/// Thrown when the caller is authenticated but not permitted to act on the target entity,
/// such as a user touching another user's booking. Surfaces as <c>403 Forbidden</c>.
/// </summary>
public class ForbiddenException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException);
