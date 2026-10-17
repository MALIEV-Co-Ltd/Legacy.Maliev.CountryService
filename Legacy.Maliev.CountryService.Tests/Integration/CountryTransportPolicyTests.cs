using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.CountryService.Tests.Integration;

/// <summary>Exercises source transport behavior through the actual Country application.</summary>
/// <param name="fixture">Owns the existing PostgreSQL and application fixture.</param>
[Collection("Country lifecycle external configuration")]
public sealed class CountryTransportPolicyTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    /// <summary>Resets the existing fixture before the HTTP case.</summary>
    public Task InitializeAsync() => fixture.ResetAsync();

    /// <summary>Leaves fixture-owned resources to their existing owner.</summary>
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Requires the source non-Development HSTS response on a public HTTPS host.</summary>
    [Fact]
    public async Task GetCountries_ProductionHttps_EmitsSourceHstsHeader()
    {
        var id = await fixture.SeedAsync("Thailand", "TH", "THA");
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://countries.maliev.com"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/Countries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var countries = await response.Content.ReadFromJsonAsync<CountryResponse[]>();
        Assert.NotNull(countries);
        var country = Assert.Single(countries);
        Assert.Equal(id, country.Id);
        Assert.Equal("Thailand", country.Name);
        Assert.Equal("max-age=2592000",
            response.Headers.TryGetValues("Strict-Transport-Security", out var values)
                ? Assert.Single(values)
                : null);
    }
}
