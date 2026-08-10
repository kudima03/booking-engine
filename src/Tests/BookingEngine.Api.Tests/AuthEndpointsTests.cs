using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingEngine.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BookingEngine.Api.Tests;

[Collection(nameof(BookingEngineApiTestSet))]
public sealed record AuthEndpointsTests
{
    private readonly BookingEngineApiFactory _factory;

    public AuthEndpointsTests(BookingEngineApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ShouldIssueAccessTokenWhenRegisteringThenSigningIn()
    {
        (HttpClient client, _, _) = await _factory.AuthenticateAsUserAsync();

        using (client)
        {
            Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
            Assert.False(
                string.IsNullOrEmpty(client.DefaultRequestHeaders.Authorization?.Parameter)
            );
        }
    }

    [Fact]
    public async Task ShouldRejectSignInWhenPasswordIsWrong()
    {
        (HttpClient client, string email, _) = await _factory.AuthenticateAsUserAsync();
        client.Dispose();

        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new { email, password = "Wr0ng!Password" }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShouldRejectRegistrationWhenPasswordIsTooWeak()
    {
        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new { email = $"{Guid.NewGuid():N}@booking-engine.test", password = "short" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldReturnCallerEmailWhenReadingManagedInfo()
    {
        (HttpClient client, string email, _) = await _factory.AuthenticateAsUserAsync();

        using (client)
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/auth/manage/info", UriKind.Relative)
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            JsonElement info = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(email, info.GetProperty("email").GetString());
        }
    }

    [Fact]
    public async Task ShouldRejectManagedInfoWhenNotAuthenticated()
    {
        using HttpClient anonymous = _factory.CreateClient();
        using HttpResponseMessage response = await anonymous.GetAsync(
            new Uri("/auth/manage/info", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShouldGrantUserRoleOnRegistration()
    {
        (HttpClient client, string email, _) = await _factory.AuthenticateAsUserAsync();
        client.Dispose();

        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<
            UserManager<ApplicationUser>
        >();

        ApplicationUser user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("The registered user was not stored.");

        Assert.Equal([KnownRoles.User], await users.GetRolesAsync(user));
    }

    [Fact]
    public async Task ShouldSeedAdministratorWithBothRoles()
    {
        using HttpClient client = await _factory.AuthenticateAsAdminAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<
            UserManager<ApplicationUser>
        >();

        ApplicationUser administrator = await users.FindByEmailAsync(
            BookingEngineApiFactory.AdminEmail
        ) ?? throw new InvalidOperationException("The administrator was not seeded.");

        Assert.Equal(
            [KnownRoles.Admin, KnownRoles.User],
            (await users.GetRolesAsync(administrator)).Order()
        );
    }

    [Fact]
    public async Task ShouldSeedEveryKnownRole()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RoleManager<ApplicationRole> roles = scope.ServiceProvider.GetRequiredService<
            RoleManager<ApplicationRole>
        >();

        foreach (string role in KnownRoles.All)
        {
            Assert.True(await roles.RoleExistsAsync(role), $"Role '{role}' was not seeded.");
        }
    }
}
