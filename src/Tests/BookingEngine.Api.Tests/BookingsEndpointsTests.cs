using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookingEngine.Api.Tests;

[Collection(nameof(BookingEngineApiTestSet))]
public sealed record BookingsEndpointsTests
{
    private readonly BookingEngineApiFactory _factory;

    public BookingsEndpointsTests(BookingEngineApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Midnight UTC on the next Thursday at least a week away.
    /// </summary>
    /// <remarks>
    /// Bookings are checked against the real clock, so the period must be in the future.
    /// </remarks>
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

    /// <summary>
    /// Creates a resource open 09:00-11:00 on the next Thursday, in 30-minute slots.
    /// </summary>
    private static async Task<(Guid ResourceId, DateTimeOffset FirstSlot)> NewBookableResourceAsync(
        HttpClient admin
    )
    {
        Guid resourceId = await admin.CreateResourceAsync();

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Thursday,
            new TimeOnly(9, 0),
            new TimeOnly(11, 0)
        );

        return (resourceId, NextThursday().AddHours(9));
    }

    private static Task<HttpResponseMessage> BookAsync(
        HttpClient client,
        Guid resourceId,
        DateTimeOffset startsAt,
        TimeSpan? duration = null,
        Guid? userId = null
    )
    {
        return client.PostAsJsonAsync(
            new Uri("/bookings", UriKind.Relative),
            new
            {
                resourceId,
                userId,
                startsAt,
                endsAt = startsAt + (duration ?? TimeSpan.FromMinutes(30)),
            }
        );
    }

    private static async Task<IReadOnlyCollection<string>> SlotStartsAsync(
        HttpClient client,
        Guid resourceId,
        DateTimeOffset from,
        DateTimeOffset to
    )
    {
        JsonElement slots = await client.GetFromJsonAsync<JsonElement>(
            new Uri(
                $"/resources/{resourceId}/availability"
                    + $"?from={Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))}"
                    + $"&to={Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture))}",
                UriKind.Relative
            )
        );

        return
        [
            .. slots
                .EnumerateArray()
                .Select(x => x.GetProperty("startsAt").GetDateTimeOffset())
                .Select(x => x.ToString("HH:mm", CultureInfo.InvariantCulture)),
        ];
    }

