namespace BookingEngine.Domain.Exceptions;

/// <summary>
/// Thrown when a booking cannot be placed because the requested period is not free or not on
/// the resource's grid. Surfaces as <c>409 Conflict</c>.
/// </summary>
public class BookingConflictException(
    string? message = null,
    Exception? innerException = null
) : Exception(message, innerException);
