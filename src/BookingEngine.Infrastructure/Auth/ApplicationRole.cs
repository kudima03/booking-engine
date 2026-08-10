using Microsoft.AspNetCore.Identity;

namespace BookingEngine.Infrastructure.Auth;

/// <summary>
/// A role a user can hold. See <see cref="KnownRoles" /> for the roles this service uses.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>;
