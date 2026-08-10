namespace BookingEngine.Api.Endpoints.ResourceTypes.Models;

/// <summary>
/// Request body for a partial update (PATCH) of a resource type.
/// Only non-null fields are applied; omitted or null fields retain their current values.
/// </summary>
/// <param name="Name">New display name. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="Description">New description. Pass <c>null</c> or omit to keep the existing value.</param>
public sealed record ResourceTypeWithPartialUpdate(string? Name, string? Description);
