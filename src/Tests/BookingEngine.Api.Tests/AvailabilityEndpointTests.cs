using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookingEngine.Api.Tests;

[Collection(nameof(BookingEngineApiTestSet))]
public sealed record AvailabilityEndpointTests
{
    /// <summary>
    /// Midnight UTC on the next Thursday at least a week away.
    /// </summary>
    /// <remarks>
    /// The window must be in the future, because a resource's minimum notice is measured
    /// from the real clock: a fixed past date would be clamped away to nothing.
    /// </remarks>
    private static readonly DateTimeOffset Thursday = NextThursday();

    private readonly BookingEngineApiFactory _factory;

    public AvailabilityEndpointTests(BookingEngineApiFactory factory)
    {
        _factory = factory;
    }

    private static DateTimeOffset NextThursday()
    {
        DateTimeOffset midnight = new(
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
            TimeOnly.MinValue,
            TimeSpan.Zero
        );

        while (midnight.DayOfWeek != DayOfWeek.Thursday)
        {
            midnight = midnight.AddDays(1);
        }

        return midnight;
    }

    private static string Route(Guid resourceId, DateTimeOffset from, DateTimeOffset to)
    {
        return $"/resources/{resourceId}/availability"
            + $"?from={Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))}"
            + $"&to={Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture))}";
    }

    private static async Task<IReadOnlyCollection<string>> SlotStartsAsync(
        HttpClient client,
        Guid resourceId,
        DateTimeOffset from,
        DateTimeOffset to
    )
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(Route(resourceId, from, to), UriKind.Relative)
        );

        _ = response.EnsureSuccessStatusCode();

        JsonElement slots = await response.Content.ReadFromJsonAsync<JsonElement>();

        return
        [
            .. slots
                .EnumerateArray()
                .Select(x => x.GetProperty("startsAt").GetDateTimeOffset())
                .Select(x => x.ToString("HH:mm", CultureInfo.InvariantCulture)),
        ];
    }

    [Fact]
    public async Task ShouldReturnGridOfOpeningHoursWhenNothingIsBusy()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Thursday,
            new TimeOnly(9, 0),
            new TimeOnly(11, 0)
        );

        using HttpClient anonymous = _factory.CreateClient();

        Assert.Equal(
            ["09:00", "09:30", "10:00", "10:30"],
            await SlotStartsAsync(anonymous, resourceId, Thursday, Thursday.AddDays(1))
        );
    }

    [Fact]
    public async Task ShouldExcludeBlackedOutSlotsWithoutShiftingTheGrid()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Thursday,
            new TimeOnly(9, 0),
            new TimeOnly(11, 0)
        );

        // Ends at 09:40, which is not on the grid: the remaining slots must stay at
        // 10:00 and 10:30 rather than re-anchoring to 09:40.
        _ = await admin.CreateBlackoutAsync(
            resourceId,
            Thursday.AddHours(9).AddMinutes(30),
            Thursday.AddHours(9).AddMinutes(40)
        );

        using HttpClient anonymous = _factory.CreateClient();

        Assert.Equal(
            ["09:00", "10:00", "10:30"],
            await SlotStartsAsync(anonymous, resourceId, Thursday, Thursday.AddDays(1))
        );
    }

    [Fact]
    public async Task ShouldReturnEmptyWhenResourceHasNoOpeningHours()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        using HttpClient anonymous = _factory.CreateClient();

        Assert.Empty(
            await SlotStartsAsync(anonymous, resourceId, Thursday, Thursday.AddDays(7))
        );
    }

    [Fact]
    public async Task ShouldRespectMinimumNoticeRelativeToNow()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();

        // A ten-year notice pushes every slot in the queried window out of reach.
        Guid resourceId = await admin.CreateResourceAsync(
            minNotice: TimeSpan.FromDays(3650),
            maxHorizon: TimeSpan.FromDays(7300)
        );

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Thursday,
            new TimeOnly(9, 0),
            new TimeOnly(11, 0)
        );

        using HttpClient anonymous = _factory.CreateClient();

        Assert.Empty(
            await SlotStartsAsync(anonymous, resourceId, Thursday, Thursday.AddDays(7))
        );
    }

    [Fact]
    public async Task ShouldRespectMaximumHorizonRelativeToNow()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();

        // An hour-long horizon leaves nothing bookable a year out.
        Guid resourceId = await admin.CreateResourceAsync(
            maxHorizon: TimeSpan.FromHours(1)
        );

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Thursday,
            new TimeOnly(9, 0),
            new TimeOnly(11, 0)
        );

        using HttpClient anonymous = _factory.CreateClient();
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(300);

        Assert.Empty(await SlotStartsAsync(anonymous, resourceId, from, from.AddDays(7)));
    }

    [Fact]
    public async Task ShouldOnlyOfferSlotsOnTheConfiguredWeekday()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Friday,
            new TimeOnly(9, 0),
            new TimeOnly(10, 0)
        );

        using HttpClient anonymous = _factory.CreateClient();

        // Thursday only: nothing. Thursday plus Friday: the Friday grid.
        Assert.Empty(
            await SlotStartsAsync(anonymous, resourceId, Thursday, Thursday.AddHours(23))
        );

        Assert.Equal(
            ["09:00", "09:30"],
            await SlotStartsAsync(anonymous, resourceId, Thursday, Thursday.AddDays(2))
        );
    }

    [Fact]
    public async Task ShouldReturn400WhenWindowIsReversed()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri(Route(resourceId, Thursday.AddDays(1), Thursday), UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn400WhenWindowIsWiderThanNinetyDays()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync();

        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri(Route(resourceId, Thursday, Thursday.AddDays(91)), UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn404WhenResourceDoesNotExist()
    {
        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri(Route(Guid.NewGuid(), Thursday, Thursday.AddDays(1)), UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