    [Fact]
    public async Task ShouldConfirmBookingOnAnAvailableSlot()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage response = await BookAsync(user, resourceId, slot);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            JsonElement booking = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal("Confirmed", booking.GetProperty("status").GetString());
            Assert.Equal(slot, booking.GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal(resourceId, booking.GetProperty("resourceId").GetGuid());
        }
    }

    [Fact]
    public async Task ShouldRemoveBookedSlotFromAvailability()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage booked = await BookAsync(user, resourceId, slot);
            _ = booked.EnsureSuccessStatusCode();
        }

        using HttpClient anonymous = _factory.CreateClient();

        Assert.Equal(
            ["09:30", "10:00", "10:30"],
            await SlotStartsAsync(
                anonymous,
                resourceId,
                slot.Date,
                slot.Date.AddDays(1)
            )
        );
    }

    [Fact]
    public async Task ShouldReturn409WhenSlotIsAlreadyBooked()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient first, _, _) = await _factory.AuthenticateAsUserAsync();
        (HttpClient second, _, _) = await _factory.AuthenticateAsUserAsync();

        using (first)
        using (second)
        {
            using HttpResponseMessage booked = await BookAsync(first, resourceId, slot);
            _ = booked.EnsureSuccessStatusCode();

            using HttpResponseMessage refused = await BookAsync(second, resourceId, slot);

            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldReturn409WhenPeriodIsNotOnTheGrid()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            // Starts a quarter past the hour, so it is not one of the emitted slots.
            using HttpResponseMessage response = await BookAsync(
                user,
                resourceId,
                slot.AddMinutes(15)
            );

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldReturn409WhenPeriodSpansSeveralSlots()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage response = await BookAsync(
                user,
                resourceId,
                slot,
                TimeSpan.FromHours(1)
            );

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldReturn409WhenSlotIsBlackedOut()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        _ = await admin.CreateBlackoutAsync(resourceId, slot, slot.AddMinutes(30));

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage response = await BookAsync(user, resourceId, slot);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldReturn409WhenBookingIsInsideMinimumNotice()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
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

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage response = await BookAsync(
                user,
                resourceId,
                NextThursday().AddHours(9)
            );

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldReturn409WhenBookingIsBeyondMaximumHorizon()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        Guid resourceId = await admin.CreateResourceAsync(maxHorizon: TimeSpan.FromDays(1));

        _ = await admin.CreateOpeningHoursAsync(
            resourceId,
            DayOfWeek.Thursday,
            new TimeOnly(9, 0),
            new TimeOnly(11, 0)
        );

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage response = await BookAsync(
                user,
                resourceId,
                NextThursday().AddHours(9)
            );

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldReturn404WhenResourceDoesNotExist()
    {
        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage response = await BookAsync(
                user,
                Guid.NewGuid(),
                NextThursday().AddHours(9)
            );

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldRejectBookingWhenNotAuthenticated()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await BookAsync(anonymous, resourceId, slot);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShouldIgnoreSuppliedUserIdWhenCallerIsNotAdministrator()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            JsonElement self = await user.GetFromJsonAsync<JsonElement>(
                new Uri("/users/current", UriKind.Relative)
            );

            using HttpResponseMessage response = await BookAsync(
                user,
                resourceId,
                slot,
                userId: Guid.NewGuid()
            );

            JsonElement booking = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(
                self.GetProperty("id").GetGuid(),
                booking.GetProperty("userId").GetGuid()
            );
        }
    }

    [Fact]
    public async Task ShouldBookOnBehalfOfAnotherUserWhenCallerIsAdministrator()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();
        Guid userId;

        using (user)
        {
            JsonElement self = await user.GetFromJsonAsync<JsonElement>(
                new Uri("/users/current", UriKind.Relative)
            );

            userId = self.GetProperty("id").GetGuid();
        }

        using HttpResponseMessage response = await BookAsync(
            admin,
            resourceId,
            slot,
            userId: userId
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement booking = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(userId, booking.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task ShouldReturn403WhenActingOnAnotherUsersBooking()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient owner, _, _) = await _factory.AuthenticateAsUserAsync();
        (HttpClient stranger, _, _) = await _factory.AuthenticateAsUserAsync();

        using (owner)
        using (stranger)
        {
            using HttpResponseMessage booked = await BookAsync(owner, resourceId, slot);
            JsonElement booking = await booked.Content.ReadFromJsonAsync<JsonElement>();
            Guid id = booking.GetProperty("id").GetGuid();

            using HttpResponseMessage read = await stranger.GetAsync(
                new Uri($"/bookings/{id}", UriKind.Relative)
            );

            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

            using HttpResponseMessage deleted = await stranger.DeleteAsync(
                new Uri($"/bookings/{id}", UriKind.Relative)
            );

            Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldFreeTheSlotWhenBookingIsCancelled()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage booked = await BookAsync(user, resourceId, slot);
            JsonElement booking = await booked.Content.ReadFromJsonAsync<JsonElement>();
            Guid id = booking.GetProperty("id").GetGuid();

            using HttpResponseMessage cancelled = await user.PatchAsJsonAsync(
                new Uri($"/bookings/{id}", UriKind.Relative),
                new { status = "Cancelled" }
            );

            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

            JsonElement updated = await cancelled.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal("Cancelled", updated.GetProperty("status").GetString());
        }

        using HttpClient anonymous = _factory.CreateClient();

        Assert.Contains(
            "09:00",
            await SlotStartsAsync(anonymous, resourceId, slot.Date, slot.Date.AddDays(1))
        );
    }

    [Fact]
    public async Task ShouldNotConflictWithItselfWhenRescheduling()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage booked = await BookAsync(user, resourceId, slot);
            JsonElement booking = await booked.Content.ReadFromJsonAsync<JsonElement>();
            Guid id = booking.GetProperty("id").GetGuid();

            // Re-confirming the very same period must succeed: a booking never blocks itself.
            using HttpResponseMessage response = await user.PatchAsJsonAsync(
                new Uri($"/bookings/{id}", UriKind.Relative),
                new { startsAt = slot, endsAt = slot.AddMinutes(30) }
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldMoveBookingToAnotherFreeSlot()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient user, _, _) = await _factory.AuthenticateAsUserAsync();

        using (user)
        {
            using HttpResponseMessage booked = await BookAsync(user, resourceId, slot);
            JsonElement booking = await booked.Content.ReadFromJsonAsync<JsonElement>();
            Guid id = booking.GetProperty("id").GetGuid();

            using HttpResponseMessage response = await user.PatchAsJsonAsync(
                new Uri($"/bookings/{id}", UriKind.Relative),
                new { startsAt = slot.AddHours(1), endsAt = slot.AddHours(1).AddMinutes(30) }
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            JsonElement updated = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(
                slot.AddHours(1),
                updated.GetProperty("startsAt").GetDateTimeOffset()
            );
        }
    }

    [Fact]
    public async Task ShouldListOnlyOwnBookingsForOrdinaryUsers()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        (HttpClient owner, _, _) = await _factory.AuthenticateAsUserAsync();
        (HttpClient stranger, _, _) = await _factory.AuthenticateAsUserAsync();

        using (owner)
        using (stranger)
        {
            using HttpResponseMessage booked = await BookAsync(owner, resourceId, slot);
            _ = booked.EnsureSuccessStatusCode();

            JsonElement mine = await owner.GetFromJsonAsync<JsonElement>(
                new Uri("/bookings/current", UriKind.Relative)
            );

            JsonElement theirs = await stranger.GetFromJsonAsync<JsonElement>(
                new Uri("/bookings/current", UriKind.Relative)
            );

            _ = Assert.Single(mine.EnumerateArray());
            Assert.Empty(theirs.EnumerateArray());

            using HttpResponseMessage refused = await stranger.GetAsync(
                new Uri("/bookings", UriKind.Relative)
            );

            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldConfirmExactlyOneBookingWhenManyCallersRaceForOneSlot()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        (Guid resourceId, DateTimeOffset slot) = await NewBookableResourceAsync(admin);

        const int contenders = 20;
        HttpClient[] clients = await Task.WhenAll(
            Enumerable
                .Range(0, contenders)
                .Select(async _ =>
                {
                    (HttpClient Client, string Email, string Password) authenticated =
                        await _factory.AuthenticateAsUserAsync();

                    return authenticated.Client;
                })
        );

        try
        {
            HttpResponseMessage[] responses = await Task.WhenAll(
                clients.Select(client => BookAsync(client, resourceId, slot))
            );

            try
            {
                int confirmed = responses.Count(x => x.StatusCode == HttpStatusCode.OK);
                int refused = responses.Count(x =>
                    x.StatusCode == HttpStatusCode.Conflict
                );

                Assert.Equal(1, confirmed);
                Assert.Equal(contenders - 1, refused);
            }
            finally
            {
                foreach (HttpResponseMessage response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            foreach (HttpClient client in clients)
            {
                client.Dispose();
            }
        }
    }
}
