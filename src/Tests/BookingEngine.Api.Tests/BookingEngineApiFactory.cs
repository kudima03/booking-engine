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
/// The host applies its own migrations on start, so the containers only have to exist;
/// no schema is created here.
/// </remarks>
public sealed class BookingEngineApiFactory
    : WebApplicationFactory<Program>,
        IAsyncLifetime
{
    private readonly PostgreSqlContainer _bookingDb = new PostgreSqlBuilder(
        "postgres:18-alpine"
    ).Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _ = builder.UseEnvironment(Environments.Development);

        _ = builder.ConfigureAppConfiguration(
            (_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:BookingDb"] = _bookingDb.GetConnectionString(),
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
        await _bookingDb.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _bookingDb.DisposeAsync();
    }
}
