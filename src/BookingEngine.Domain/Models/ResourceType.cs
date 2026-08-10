namespace BookingEngine.Domain.Models;

/// <summary>
/// A category of bookable resource, such as <c>"Meeting room"</c> or <c>"Company car"</c>.
/// </summary>
/// <param name="Id">Unique identifier of the resource type (UUID v4).</param>
/// <param name="Name">Short display name of the category.</param>
/// <param name="Description">Longer description explaining what belongs to this category.</param>
public sealed record ResourceType(Guid Id, string Name, string Description);
