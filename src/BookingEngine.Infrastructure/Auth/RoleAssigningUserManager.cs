using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingEngine.Infrastructure.Auth;

/// <summary>
/// A <see cref="UserManager{TUser}" /> that grants <see cref="KnownRoles.User" /> to every
/// account it creates.
/// </summary>
/// <remarks>
/// The standard Identity registration endpoint calls <c>CreateAsync</c> and offers no hook
/// afterwards, so overriding it here is the shortest way to make self-registered accounts
/// arrive with a role already attached.
/// </remarks>
public sealed class RoleAssigningUserManager(
    IUserStore<ApplicationUser> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<ApplicationUser>> logger
)
    : UserManager<ApplicationUser>(
        store,
        optionsAccessor,
        passwordHasher,
        userValidators,
        passwordValidators,
        keyNormalizer,
        errors,
        services,
        logger
    )
{
    public override async Task<IdentityResult> CreateAsync(
        ApplicationUser user,
        string password
    )
    {
        IdentityResult result = await base.CreateAsync(user, password);

        return !result.Succeeded ? result : await AddToRoleAsync(user, KnownRoles.User);
    }
}
