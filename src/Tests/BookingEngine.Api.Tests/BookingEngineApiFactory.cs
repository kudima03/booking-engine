using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using JsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace BookingEngine.Api.Tests;

/// <summary>
/// Hosts the API in memory against throwaway PostgreSQL instances.
/// </summary>
/// <remarks>
/// The host applies its own migrations and seeds the roles on start, so the containers only
/// have to exist; no schema is created here.
/// </remarks>
public sealed class BookingEngineApiFactory
    : WebApplicationFactory<Program>,
        IAsyncLifetime
{
    /// <summary>
    /// Credentials of the administrator seeded by the host.
    /// </summary>
    public const string AdminEmail = "admin@booking-engine.test";

    public const string AdminPassword = "Adm1n!Password";

    private readonly PostgreSqlContainer _bookingDb = new PostgreSqlBuilder(
        "postgres:18-alpine"
    ).Build();

    private readonly PostgreSqlContainer _authDb = new PostgreSqlBuilder(
        "postgres:18-alpine"
    ).Build();

    /// <summary>
    /// Registers a fresh ordinary user and returns a client carrying their access token.
    /// </summary>
    /// <returns>The authenticated client and the credentials it was issued for.</returns>
    public async Task<(HttpClient Client, string Email, string Password)> AuthenticateAsUserAsync()
    {
        string email = $"{Guid.NewGuid():N}@booking-engine.test";
        const string password = "Us3r!Password";

        using (HttpClient anonymous = CreateClient())
        {
            using HttpResponseMessage registration = await anonymous.PostAsJsonAsync(
                new Uri("/auth/register", UriKind.Relative),
                new { email, password }
            );

            _ = registration.EnsureSuccessStatusCode();
        }

        return (await AuthenticateAsync(email, password), email, password);
    }

    /// <summary>
    /// Returns a client carrying the seeded administrator's access token.
    /// </summary>
    public Task<HttpClient> AuthenticateAsAdminAsync()
    {
        return AuthenticateAsync(AdminEmail, AdminPassword);
    }

    /// <summary>
    /// Signs in and returns a client with the resulting bearer token attached.
    /// </summary>
    /// <param name="email">Email address to sign in with.</param>
    /// <param name="password">Password to sign in with.</param>
    /// <returns>A client whose requests carry the access token.</returns>
    public async Task<HttpClient> AuthenticateAsync(string email, string password)
    {
        HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new { email, password }
        );

        _ = response.EnsureSuccessStatusCode();

        JsonElement token = await response.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.GetProperty("accessToken").GetString()
        );

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _ = builder.UseEnvironment(Environments.Development);

        _ = builder.ConfigureAppConfiguration(
            (_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:BookingDb"] = _bookingDb.GetConnectionString(),
                        ["ConnectionStrings:AuthDb"] = _authDb.GetConnectionString(),
                        ["Identity:Admin:Email"] = AdminEmail,
                        ["Identity:Admin:Password"] = AdminPassword,
                    }
                )
        );

        // Raw JSON literals in the assertions are easier to read than one long line.
        _ = builder.ConfigureTestServices(services =>
            services.Configure<JsonOptions>(options =>
                options.JsonSerializerOptions.WriteIndented = true
            )
        );
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_bookingDb.StartAsync(), _authDb.StartAsync());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _bookingDb.DisposeAsync();
        await _authDb.DisposeAsync();
    }
}
