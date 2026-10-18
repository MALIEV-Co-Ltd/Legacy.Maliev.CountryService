using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
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

    /// <summary>Preserves native configured-port discovery, status, path and query.</summary>
    [Theory]
    [InlineData("7443", "7444", "https://countries.maliev.com:7443/Countries?label=source%20path&quantity=1")]
    [InlineData("", "7444", "https://countries.maliev.com:7444/Countries?label=source%20path&quantity=1")]
    public async Task GetCountries_HttpWithDiscoveredHttpsPort_Redirects307PreservingPathAndQuery(
        string httpsPort, string ancmHttpsPort, string expectedLocation)
    {
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("HTTPS_PORT", httpsPort);
            builder.UseSetting("ANCM_HTTPS_PORT", ancmHttpsPort);
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://countries.maliev.com"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/Countries?label=source%20path&quantity=1");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    /// <summary>Preserves native pass-through when no HTTPS port can be discovered.</summary>
    [Fact]
    public async Task GetCountries_HttpWithoutHttpsPort_ReturnsCountryWithoutInventedRedirect()
    {
        var id = await fixture.SeedAsync("Thailand", "TH", "THA");
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("HTTPS_PORT", "");
            builder.UseSetting("ANCM_HTTPS_PORT", "");
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://countries.maliev.com"),
            AllowAutoRedirect = false
        });
        var addresses = app.Server.Features.Get<IServerAddressesFeature>();
        if (addresses is not null)
        {
            Assert.DoesNotContain(addresses.Addresses,
                address => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        using var response = await client.GetAsync("/Countries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        var countries = await response.Content.ReadFromJsonAsync<CountryResponse[]>();
        Assert.NotNull(countries);
        var country = Assert.Single(countries);
        Assert.Equal(id, country.Id);
        Assert.Equal("Thailand", country.Name);
    }

    /// <summary>Preserves source environment gating and native loopback exclusions.</summary>
    [Theory]
    [InlineData("Development", "https://countries.maliev.com", false)]
    [InlineData("Staging", "https://countries.maliev.com", true)]
    [InlineData("Production", "https://localhost", false)]
    [InlineData("Production", "https://127.0.0.1", false)]
    [InlineData("Production", "https://[::1]", false)]
    public async Task GetCountries_HttpsEnvironmentAndHost_PreserveHstsBoundary(
        string environment, string address, bool expectedHsts)
    {
        await fixture.SeedAsync("Thailand", "TH", "THA");
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(address),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/Countries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedHsts ? "max-age=2592000" : null,
            response.Headers.TryGetValues("Strict-Transport-Security", out var values)
                ? Assert.Single(values)
                : null);
    }

    /// <summary>The existing quiet probe rejection remains ahead of redirects and CORS.</summary>
    [Fact]
    public async Task DiagnosticProbe_MissingAdmissionHeader_RemainsQuiet404BeforeTransport()
    {
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("HTTPS_PORT", "7443");
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://countries.maliev.com"),
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/diagnostics/observability");
        request.Headers.Add("Origin", "https://quote.maliev.com");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
