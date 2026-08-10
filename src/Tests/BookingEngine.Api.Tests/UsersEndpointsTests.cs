using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingEngine.Infrastructure.Auth;

namespace BookingEngine.Api.Tests;

[Collection(nameof(BookingEngineApiTestSet))]
public sealed record UsersEndpointsTests
{
    private readonly BookingEngineApiFactory _factory;

    public UsersEndpointsTests(BookingEngineApiFactory factory)
    {
        _factory = factory;
    }

    private static async Task<JsonElement> CurrentAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/users/current", UriKind.Relative)
        );

        _ = response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task ShouldReturnOwnAccountWhenReadingCurrentUser()
    {
        (HttpClient client, string email, _) = await _factory.AuthenticateAsUserAsync();

        using (client)
        {
            JsonElement user = await CurrentAsync(client);

            Assert.Equal(email, user.GetProperty("email").GetString());
            Assert.False(user.GetProperty("isBlocked").GetBoolean());
            Assert.Equal(
                [KnownRoles.User],
                user.GetProperty("roles").EnumerateArray().Select(x => x.GetString())
            );
        }
    }

    [Fact]
    public async Task ShouldCompleteProfileWhenPatchingCurrentUser()
    {
        (HttpClient client, _, _) = await _factory.AuthenticateAsUserAsync();

        using (client)
        {
            using HttpResponseMessage response = await client.PatchAsJsonAsync(
                new Uri("/users/current", UriKind.Relative),
                new { name = "Ada", surname = "Lovelace", phone = "+441234567890" }
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            JsonElement user = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal("Ada", user.GetProperty("name").GetString());
            Assert.Equal("Lovelace", user.GetProperty("surname").GetString());
            Assert.Equal("+441234567890", user.GetProperty("phone").GetString());
        }
    }

    [Fact]
    public async Task ShouldKeepExistingFieldsWhenPatchingWithNulls()
    {
        (HttpClient client, _, _) = await _factory.AuthenticateAsUserAsync();

        using (client)
        {
            using HttpResponseMessage seed = await client.PatchAsJsonAsync(
                new Uri("/users/current", UriKind.Relative),
                new { name = "Grace", surname = "Hopper", phone = "+15550000000" }
            );

            _ = seed.EnsureSuccessStatusCode();

            using HttpResponseMessage response = await client.PatchAsJsonAsync(
                new Uri("/users/current", UriKind.Relative),
                new { name = "Grace Brewster" }
            );

            JsonElement user = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal("Grace Brewster", user.GetProperty("name").GetString());
            Assert.Equal("Hopper", user.GetProperty("surname").GetString());
            Assert.Equal("+15550000000", user.GetProperty("phone").GetString());
        }
    }

    [Fact]
    public async Task ShouldRejectCurrentUserWhenNotAuthenticated()
    {
        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri("/users/current", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShouldListUsersWhenCallerIsAdministrator()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        using HttpResponseMessage response = await admin.GetAsync(
            new Uri("/users", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement users = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains(
            users.EnumerateArray(),
            x => x.GetProperty("email").GetString() == BookingEngineApiFactory.AdminEmail
        );
    }

    [Fact]
    public async Task ShouldRejectListingUsersWhenCallerIsNotAdministrator()
    {
        (HttpClient client, _, _) = await _factory.AuthenticateAsUserAsync();

        using (client)
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/users", UriKind.Relative)
            );

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShouldReturn404WhenUserDoesNotExist()
    {
        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        using HttpResponseMessage response = await admin.GetAsync(
            new Uri($"/users/{Guid.NewGuid()}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ShouldRefuseSignInAfterBlockingUser()
    {
        (HttpClient client, string email, string password) =
            await _factory.AuthenticateAsUserAsync();

        Guid id = (await CurrentAsync(client)).GetProperty("id").GetGuid();
        client.Dispose();

        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        using HttpResponseMessage blocked = await admin.PostAsync(
            new Uri($"/users/{id}/block", UriKind.Relative),
            content: null
        );

        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);

        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage signIn = await anonymous.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new { email, password }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
    }

    [Fact]
    public async Task ShouldReportBlockedStateAndAllowSignInAgainAfterUnblocking()
    {
        (HttpClient client, string email, string password) =
            await _factory.AuthenticateAsUserAsync();

        Guid id = (await CurrentAsync(client)).GetProperty("id").GetGuid();
        client.Dispose();

        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();

        using (
            HttpResponseMessage blocked = await admin.PostAsync(
                new Uri($"/users/{id}/block", UriKind.Relative),
                content: null
            )
        )
        {
            _ = blocked.EnsureSuccessStatusCode();
        }

        using (
            HttpResponseMessage state = await admin.GetAsync(
                new Uri($"/users/{id}", UriKind.Relative)
            )
        )
        {
            JsonElement user = await state.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(user.GetProperty("isBlocked").GetBoolean());
        }

        using (
            HttpResponseMessage unblocked = await admin.DeleteAsync(
                new Uri($"/users/{id}/block", UriKind.Relative)
            )
        )
        {
            Assert.Equal(HttpStatusCode.OK, unblocked.StatusCode);
        }

        using HttpClient restored = await _factory.AuthenticateAsync(email, password);

        Assert.NotNull(restored.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task ShouldGrantAndRevokeAdministratorRole()
    {
        (HttpClient client, string email, string password) =
            await _factory.AuthenticateAsUserAsync();

        Guid id = (await CurrentAsync(client)).GetProperty("id").GetGuid();
        client.Dispose();

        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();

        using (
            HttpResponseMessage granted = await admin.PostAsync(
                new Uri($"/users/{id}/roles/{KnownRoles.Admin}", UriKind.Relative),
                content: null
            )
        )
        {
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        }

        // The role only reaches the caller's principal once a new token is issued.
        using (HttpClient promoted = await _factory.AuthenticateAsync(email, password))
        {
            using HttpResponseMessage listed = await promoted.GetAsync(
                new Uri("/users", UriKind.Relative)
            );

            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        }

        using (
            HttpResponseMessage revoked = await admin.DeleteAsync(
                new Uri($"/users/{id}/roles/{KnownRoles.Admin}", UriKind.Relative)
            )
        )
        {
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        }

        using HttpClient demoted = await _factory.AuthenticateAsync(email, password);
        using HttpResponseMessage refused = await demoted.GetAsync(
            new Uri("/users", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task ShouldReturn400WhenRoleIsNotRecognised()
    {
        (HttpClient client, _, _) = await _factory.AuthenticateAsUserAsync();
        Guid id = (await CurrentAsync(client)).GetProperty("id").GetGuid();
        client.Dispose();

        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();
        using HttpResponseMessage response = await admin.PostAsync(
            new Uri($"/users/{id}/roles/Superuser", UriKind.Relative),
            content: null
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldNotExposeAWayToHardDeleteAUser()
    {
        (HttpClient client, _, _) = await _factory.AuthenticateAsUserAsync();
        Guid id = (await CurrentAsync(client)).GetProperty("id").GetGuid();
        client.Dispose();

        using HttpClient admin = await _factory.AuthenticateAsAdminAsync();

        using HttpResponseMessage response = await admin.DeleteAsync(
            new Uri($"/users/{id}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        using HttpResponseMessage stillThere = await admin.GetAsync(
            new Uri($"/users/{id}", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }
}
