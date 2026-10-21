using System.Net;
using System.Net.Http.Headers;
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

    /// <summary>Applies the original scheme/subdomain/port rules rather than the default policy.</summary>
    [Theory]
    [InlineData("http://quote.maliev.com", true)]
    [InlineData("https://nested.quote.maliev.com", true)]
    [InlineData("https://maliev.com", false)]
    [InlineData("https://quote.maliev.com:444", false)]
    [InlineData("https://quote.maliev.com.evil.test", false)]
    [InlineData("https://example.test", false)]
    public async Task GetCountries_OriginBoundary_PreservesSourceWildcardPolicy(
        string origin, bool expectedAllowed)
    {
        await fixture.SeedAsync("Thailand", "TH", "THA");
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://countries.maliev.com"),
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Countries");
        request.Headers.Add("Origin", origin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedAllowed ? origin : null,
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
                ? Assert.Single(values)
                : null);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    /// <summary>Exercises real preflight before authorization without allowing credentials.</summary>
    [Theory]
    [InlineData("https://quote.maliev.com", true)]
    [InlineData("https://untrusted.test", false)]
    public async Task UpdatePreflight_OriginBoundary_PreservesMethodsHeadersAndNoCredentials(
        string origin, bool expectedAllowed)
    {
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://countries.maliev.com"),
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/Countries/1");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "x-country-probe,content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(expectedAllowed ? origin : null,
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
                ? Assert.Single(values)
                : null);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        if (expectedAllowed)
        {
            Assert.Equal("PUT", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods")));
            var allowedHeaders = response.Headers.GetValues("Access-Control-Allow-Headers")
                .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { "content-type", "x-country-probe" }, allowedHeaders);
        }
        else
        {
            Assert.False(response.Headers.Contains("Access-Control-Allow-Methods"));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Headers"));
        }
    }

    /// <summary>CORS must not turn anonymous or ungranted protected reads into successful reads.</summary>
    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task ProtectedRead_AllowedOrigin_Preserves401And403WithoutCredentials(
        bool authenticatedWithoutPermission, HttpStatusCode expectedStatus)
    {
        var id = await fixture.SeedAsync("Thailand", "TH", "THA");
        await using var baseFactory = fixture.CreateDocumentationFactory();
        await using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://countries.maliev.com"),
            AllowAutoRedirect = false
        });
        if (authenticatedWithoutPermission)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", fixture.Token("valid"));
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Countries/{id}");
        request.Headers.Add("Origin", "https://quote.maliev.com");

        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("https://quote.maliev.com",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Equal("max-age=2592000",
            Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }
}
