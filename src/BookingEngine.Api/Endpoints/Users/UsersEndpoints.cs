using System.Security.Claims;
using BookingEngine.Api.Endpoints.Users.Models;
using BookingEngine.Api.Middlewares;
using BookingEngine.Domain.Exceptions;
using BookingEngine.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.Api.Endpoints.Users;

/// <summary>
/// Manages user accounts, their profiles, their roles and whether they are blocked.
/// </summary>
/// <remarks>
/// Accounts are created through <c>POST /auth/register</c>, not here. Everything on this
/// controller requires an administrator except the two <c>current</c> actions, which act on
/// the caller's own account.
/// </remarks>
[ApiController]
[Route("users")]
[Tags("Users")]
[Authorize]
public sealed class UsersEndpoints(
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider
) : ControllerBase
{
    /// <summary>
    /// Returns every user account.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>All user accounts, each with the roles it holds.</returns>
    /// <response code="200">Users returned successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType<IEnumerable<User>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IReadOnlyCollection<User>> All(CancellationToken cancellationToken)
    {
        List<ApplicationUser> users = await userManager
            .Users.AsNoTracking()
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);

        List<User> result = [];

        foreach (ApplicationUser user in users)
        {
            result.Add(await MaterializeAsync(user));
        }

        return result;
    }

    /// <summary>
    /// Returns the account of the calling user.
    /// </summary>
    /// <returns>The caller's own account.</returns>
    /// <response code="200">Account returned successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="404">The token refers to an account that no longer exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("current")]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<User> Current()
    {
        return await MaterializeAsync(await RequireAsync(CallerId()));
    }

    /// <summary>
    /// Updates the profile of the calling user.
    /// </summary>
    /// <remarks>
    /// The standard registration endpoint accepts only an email address and a password, so
    /// this is where a user supplies their name, surname and telephone number.
    /// </remarks>
    /// <param name="model">The fields to change. Null fields are left untouched.</param>
    /// <returns>The updated account.</returns>
    /// <response code="200">Profile updated successfully.</response>
    /// <response code="400">The submitted profile was rejected.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="404">The token refers to an account that no longer exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPatch("current")]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<User> PatchCurrent([FromBody] UserWithPartialUpdate model)
    {
        return await ApplyProfileAsync(await RequireAsync(CallerId()), model);
    }

    /// <summary>
    /// Returns a single user account by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier (UUID) of the user to retrieve.</param>
    /// <returns>The user matching the provided identifier.</returns>
    /// <response code="200">User found and returned successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No user exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<User> ById(Guid id)
    {
        return await MaterializeAsync(await RequireAsync(id));
    }

    /// <summary>
    /// Updates another user's profile.
    /// </summary>
    /// <param name="id">The unique identifier (UUID) of the user to update.</param>
    /// <param name="model">The fields to change. Null fields are left untouched.</param>
    /// <returns>The updated account.</returns>
    /// <response code="200">Profile updated successfully.</response>
    /// <response code="400">The submitted profile was rejected.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No user exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType<User>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<User> Patch(Guid id, [FromBody] UserWithPartialUpdate model)
    {
        return await ApplyProfileAsync(await RequireAsync(id), model);
    }

    /// <summary>
    /// Deletes the specified user account.
    /// </summary>
    /// <remarks>
    /// Bookings held by the user are not removed, because they live in a different database.
    /// </remarks>
    /// <param name="id">The unique identifier (UUID) of the user to delete.</param>
    /// <response code="200">User deleted successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No user exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task Delete(Guid id)
    {
        Require(await userManager.DeleteAsync(await RequireAsync(id)));
    }

    /// <summary>
    /// Blocks the specified user.
    /// </summary>
    /// <remarks>
    /// A blocked user cannot sign in, and their security stamp is refreshed so that refreshing
    /// an existing session fails too. An access token issued before the block stays valid until
    /// it expires, which is why access tokens are short-lived.
    /// </remarks>
    /// <param name="userId">The unique identifier (UUID) of the user to block.</param>
    /// <response code="200">User blocked successfully.</response>
    /// <response code="400">The user could not be blocked.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No user exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost("{userId:guid}/block")]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task Block(Guid userId)
    {
        ApplicationUser user = await RequireAsync(userId);

        Require(await userManager.SetLockoutEnabledAsync(user, true));
        Require(await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
        Require(await userManager.UpdateSecurityStampAsync(user));
    }

    /// <summary>
    /// Unblocks the specified user.
    /// </summary>
    /// <param name="userId">The unique identifier (UUID) of the user to unblock.</param>
    /// <response code="200">User unblocked successfully (or was not blocked).</response>
    /// <response code="400">The user could not be unblocked.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No user exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete("{userId:guid}/block")]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task Unblock(Guid userId)
    {
        Require(await userManager.SetLockoutEndDateAsync(await RequireAsync(userId), null));
    }

    /// <summary>
    /// Grants a role to the specified user.
    /// </summary>
    /// <remarks>
    /// If the user already holds the role, the operation still succeeds (idempotent grant).
    /// </remarks>
    /// <param name="userId">The unique identifier (UUID) of the user.</param>
    /// <param name="role">The role to grant, either <c>Admin</c> or <c>User</c>.</param>
    /// <response code="200">Role granted successfully (or already held).</response>
    /// <response code="400">The role is not one this service recognises.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No user exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost("{userId:guid}/roles/{role}")]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task AddRole(Guid userId, string role)
    {
        ApplicationUser user = await RequireAsync(userId);

        if (!await userManager.IsInRoleAsync(user, RequireKnown(role)))
        {
            Require(await userManager.AddToRoleAsync(user, role));
        }
    }

    /// <summary>
    /// Revokes a role from the specified user.
    /// </summary>
    /// <remarks>
    /// If the user does not hold the role, the operation still succeeds (idempotent revoke).
    /// </remarks>
    /// <param name="userId">The unique identifier (UUID) of the user.</param>
    /// <param name="role">The role to revoke, either <c>Admin</c> or <c>User</c>.</param>
    /// <response code="200">Role revoked successfully (or was not held).</response>
    /// <response code="400">The role is not one this service recognises.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No user exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete("{userId:guid}/roles/{role}")]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task RemoveRole(Guid userId, string role)
    {
        ApplicationUser user = await RequireAsync(userId);

        if (await userManager.IsInRoleAsync(user, RequireKnown(role)))
        {
            Require(await userManager.RemoveFromRoleAsync(user, role));
        }
    }

    private Guid CallerId()
    {
        string? id = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(id, out Guid parsed)
            ? parsed
            : throw new EntityNotFoundException();
    }

    private async Task<ApplicationUser> RequireAsync(Guid id)
    {
        return await userManager.FindByIdAsync(id.ToString())
            ?? throw new EntityNotFoundException();
    }

    private async Task<User> MaterializeAsync(ApplicationUser user)
    {
        return new User(
            user,
            [.. await userManager.GetRolesAsync(user)],
            timeProvider.GetUtcNow()
        );
    }

    private async Task<User> ApplyProfileAsync(
        ApplicationUser user,
        UserWithPartialUpdate model
    )
    {
        user.Name = model.Name ?? user.Name;
        user.Surname = model.Surname ?? user.Surname;
        user.PhoneNumber = model.Phone ?? user.PhoneNumber;

        Require(await userManager.UpdateAsync(user));

        return await MaterializeAsync(user);
    }

    private static string RequireKnown(string role)
    {
        return KnownRoles.All.Contains(role, StringComparer.Ordinal)
            ? role
            : throw new ArgumentException($"Unknown role '{role}'.", nameof(role));
    }

    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ArgumentException(
                string.Join("; ", result.Errors.Select(x => x.Description))
            );
        }
    }
}
