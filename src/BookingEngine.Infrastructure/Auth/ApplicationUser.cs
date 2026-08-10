using Microsoft.AspNetCore.Identity;

namespace BookingEngine.Infrastructure.Auth;

/// <summary>
/// A person who can sign in and hold bookings.
/// </summary>
/// <remarks>
/// <see cref="IdentityUser{TKey}" /> already carries the email, phone number and lockout
/// state, so only the two name fields are added here. Both are nullable because the standard
/// Identity registration endpoint accepts nothing but an email address and a password; they
/// are filled in afterwards through the profile endpoint.
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Given name of the user.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Family name of the user.
    /// </summary>
    public string? Surname { get; set; }
}
