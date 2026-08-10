using BookingEngine.Api.Endpoints.OpeningHours.Models;
using BookingEngine.Api.Middlewares;
using BookingEngine.Domain.Exceptions;
using BookingEngine.Infrastructure.Auth;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpeningHoursModel = BookingEngine.Domain.Models.OpeningHours;

namespace BookingEngine.Api.Endpoints.OpeningHours;

/// <summary>
/// Manages the weekly windows during which resources can be booked.
/// </summary>
/// <remarks>
/// Times are UTC wall clock. A window may not cross midnight, so <c>StartTime</c> must be
/// earlier than <c>EndTime</c>; cover an overnight period with two windows on consecutive
/// days. Readable by anyone; only administrators may change it.
/// </remarks>
[ApiController]
[Route("opening-hours")]
[Tags("Opening Hours")]
[Authorize(Roles = KnownRoles.Admin)]
public sealed class OpeningHoursEndpoints(BookingDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Returns all opening windows as a streaming collection.
    /// </summary>
    /// <returns>A streaming sequence of all opening hours resources.</returns>
    /// <response code="200">Opening hours returned successfully.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IEnumerable<OpeningHoursModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<OpeningHoursModel> All()
    {
        return dbContext
            .OpeningHours.AsNoTracking()
            .OrderBy(x => x.DayOfWeek)
            .ThenBy(x => x.StartTime)
            .AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns a single opening window by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the opening window to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The opening window matching the provided identifier.</returns>
    /// <response code="200">Opening window found and returned successfully.</response>
    /// <response code="404">No opening window exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<OpeningHoursModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<OpeningHoursModel> ById(Guid id, CancellationToken cancellationToken)
    {
        return await RequireAsync(id, cancellationToken);
    }

    /// <summary>
    /// Creates a new opening window.
    /// </summary>
    /// <remarks>
    /// A new UUID is assigned by the server. The caller cannot specify the identifier.
    /// </remarks>
    /// <param name="model">The opening window data to create.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The newly created opening window, including the server-assigned identifier.</returns>
    /// <response code="200">Opening window created successfully. Returns the created resource.</response>
    /// <response code="400">The window ends no later than it starts.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">The referenced resource does not exist.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost]
    [ProducesResponseType<OpeningHoursModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<OpeningHoursModel> New(
        [FromBody] OpeningHoursWithNoId model,
        CancellationToken cancellationToken
    )
    {
        OpeningHoursModel created = new(
            Guid.CreateVersion7(),
            model.ResourceId,
            model.DayOfWeek,
            model.StartTime,
            model.EndTime
        );

        await ValidateAsync(created, cancellationToken);

        _ = dbContext.OpeningHours.Add(created);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return created;
    }

    /// <summary>
    /// Fully replaces an existing opening window.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the opening window to replace.</param>
    /// <param name="model">The new opening window data. All fields are required.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated opening window.</returns>
    /// <response code="200">Opening window updated successfully. Returns the updated resource.</response>
    /// <response code="400">The window ends no later than it starts.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No opening window, or no referenced resource, exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<OpeningHoursModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<OpeningHoursModel> Update(
        Guid id,
        [FromBody] OpeningHoursWithNoId model,
        CancellationToken cancellationToken
    )
    {
        _ = await RequireAsync(id, cancellationToken);

        OpeningHoursModel updated = new(
            id,
            model.ResourceId,
            model.DayOfWeek,
            model.StartTime,
            model.EndTime
        );

        await ValidateAsync(updated, cancellationToken);

        _ = dbContext.OpeningHours.Update(updated);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>
    /// Partially updates an existing opening window.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the opening window to update.</param>
    /// <param name="model">The fields to change. Null fields are left untouched.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated opening window.</returns>
    /// <response code="200">Opening window updated successfully. Returns the updated resource.</response>
    /// <response code="400">The window ends no later than it starts.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No opening window, or no referenced resource, exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<OpeningHoursModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<OpeningHoursModel> Patch(
        Guid id,
        [FromBody] OpeningHoursWithPartialUpdate model,
        CancellationToken cancellationToken
    )
    {
        OpeningHoursModel existing = await RequireAsync(id, cancellationToken);

        OpeningHoursModel updated = existing with
        {
            ResourceId = model.ResourceId ?? existing.ResourceId,
            DayOfWeek = model.DayOfWeek ?? existing.DayOfWeek,
            StartTime = model.StartTime ?? existing.StartTime,
            EndTime = model.EndTime ?? existing.EndTime,
        };

        await ValidateAsync(updated, cancellationToken);

        _ = dbContext.OpeningHours.Update(updated);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>
    /// Deletes the specified opening window.
    /// </summary>
    /// <remarks>
    /// If no opening window with the given identifier exists, the operation still succeeds
    /// (idempotent delete). Existing bookings inside the removed window are not cancelled.
    /// </remarks>
    /// <param name="id">The unique identifier (UUID v4) of the opening window to delete.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <response code="200">Opening window deleted successfully (or did not exist).</response>
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
            .OpeningHours.Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<OpeningHoursModel> RequireAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        return await dbContext
                .OpeningHours.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException();
    }

    private async Task ValidateAsync(
        OpeningHoursModel hours,
        CancellationToken cancellationToken
    )
    {
        if (hours.StartTime >= hours.EndTime)
        {
            throw new ArgumentException("StartTime must be earlier than EndTime.");
        }

        bool resourceExists = await dbContext.Resources.AnyAsync(
            x => x.Id == hours.ResourceId,
            cancellationToken
        );

        if (!resourceExists)
        {
            throw new EntityNotFoundException(
                $"Resource '{hours.ResourceId}' does not exist."
            );
        }
    }
}
