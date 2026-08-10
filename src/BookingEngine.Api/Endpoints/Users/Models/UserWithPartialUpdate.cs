namespace BookingEngine.Api.Endpoints.Users.Models;

/// <summary>
/// Request body for a partial update (PATCH) of a user's profile.
/// Only non-null fields are applied; omitted or null fields retain their current values.
/// </summary>
/// <param name="Name">New given name. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="Surname">New family name. Pass <c>null</c> or omit to keep the existing value.</param>
/// <param name="Phone">New telephone number. Pass <c>null</c> or omit to keep the existing value.</param>
public sealed record UserWithPartialUpdate(string? Name, string? Surname, string? Phone);
