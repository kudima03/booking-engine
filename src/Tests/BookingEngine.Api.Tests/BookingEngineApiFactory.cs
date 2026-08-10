using Microsoft.AspNetCore.Mvc.Testing;

namespace BookingEngine.Api.Tests;

/// <summary>
/// Hosts the API in memory for integration tests.
/// </summary>
public sealed class BookingEngineApiFactory : WebApplicationFactory<Program>;
