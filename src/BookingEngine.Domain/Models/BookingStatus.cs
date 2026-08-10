namespace BookingEngine.Domain.Models;

/// <summary>
/// Lifecycle state of a booking.
/// </summary>
public enum BookingStatus
{
    /// <summary>
    /// The booking holds its period; the slot is unavailable to everyone else.
    /// </summary>
    Confirmed,

    /// <summary>
    /// The booking has been withdrawn; its period is available again.
    /// </summary>
    Cancelled,
}
