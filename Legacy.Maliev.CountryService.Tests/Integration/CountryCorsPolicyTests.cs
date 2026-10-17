using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.CountryService.Tests.Integration;

/// <summary>Exercises source cross-origin behavior through the actual Country application.</summary>
/// <param name="fixture">Owns the existing PostgreSQL and application fixture.</param>
[Collection("Country lifecycle external configuration")]
public sealed class CountryCorsPolicyTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    /// <summary>Resets the existing fixture before the HTTP case.</summary>
    public Task InitializeAsync() => fixture.ResetAsync();

    /// <summary>Leaves fixture-owned resources to their existing owner.</summary>
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Requires an allowed MALIEV subdomain without credential permission.</summary>
    [Fact]
    public async Task GetCountries_MalievSubdomainOrigin_AllowsOriginWithoutCredentials()
    {
        var id = await fixture.SeedAsync("Thailand", "TH", "THA");
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://countries.maliev.com"),
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Countries");
        request.Headers.Add("Origin", "https://quote.maliev.com");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var countries = await response.Content.ReadFromJsonAsync<CountryResponse[]>();
        Assert.NotNull(countries);
        var country = Assert.Single(countries);
        Assert.Equal(id, country.Id);
        Assert.Equal("Thailand", country.Name);
        Assert.Equal("https://quote.maliev.com",
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
                ? Assert.Single(values)
                : null);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
