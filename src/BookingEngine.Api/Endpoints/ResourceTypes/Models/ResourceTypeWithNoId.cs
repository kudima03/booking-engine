namespace BookingEngine.Api.Endpoints.ResourceTypes.Models;

/// <summary>
/// Request body for creating or fully replacing a resource type.
/// </summary>
/// <param name="Name">Short display name of the category. Must be unique.</param>
/// <param name="Description">Longer description explaining what belongs to this category.</param>
public sealed record ResourceTypeWithNoId(string Name, string Description);
