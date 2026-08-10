using System.Net.Http.Json;
using System.Text.Json;

namespace BookingEngine.Api.Tests;

/// <summary>
/// Fixture builders that create catalogue entries and return their identifiers.
/// </summary>
internal static class HttpClientExtensions
{
    extension(HttpClient client)
    {
        public async Task<Guid> CreateResourceTypeAsync()
        {
            return await CreatedIdAsync(
                client,
                "/resource-types",
                new { name = $"Type {Guid.NewGuid():N}", description = "A category" }
            );
        }

        public async Task<Guid> CreateResourceAsync(
            Guid? typeId = null,
            TimeSpan? minNotice = null,
            TimeSpan? maxHorizon = null,
            TimeSpan? slotDuration = null
        )
        {
            return await CreatedIdAsync(
                client,
                "/resources",
                new
                {
                    typeId = typeId ?? await client.CreateResourceTypeAsync(),
                    name = $"Room {Guid.NewGuid():N}",
                    description = "A bookable room",
                    minNotice = minNotice ?? TimeSpan.Zero,
                    maxHorizon = maxHorizon ?? TimeSpan.FromDays(365),
                    slotDuration = slotDuration ?? TimeSpan.FromMinutes(30),
                }
            );
        }

        public async Task<Guid> CreateOpeningHoursAsync(
            Guid resourceId,
            DayOfWeek dayOfWeek,
            TimeOnly startTime,
            TimeOnly endTime
        )
        {
            return await CreatedIdAsync(
                client,
                "/opening-hours",
                new
                {
                    resourceId,
                    dayOfWeek,
                    startTime,
                    endTime,
                }
            );
        }

        public async Task<Guid> CreateBlackoutAsync(
            Guid resourceId,
            DateTimeOffset startsAt,
            DateTimeOffset endsAt
        )
        {
            return await CreatedIdAsync(
                client,
                "/blackouts",
                new
                {
                    resourceId,
                    startsAt,
                    endsAt,
                    reason = "Maintenance",
                }
            );
        }
    }

    private static async Task<Guid> CreatedIdAsync(
        HttpClient client,
        string route,
        object body
    )
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(route, UriKind.Relative),
            body
        );

        _ = response.EnsureSuccessStatusCode();

        JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>();

        return created.GetProperty("id").GetGuid();
    }
}
