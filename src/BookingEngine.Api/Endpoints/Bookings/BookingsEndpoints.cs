using System.Security.Claims;
using BookingEngine.Api.Endpoints.Bookings.Models;
using BookingEngine.Api.Middlewares;
using BookingEngine.ApplicationCore;
using BookingEngine.Domain.Exceptions;
using BookingEngine.Domain.Models;
using BookingEngine.Infrastructure.Auth;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.Api.Endpoints.Bookings;

/// <summary>
/// Manages bookings held on resources.
/// </summary>
/// <remarks>
/// A user may read and change only their own bookings; an administrator may act on anyone's.
/// Every write runs inside a serializable transaction, so two callers competing for the same
/// slot cannot both succeed.
/// </remarks>
[ApiController]
[Route("bookings")]
[Tags("Bookings")]
[Authorize]
public sealed class BookingsEndpoints(
    BookingDbContext dbContext,
    BookingService bookings,
    TimeProvider timeProvider
) : ControllerBase
{
    /// <summary>
    /// Returns every booking in the system.
    /// </summary>
    /// <returns>A streaming sequence of all bookings.</returns>
    /// <response code="200">Bookings returned successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [Authorize(Roles = KnownRoles.Admin)]
    [ProducesResponseType<IEnumerable<Booking>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<Booking> All()
    {
        return dbContext.Bookings.AsNoTracking().OrderBy(x => x.StartsAt).AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns the bookings held by the calling user.
    /// </summary>
    /// <returns>A streaming sequence of the caller's own bookings.</returns>
    /// <response code="200">Bookings returned successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("current")]
    [ProducesResponseType<IEnumerable<Booking>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<Booking> Current()
    {
        Guid callerId = CallerId();

        return dbContext
            .Bookings.AsNoTracking()
            .Where(x => x.UserId == callerId)
            .OrderBy(x => x.StartsAt)
            .AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns a single booking by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the booking to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The booking matching the provided identifier.</returns>
    /// <response code="200">Booking found and returned successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The booking belongs to another user.</response>
    /// <response code="404">No booking exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<Booking>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Booking> ById(Guid id, CancellationToken cancellationToken)
    {
        return RequireOwnership(await RequireAsync(id, cancellationToken));
    }

    /// <summary>
    /// Books a slot on a resource.
    /// </summary>
    /// <remarks>
    /// The period must match exactly one of the slots returned by the resource's availability
    /// endpoint. The check and the write share a serializable transaction, so of two callers
    /// racing for the same slot only one succeeds; the other receives 409 Conflict.
    /// An administrator may set <c>userId</c> to book on another user's behalf; for everyone
    /// else the booking is placed for the caller regardless of what the body says.
    /// </remarks>
    /// <param name="model">The booking to place.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The confirmed booking, including the server-assigned identifier.</returns>
    /// <response code="200">Booking confirmed successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="404">No resource exists with the specified identifier.</response>
    /// <response code="409">The period is not an available slot, or another caller took it first.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost]
    [ProducesResponseType<Booking>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Booking> New(
        [FromBody] BookingWithNoId model,
        CancellationToken cancellationToken
    )
    {
        Booking candidate = new(
            Guid.CreateVersion7(),
            model.ResourceId,
            IsAdministrator() ? model.UserId ?? CallerId() : CallerId(),
            model.StartsAt.ToUniversalTime(),
            model.EndsAt.ToUniversalTime(),
            BookingStatus.Confirmed
        );

        return await bookings.CreateAsync(
            candidate,
            timeProvider.GetUtcNow(),
            cancellationToken
        );
    }

    /// <summary>
    /// Partially updates a booking, including cancelling it.
    /// </summary>
    /// <remarks>
    /// Send <c>{ "status": "Cancelled" }</c> to cancel; the period becomes available again.
    /// Moving the period, or restoring a cancelled booking to <c>Confirmed</c>, re-checks
    /// availability inside the same serializable transaction. A booking never conflicts with
    /// its own current period.
    /// </remarks>
    /// <param name="id">The unique identifier (UUID v4) of the booking to update.</param>
    /// <param name="model">The fields to change. Null fields are left untouched.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated booking.</returns>
    /// <response code="200">Booking updated successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The booking belongs to another user.</response>
    /// <response code="404">No booking exists with the specified identifier.</response>
    /// <response code="409">The new period is not an available slot.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Booking>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Booking> Patch(
        Guid id,
        [FromBody] BookingWithPartialUpdate model,
        CancellationToken cancellationToken
    )
    {
        _ = RequireOwnership(await RequireAsync(id, cancellationToken));

        return await bookings.ChangeAsync(
            id,
            existing =>
                existing with
                {
                    StartsAt = (model.StartsAt ?? existing.StartsAt).ToUniversalTime(),
                    EndsAt = (model.EndsAt ?? existing.EndsAt).ToUniversalTime(),
                    Status = model.Status ?? existing.Status,
                },
            timeProvider.GetUtcNow(),
            cancellationToken
        );
    }

    /// <summary>
    /// Deletes the specified booking.
    /// </summary>
    /// <remarks>
    /// The period becomes available again. Cancelling via PATCH keeps a record of the
    /// booking; deleting removes it outright.
    /// </remarks>
    /// <param name="id">The unique identifier (UUID v4) of the booking to delete.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <response code="200">Booking deleted successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The booking belongs to another user.</response>
    /// <response code="404">No booking exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task Delete(Guid id, CancellationToken cancellationToken)
    {
        _ = RequireOwnership(await RequireAsync(id, cancellationToken));

        _ = await dbContext
            .Bookings.Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Guid CallerId()
    {
        string? id = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(id, out Guid parsed)
            ? parsed
            : throw new EntityNotFoundException();
    }

    private bool IsAdministrator()
    {
        return User.IsInRole(KnownRoles.Admin);
    }

    private async Task<Booking> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext
                .Bookings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException();
    }

    private Booking RequireOwnership(Booking booking)
    {
        return IsAdministrator() || (booking.UserId == CallerId())
            ? booking
            : throw new ForbiddenException("The booking belongs to another user.");
    }
}
