using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CountryService.Tests.Integration;

public sealed class ApiCompatibilityTests
{
    [Fact]
    public async Task GetCountries_Anonymous_ReturnsLegacyRouteAndShape()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder
                .UseEnvironment("Testing")
                .UseSetting(
                    "ConnectionStrings:CountryDbContext",
                    "Host=localhost;Database=unused;Username=unused;Password=unused")
                .UseSetting("CORS:AllowedOrigins:0", "https://example.test")
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ICountryService>(new StubCountryService());
                }));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Countries");
        var countries = await response.Content.ReadFromJsonAsync<CountryResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(countries);
        Assert.Equal("Thailand", Assert.Single(countries).Name);
    }

    [Fact]
    public async Task ScalarRoute_IsPublishedWithoutSwagger()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder
                .UseEnvironment("Testing")
                .UseSetting(
                    "ConnectionStrings:CountryDbContext",
                    "Host=localhost;Database=unused;Username=unused;Password=unused")
                .UseSetting("CORS:AllowedOrigins:0", "https://example.test"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/countries/scalar");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVersionedCountries_ReturnsModernVersionedRoute()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder
                .UseEnvironment("Testing")
                .UseSetting(
                    "ConnectionStrings:CountryDbContext",
                    "Host=localhost;Database=unused;Username=unused;Password=unused")
                .UseSetting("CORS:AllowedOrigins:0", "https://example.test")
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ICountryService>(new StubCountryService());
                }));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/country/v1/countries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class StubCountryService : ICountryService
    {
        public Task<IReadOnlyList<CountryResponse>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CountryResponse>>(
                [new CountryResponse(7, "Thailand", "Asia", "764", "TH", "THA", null, null)]);

        public Task<CountryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<CountryResponse?>(null);

        public Task<CountryResponse> CreateAsync(
            UpsertCountryRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateAsync(
            int id,
            UpsertCountryRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
