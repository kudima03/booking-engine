namespace BookingEngine.Infrastructure.Auth;

/// <summary>
/// The roles this service recognises.
/// </summary>
public static class KnownRoles
{
    /// <summary>
    /// Manages the catalogue and every user's bookings.
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Manages only their own bookings. Granted automatically on registration.
    /// </summary>
    public const string User = "User";

    /// <summary>
    /// Every role, in the order they should be seeded.
    /// </summary>
    public static IReadOnlyCollection<string> All => [Admin, User];
}
