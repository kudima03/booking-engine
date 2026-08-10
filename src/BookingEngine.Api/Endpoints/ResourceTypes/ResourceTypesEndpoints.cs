using BookingEngine.Api.Endpoints.ResourceTypes.Models;
using BookingEngine.Api.Middlewares;
using BookingEngine.Domain.Exceptions;
using BookingEngine.Domain.Models;
using BookingEngine.Infrastructure.Auth;
using BookingEngine.Infrastructure.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingEngine.Api.Endpoints.ResourceTypes;

/// <summary>
/// Manages resource type resources.
/// </summary>
/// <remarks>
/// A resource type is the category a resource belongs to, such as <c>"Meeting room"</c>.
/// Resource types must be created before resources that reference them. The catalogue is
/// readable by anyone; only administrators may change it.
/// </remarks>
[ApiController]
[Route("resource-types")]
[Tags("Resource Types")]
[Authorize(Roles = KnownRoles.Admin)]
public sealed class ResourceTypesEndpoints(BookingDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Returns all resource types as a streaming collection.
    /// </summary>
    /// <returns>A streaming sequence of all resource type resources.</returns>
    /// <response code="200">Resource types returned successfully.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IEnumerable<ResourceType>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IAsyncEnumerable<ResourceType> All()
    {
        return dbContext.ResourceTypes.AsNoTracking().OrderBy(x => x.Name).AsAsyncEnumerable();
    }

    /// <summary>
    /// Returns a single resource type by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the resource type to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The resource type matching the provided identifier.</returns>
    /// <response code="200">Resource type found and returned successfully.</response>
    /// <response code="404">No resource type exists with the specified identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ResourceType>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ResourceType> ById(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext
                .ResourceTypes.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException();
    }

    /// <summary>
    /// Creates a new resource type.
    /// </summary>
    /// <remarks>
    /// A new UUID is assigned by the server. The caller cannot specify the identifier.
    /// </remarks>
    /// <param name="model">The resource type data to create.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The newly created resource type, including the server-assigned identifier.</returns>
    /// <response code="200">Resource type created successfully. Returns the created resource.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="409">A resource type with that name already exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost]
    [ProducesResponseType<ResourceType>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ResourceType> New(
        [FromBody] ResourceTypeWithNoId model,
        CancellationToken cancellationToken
    )
    {
        ResourceType created = new(Guid.CreateVersion7(), model.Name, model.Description);

        _ = dbContext.ResourceTypes.Add(created);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return created;
    }

    /// <summary>
    /// Fully replaces an existing resource type.
    /// </summary>
    /// <remarks>
    /// All fields are overwritten with the values in the request body. Renaming a resource type
    /// affects every resource that references it.
    /// </remarks>
    /// <param name="id">The unique identifier (UUID v4) of the resource type to replace.</param>
    /// <param name="model">The new resource type data. All fields are required.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated resource type.</returns>
    /// <response code="200">Resource type updated successfully. Returns the updated resource.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No resource type exists with the specified identifier.</response>
    /// <response code="409">A resource type with that name already exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ResourceType>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ResourceType> Update(
        Guid id,
        [FromBody] ResourceTypeWithNoId model,
        CancellationToken cancellationToken
    )
    {
        _ = await RequireAsync(id, cancellationToken);

        ResourceType updated = new(id, model.Name, model.Description);

        _ = dbContext.ResourceTypes.Update(updated);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>
    /// Partially updates an existing resource type.
    /// </summary>
    /// <param name="id">The unique identifier (UUID v4) of the resource type to update.</param>
    /// <param name="model">The fields to change. Null fields are left untouched.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The updated resource type.</returns>
    /// <response code="200">Resource type updated successfully. Returns the updated resource.</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No resource type exists with the specified identifier.</response>
    /// <response code="409">A resource type with that name already exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ResourceType>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ResourceType> Patch(
        Guid id,
        [FromBody] ResourceTypeWithPartialUpdate model,
        CancellationToken cancellationToken
    )
    {
        ResourceType existing = await RequireAsync(id, cancellationToken);

        ResourceType updated = existing with
        {
            Name = model.Name ?? existing.Name,
            Description = model.Description ?? existing.Description,
        };

        _ = dbContext.ResourceTypes.Update(updated);
        _ = await dbContext.SaveChangesAsync(cancellationToken);

        return updated;
    }

    /// <summary>
    /// Deletes the specified resource type.
    /// </summary>
    /// <remarks>
    /// If no resource type with the given identifier exists, the operation still succeeds
    /// (idempotent delete). A resource type still referenced by resources cannot be deleted.
    /// </remarks>
    /// <param name="id">The unique identifier (UUID v4) of the resource type to delete.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <response code="200">Resource type deleted successfully (or did not exist).</response>
    /// <response code="401">The request carries no valid access token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="409">The resource type is still referenced by one or more resources.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task Delete(Guid id, CancellationToken cancellationToken)
    {
        _ = await dbContext
            .ResourceTypes.Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<ResourceType> RequireAsync(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        return await dbContext
                .ResourceTypes.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException();
    }
}
