using System.Net;

namespace BookingEngine.Api.Tests;

[Collection(nameof(BookingEngineApiTestSet))]
public sealed record HealthEndpointsTests
{
    private readonly HttpClient _client;

    public HealthEndpointsTests(BookingEngineApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ShouldReportHealthyWhenProbingAliveness()
    {
        using HttpResponseMessage response = await _client.GetAsync(
            new Uri("/alive", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ShouldReportHealthyWhenProbingHealth()
    {
        using HttpResponseMessage response = await _client.GetAsync(
            new Uri("/health", UriKind.Relative)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
