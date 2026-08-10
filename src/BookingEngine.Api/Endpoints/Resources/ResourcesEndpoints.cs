using BookingEngine.Api.Endpoints.Resources.Models;
using BookingEngine.Api.Middlewares;
using BookingEngine.ApplicationCore;
using BookingEngine.Domain.Exceptions;
using BookingEngine.Domain.Models;
using BookingEngine.Infrastructure.Auth;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpeningHoursModel = BookingEngine.Domain.Models.OpeningHours;

namespace BookingEngine.Api.Endpoints.Resources;

/// <summary>
/// Manages resource resources and their nested opening hours, blackouts and bookings.
/// </summary>
/// <remarks>
/// A resource is something that can be booked. Its three durations define the booking grid:
/// slots are <c>SlotDuration</c> long, may not start sooner than <c>MinNotice</c> from now,
/// and may not start later than <c>MaxHorizon</c> from now. The catalogue is readable by
/// anyone; only administrators may change it.
/// </remarks>
[ApiController]
[Route("resources")]
[Tags("Resources")]
[Authorize(Roles = KnownRoles.Admin)]
public sealed class ResourcesEndpoints(
    BookingDbContext dbContext,
    AvailabilityService availability,
    TimeProvider timeProvider
) : ControllerBase
{
    /// <summary>
    /// The widest window the availability endpoint will compute in one request.
    /// </summary>
    private static readonly TimeSpan MaxAvailabilityWindow = TimeSpan.FromDays(90);

    /// <summary>
    /// Returns all resources as a streaming collection.
    /// </summary>
    /// <returns>A streaming sequence of all resource resources.</returns>
    /// <response code="200">Resources returned successfully.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IEnumerable<Resource>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<Resource> All()
    {
        return dbContext.Resources.AsNoTracking().OrderBy(x => x.Name).AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns a single resource by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the resource to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The resource matching the provided identifier.</returns>
    /// <response code="200">Resource found and returned successfully.</response>
    /// <response code="404">No resource exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<Resource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Resource> ById(Guid id, CancellationToken cancellationToken)
    {
        return await RequireAsync(id, cancellationToken);
    }

    /// <summary>
    /// Returns the weekly opening hours of the specified resource.
    /// </summary>
    /// <param name="resourceId">The unique identifier (UUID v4) of the resource.</param>
    /// <returns>A streaming sequence of the resource's opening windows.</returns>
    /// <response code="200">Opening hours returned successfully.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{resourceId:guid}/opening-hours")]
    [AllowAnonymous]
    [ProducesResponseType<IEnumerable<OpeningHoursModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<OpeningHoursModel> NestedOpeningHours(Guid resourceId)
    {
        return dbContext
            .OpeningHours.AsNoTracking()
            .Where(x => x.ResourceId == resourceId)
            .OrderBy(x => x.DayOfWeek)
            .ThenBy(x => x.StartTime)
            .AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns the blackouts of the specified resource.
    /// </summary>
    /// <param name="resourceId">The unique identifier (UUID v4) of the resource.</param>
    /// <returns>A streaming sequence of the resource's blackouts.</returns>
    /// <response code="200">Blackouts returned successfully.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{resourceId:guid}/blackouts")]
    [AllowAnonymous]
    [ProducesResponseType<IEnumerable<Blackout>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<Blackout> NestedBlackouts(Guid resourceId)
    {
        return dbContext
            .Blackouts.AsNoTracking()
            .Where(x => x.ResourceId == resourceId)
            .OrderBy(x => x.StartsAt)
            .AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns every booking held on the specified resource.
    /// </summary>
    /// <remarks>
    /// Restricted to administrators, because it exposes bookings belonging to other users.
    /// </remarks>
    /// <param name="resourceId">The unique identifier (UUID v4) of the resource.</param>
    /// <returns>A streaming sequence of the resource's bookings.</returns>
    /// <response code="200">Bookings returned successfully.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{resourceId:guid}/bookings")]
    [ProducesResponseType<IEnumerable<Booking>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<Booking> NestedBookings(Guid resourceId)
    {
        return dbContext
            .Bookings.AsNoTracking()
            .Where(x => x.ResourceId == resourceId)
            .OrderBy(x => x.StartsAt)
            .AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns the slots of the specified resource that are free to book.
    /// </summary>
    /// <remarks>
    /// Slots are the resource's opening hours sliced into <c>SlotDuration</c> intervals,
    /// minus its blackouts and confirmed bookings, and clamped to the resource's minimum
    /// notice and maximum horizon. A returned slot is exactly what
    /// <c>POST /bookings</c> accepts as a period.
    /// </remarks>
    /// <param name="resourceId">The unique identifier (UUID v4) of the resource.</param>
    /// <param name="from">Earliest instant of interest, e.g. <c>2026-01-01T00:00:00Z</c>.</param>
    /// <param name="to">
    /// Latest instant of interest. Must be later than <paramref name="from" />, and no more
    /// than 90 days after it.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The free slots, ordered by start.</returns>
    /// <response code="200">Availability computed successfully.</response>
    /// <response code="400">The window is empty, reversed, or wider than 90 days.</response>
    /// <response code="404">No resource exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{resourceId:guid}/availability")]
    [AllowAnonymous]
    [ProducesResponseType<IEnumerable<AvailabilitySlot>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IReadOnlyCollection<AvailabilitySlot>> NestedAvailability(
        Guid resourceId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken
    )
    {
        ValidateWindow(from, to);

        return await availability.SlotsAsync(
            resourceId,
            from.ToUniversalTime(),
            to.ToUniversalTime(),
            timeProvider.GetUtcNow(),
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a new resource.
    /// </summary>
    /// <remarks>
    /// A new UUID is assigned by the server. The caller cannot specify the identifier.
    /// </remarks>
    /// <param name="model">The resource data to create.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The newly created resource, including the server-assigned identifier.</returns>
    /// <response code="200">Resource created successfully. Returns the created resource.</response>
    /// <response code="400">The referenced resource type does not exist, or a duration is invalid.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost]
    [ProducesResponseType<Resource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Resource> New(
        [FromBody] ResourceWithNoId model,
        CancellationToken cancellationToken
    )
    {
        Resource created = new(
            Guid.CreateVersion7(),
            model.TypeId,
            model.Name,
            model.Description,
            model.MinNotice,
            model.MaxHorizon,
            model.SlotDuration
        );

        await ValidateAsync(created, cancellationToken);

        _ = dbContext.Resources.Add(created);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return created;
    }

    /// <summary>
    /// Fully replaces an existing resource.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the resource to replace.</param>
    /// <param name="model">The new resource data. All fields are required.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated resource.</returns>
    /// <response code="200">Resource updated successfully. Returns the updated resource.</response>
    /// <response code="400">The referenced resource type does not exist, or a duration is invalid.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No resource exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<Resource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Resource> Update(
        Guid id,
        [FromBody] ResourceWithNoId model,
        CancellationToken cancellationToken
    )
    {
        _ = await RequireAsync(id, cancellationToken);

        Resource updated = new(
            id,
            model.TypeId,
            model.Name,
            model.Description,
            model.MinNotice,
            model.MaxHorizon,
            model.SlotDuration
        );

        await ValidateAsync(updated, cancellationToken);

        _ = dbContext.Resources.Update(updated);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>
    /// Partially updates an existing resource.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the resource to update.</param>
    /// <param name="model">The fields to change. Null fields are left untouched.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated resource.</returns>
    /// <response code="200">Resource updated successfully. Returns the updated resource.</response>
    /// <response code="400">The referenced resource type does not exist, or a duration is invalid.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No resource exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Resource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<Resource> Patch(
        Guid id,
        [FromBody] ResourceWithPartialUpdate model,
        CancellationToken cancellationToken
    )
    {
        Resource existing = await RequireAsync(id, cancellationToken);

        Resource updated = existing with
        {
            TypeId = model.TypeId ?? existing.TypeId,
            Name = model.Name ?? existing.Name,
            Description = model.Description ?? existing.Description,
            MinNotice = model.MinNotice ?? existing.MinNotice,
            MaxHorizon = model.MaxHorizon ?? existing.MaxHorizon,
            SlotDuration = model.SlotDuration ?? existing.SlotDuration,
        };

        await ValidateAsync(updated, cancellationToken);

        _ = dbContext.Resources.Update(updated);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>
    /// Deletes the specified resource.
    /// </summary>
    /// <remarks>
    /// If no resource with the given identifier exists, the operation still succeeds
    /// (idempotent delete). The resource's opening hours, blackouts and bookings are deleted
    /// with it.
    /// </remarks>
    /// <param name="id">The unique identifier (UUID v4) of the resource to delete.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <response code="200">Resource deleted successfully (or did not exist).</response>
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
            .Resources.Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static void ValidateWindow(DateTimeOffset from, DateTimeOffset to)
    {
        if (from >= to)
        {
            throw new ArgumentException("'from' must be earlier than 'to'.");
        }

        if ((to - from) > MaxAvailabilityWindow)
        {
            throw new ArgumentException(
                $"The window must not exceed {MaxAvailabilityWindow.TotalDays} days."
            );
        }
    }

    private async Task<Resource> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext
                .Resources.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException();
    }

    private async Task ValidateAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (resource.SlotDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException("SlotDuration must be positive.");
        }

        if (resource.MinNotice < TimeSpan.Zero)
        {
            throw new ArgumentException("MinNotice must not be negative.");
        }

        if (resource.MaxHorizon <= resource.MinNotice)
        {
            throw new ArgumentException("MaxHorizon must be greater than MinNotice.");
        }

        bool typeExists = await dbContext.ResourceTypes.AnyAsync(
            x => x.Id == resource.TypeId,
            cancellationToken
        );

        if (!typeExists)
        {
            throw new ArgumentException($"Resource type '{resource.TypeId}' does not exist.");
        }
    }
}
