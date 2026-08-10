using BookingEngine.Api.Endpoints.Blackouts.Models;
using BookingEngine.Api.Middlewares;
using BookingEngine.Domain.Exceptions;
using BookingEngine.Domain.Models;
using BookingEngine.Infrastructure.Auth;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.Api.Endpoints.Blackouts;

/// <summary>
/// Manages one-off periods during which a resource is unavailable.
/// </summary>
/// <remarks>
/// A blackout overrides the resource's opening hours for the period it covers. Instants are
/// stored as UTC; a non-UTC offset in the request is converted. Readable by anyone; only
/// administrators may change it.
/// </remarks>
[ApiController]
[Route("blackouts")]
[Tags("Blackouts")]
[Authorize(Roles = KnownRoles.Admin)]
public sealed class BlackoutsEndpoints(BookingDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Returns all blackouts as a streaming collection.
    /// </summary>
    /// <returns>A streaming sequence of all blackout resources.</returns>
    /// <response code="200">Blackouts returned successfully.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IEnumerable<Blackout>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<Blackout> All()
    {
        return dbContext.Blackouts.AsNoTracking().OrderBy(x => x.StartsAt).AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns a single blackout by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the blackout to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The blackout matching the provided identifier.</returns>
    /// <response code="200">Blackout found and returned successfully.</response>
    /// <response code="404">No blackout exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<Blackout>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Blackout> ById(Guid id, CancellationToken cancellationToken)
    {
        return await RequireAsync(id, cancellationToken);
    }

    /// <summary>
    /// Creates a new blackout.
    /// </summary>
    /// <remarks>
    /// A new UUID is assigned by the server. The caller cannot specify the identifier.
    /// Bookings already held inside the period are not cancelled.
    /// </remarks>
    /// <param name="model">The blackout data to create.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The newly created blackout, including the server-assigned identifier.</returns>
    /// <response code="200">Blackout created successfully. Returns the created resource.</response>
    /// <response code="400">The blackout ends no later than it starts.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">The referenced resource does not exist.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost]
    [ProducesResponseType<Blackout>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Blackout> New(
        [FromBody] BlackoutWithNoId model,
        CancellationToken cancellationToken
    )
    {
        Blackout created = new(
            Guid.CreateVersion7(),
            model.ResourceId,
            model.StartsAt.ToUniversalTime(),
            model.EndsAt.ToUniversalTime(),
            model.Reason
        );

        await ValidateAsync(created, cancellationToken);

        _ = dbContext.Blackouts.Add(created);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return created;
    }

    /// <summary>
    /// Fully replaces an existing blackout.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the blackout to replace.</param>
    /// <param name="model">The new blackout data. All fields are required.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated blackout.</returns>
    /// <response code="200">Blackout updated successfully. Returns the updated resource.</response>
    /// <response code="400">The blackout ends no later than it starts.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No blackout, or no referenced resource, exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<Blackout>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Blackout> Update(
        Guid id,
        [FromBody] BlackoutWithNoId model,
        CancellationToken cancellationToken
    )
    {
        _ = await RequireAsync(id, cancellationToken);

        Blackout updated = new(
            id,
            model.ResourceId,
            model.StartsAt.ToUniversalTime(),
            model.EndsAt.ToUniversalTime(),
            model.Reason
        );

        await ValidateAsync(updated, cancellationToken);

        _ = dbContext.Blackouts.Update(updated);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>
    /// Partially updates an existing blackout.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the blackout to update.</param>
    /// <param name="model">The fields to change. Null fields are left untouched.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated blackout.</returns>
    /// <response code="200">Blackout updated successfully. Returns the updated resource.</response>
    /// <response code="400">The blackout ends no later than it starts.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No blackout, or no referenced resource, exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Blackout>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Blackout> Patch(
        Guid id,
        [FromBody] BlackoutWithPartialUpdate model,
        CancellationToken cancellationToken
    )
    {
        Blackout existing = await RequireAsync(id, cancellationToken);

        Blackout updated = existing with
        {
            ResourceId = model.ResourceId ?? existing.ResourceId,
            StartsAt = (model.StartsAt ?? existing.StartsAt).ToUniversalTime(),
            EndsAt = (model.EndsAt ?? existing.EndsAt).ToUniversalTime(),
            Reason = model.Reason ?? existing.Reason,
        };

        await ValidateAsync(updated, cancellationToken);

        _ = dbContext.Blackouts.Update(updated);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>
    /// Deletes the specified blackout.
    /// </summary>
    /// <remarks>
    /// If no blackout with the given identifier exists, the operation still succeeds
    /// (idempotent delete). The period becomes bookable again.
    /// </remarks>
    /// <param name="id">The unique identifier (UUID v4) of the blackout to delete.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <response code="200">Blackout deleted successfully (or did not exist).</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task Delete(Guid id, CancellationToken cancellationToken)
    {
        _ = await dbContext
            .Blackouts.Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<Blackout> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext
                .Blackouts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException();
    }

    private async Task ValidateAsync(Blackout blackout, CancellationToken cancellationToken)
    {
        if (blackout.StartsAt >= blackout.EndsAt)
        {
            throw new ArgumentException("StartsAt must be earlier than EndsAt.");
        }

        bool resourceExists = await dbContext.Resources.AnyAsync(
            x => x.Id == blackout.ResourceId,
            cancellationToken
        );

        if (!resourceExists)
        {
            throw new EntityNotFoundException(
                $"Resource '{blackout.ResourceId}' does not exist."
            );
        }
    }
}
