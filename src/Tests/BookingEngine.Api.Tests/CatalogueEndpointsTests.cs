using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BookingEngine.Api.Tests;

[Collection(nameof(BookingEngineApiTestSet))]
public sealed record CatalogueEndpointsTests
{
    private readonly BookingEngineApiFactory _factory;

    public CatalogueEndpointsTests(BookingEngineApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ShouldReturnCreatedResourceTypeWhenPosting()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        string name = $"Type {Guid.NewGuid():N}";

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/resource-types", UriKind.Relative),
            new { name, description = "Rooms you can meet in" }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.NotEqual(Guid.Empty, created.GetProperty("id").GetGuid());
        Assert.Equal(name, created.GetProperty("name").GetString());
        Assert.Equal("Rooms you can meet in", created.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ShouldReadCatalogueWithoutAuthentication()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri($"/resources/{resourceId}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ShouldRejectCatalogueWritesFromOrdinaryUsers()
    {
        (HttpClient client, _, _) = await _factory.AuthenticateAsUserAsync();

        using (client)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/resource-types", UriKind.Relative),
                new { name = "Sneaky", description = string.Empty }
            );

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldRejectCatalogueWritesFromAnonymousCallers()
    {
        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            new Uri("/resource-types", UriKind.Relative),
            new { name = "Sneaky", description = string.Empty }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn400WhenResourceTypeDoesNotExist()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/resources", UriKind.Relative),
            new
            {
                typeId = Guid.NewGuid(),
                name = "Orphan",
                description = string.Empty,
                minNotice = TimeSpan.Zero,
                maxHorizon = TimeSpan.FromDays(1),
                slotDuration = TimeSpan.FromMinutes(30),
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("00:00:00", "1.00:00:00", "00:00:00")] // slot duration not positive
    [InlineData("-01:00:00", "1.00:00:00", "00:30:00")] // negative notice
    [InlineData("02:00:00", "01:00:00", "00:30:00")] // horizon below notice
    public async Task ShouldReturn400WhenResourceDurationsAreInconsistent(
        string minNotice,
        string maxHorizon,
        string slotDuration
    )
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid typeId = await admin.CreateResourceTypeAsync();

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/resources", UriKind.Relative),
            new
            {
                typeId,
                name = "Bad durations",
                description = string.Empty,
                minNotice,
                maxHorizon,
                slotDuration,
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn400WhenRequestBodyIsMalformedJson()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        using StringContent body = new("{ not json", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await admin.PostAsync(
            new Uri("/resource-types", UriKind.Relative),
            body
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn404WhenResourceDoesNotExist()
    {
        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri($"/resources/{Guid.NewGuid()}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ShouldKeepUntouchedFieldsWhenPatchingResource()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        using HttpResponseMessage response = await admin.PatchAsJsonAsync(
            new Uri($"/resources/{resourceId}", UriKind.Relative),
            new { description = "Now with a description" }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement updated = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Now with a description", updated.GetProperty("description").GetString());
        Assert.Equal("00:30:00", updated.GetProperty("slotDuration").GetString());
    }

    [Fact]
    public async Task ShouldReplaceEveryFieldWhenPuttingResourceType()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid id = await admin.CreateResourceTypeAsync();
        string name = $"Renamed {Guid.NewGuid():N}";

        using HttpResponseMessage response = await admin.PutAsJsonAsync(
            new Uri($"/resource-types/{id}", UriKind.Relative),
            new { name, description = "Replaced" }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement updated = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(id, updated.GetProperty("id").GetGuid());
        Assert.Equal(name, updated.GetProperty("name").GetString());
        Assert.Equal("Replaced", updated.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ShouldReturn409WhenResourceTypeNameIsTaken()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        string name = $"Duplicate {Guid.NewGuid():N}";

        using (
            HttpResponseMessage first = await admin.PostAsJsonAsync(
                new Uri("/resource-types", UriKind.Relative),
                new { name, description = string.Empty }
            )
        )
        {
            _ = first.EnsureSuccessStatusCode();
        }

        using HttpResponseMessage second = await admin.PostAsJsonAsync(
            new Uri("/resource-types", UriKind.Relative),
            new { name, description = string.Empty }
        );

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn400WhenOpeningWindowCrossesMidnight()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/opening-hours", UriKind.Relative),
            new
            {
                resourceId,
                dayOfWeek = DayOfWeek.Monday,
                startTime = "22:00:00",
                endTime = "02:00:00",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn404WhenOpeningHoursReferenceMissingResource()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/opening-hours", UriKind.Relative),
            new
            {
                resourceId = Guid.NewGuid(),
                dayOfWeek = DayOfWeek.Monday,
                startTime = "09:00:00",
                endTime = "17:00:00",
            }
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn400WhenBlackoutEndsBeforeItStarts()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/blackouts", UriKind.Relative),
            new
            {
                resourceId,
                startsAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                endsAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                reason = "Backwards",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldNormalizeBlackoutInstantsToUtc()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        using HttpResponseMessage response = await admin.PostAsJsonAsync(
            new Uri("/blackouts", UriKind.Relative),
            new
            {
                resourceId,
                startsAt = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.FromHours(2)),
                endsAt = new DateTimeOffset(2026, 1, 1, 13, 0, 0, TimeSpan.FromHours(2)),
                reason = "Maintenance",
            }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            created.GetProperty("startsAt").GetDateTimeOffset()
        );
    }

    [Fact]
    public async Task ShouldListNestedOpeningHoursAndBlackoutsOfResource()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Tuesday,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0)
        );

        _ = await admin.CreateBlackoutAsync(
            resourceId,
            new DateTimeOffset(2026, 3, 3, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 3, 13, 0, 0, TimeSpan.Zero)
        );

        using HttpClient anonymous = _factory.CreateClient();

        JsonElement hours = await anonymous.GetFromJsonAsync<JsonElement>(
            new Uri($"/resources/{resourceId}/opening-hours", UriKind.Relative)
        );

        JsonElement blackouts = await anonymous.GetFromJsonAsync<JsonElement>(
            new Uri($"/resources/{resourceId}/blackouts", UriKind.Relative)
        );

        _ = Assert.Single(hours.EnumerateArray());
        _ = Assert.Single(blackouts.EnumerateArray());
    }

    [Fact]
    public async Task ShouldRejectNestedBookingsListingForOrdinaryUsers()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        (HttpClient client, _, _) = await _factory.AuthenticateAsUserAsync();

        using (client)
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri($"/resources/{resourceId}/bookings", UriKind.Relative)
            );

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldRemoveOpeningHoursWhenResourceIsDeleted()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        Guid hoursId = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Wednesday,
            new TimeOnly(9, 0),
            new TimeOnly(10, 0)
        );

        using (
            HttpResponseMessage deleted = await admin.DeleteAsync(
                new Uri($"/resources/{resourceId}", UriKind.Relative)
            )
        )
        {
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        }

        using HttpResponseMessage missing = await admin.GetAsync(
            new Uri($"/opening-hours/{hoursId}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn409WhenDeletingResourceTypeStillInUse()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid typeId = await admin.CreateResourceTypeAsync();
        _ = await admin.CreateResourceAsync(typeId);

        using HttpResponseMessage response = await admin.DeleteAsync(
            new Uri($"/resource-types/{typeId}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn409WhenDeletingResourceWithBookingHistory()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Thursday,
            new TimeOnly(9, 0),
            new TimeOnly(11, 0)
        );

        DateTimeOffset midnight = new(
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
            TimeOnly.MinValue,
            TimeSpan.Zero
        );

        while (midnight.DayOfWeek != DayOfWeek.Thursday)
        {
            midnight = midnight.AddDays(1);
        }

        DateTimeOffset startsAt = midnight.AddHours(9);

        using (
            HttpResponseMessage booked = await admin.PostAsJsonAsync(
                new Uri("/bookings", UriKind.Relative),
                new
                {
                    resourceId,
                    startsAt,
                    endsAt = startsAt.AddMinutes(30),
                }
            )
        )
        {
            _ = booked.EnsureSuccessStatusCode();
        }

        using HttpResponseMessage response = await admin.DeleteAsync(
            new Uri($"/resources/{resourceId}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ShouldSucceedWhenDeletingSomethingThatDoesNotExist()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        using HttpResponseMessage response = await admin.DeleteAsync(
            new Uri($"/blackouts/{Guid.NewGuid()}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
