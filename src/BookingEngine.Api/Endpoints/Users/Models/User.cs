using BookingEngine.Infrastructure.Auth;

namespace BookingEngine.Api.Endpoints.Users.Models;

/// <summary>
/// Response representation of a user account.
/// </summary>
/// <param name="Id">Unique identifier of the user (UUID).</param>
/// <param name="Name">Given name, or <c>null</c> if the profile is not yet completed.</param>
/// <param name="Surname">Family name, or <c>null</c> if the profile is not yet completed.</param>
/// <param name="Email">Email address the account signs in with.</param>
/// <param name="Phone">Contact telephone number, or <c>null</c> if not provided.</param>
/// <param name="Roles">Roles the user holds, such as <c>"Admin"</c> or <c>"User"</c>.</param>
/// <param name="IsBlocked">
/// Whether the account is currently blocked. A blocked user cannot sign in or refresh, but an
/// access token issued before the block remains valid until it expires.
/// </param>
public sealed record User(
    Guid Id,
    string? Name,
    string? Surname,
    string? Email,
    string? Phone,
    IReadOnlyCollection<string> Roles,
    bool IsBlocked
)
{
    public User(ApplicationUser user, IReadOnlyCollection<string> roles, DateTimeOffset now)
        : this(
            user.Id,
            user.Name,
            user.Surname,
            user.Email,
            user.PhoneNumber,
            roles,
            user.LockoutEnd > now
        )
    { }
}
