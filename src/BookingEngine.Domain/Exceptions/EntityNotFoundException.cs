namespace BookingEngine.Domain.Exceptions;

/// <summary>
/// Thrown when a requested entity does not exist. Surfaces as <c>404 Not Found</c>.
/// </summary>
public class EntityNotFoundException(
    string? message = null,
    Exception? innerException = null
) : Exception(message, innerException);
