using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CountryService.Tests.Integration;

/// <summary>Real authenticated PostgreSQL checks for the pinned legacy identifier contract.</summary>
[Collection("Country lifecycle external configuration")]
public sealed class CountryLegacyIdentifierHttpTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    /// <inheritdoc />
    public Task InitializeAsync() => fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Zero remains invalid; missing negative identifiers preserve source404 without writes.</summary>
    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(-1, HttpStatusCode.NotFound)]
    [InlineData(int.MinValue, HttpStatusCode.NotFound)]
    public async Task UpdateLegacyCountry_NonpositiveIdentifier_PreservesSourceStatusAndStoredFields(
        int identifier, HttpStatusCode expected)
    {
        var storedId = await fixture.SeedAsync("Thailand", "TH", "THA");
        await using var beforeContext = fixture.CreateContext();
        var before = await beforeContext.Countries.AsNoTracking().SingleAsync();
        using var client = fixture.CreateClient(CountryPermissions.CountriesUpdate);

        using var response = await client.PutAsJsonAsync($"/Countries/{identifier}",
            new UpsertCountryRequest("Must not replace", "Asia", "764", "TH", "THA"));

        Assert.Equal(expected, response.StatusCode);
        await using var afterContext = fixture.CreateContext();
        var after = await afterContext.Countries.AsNoTracking().SingleAsync();
        Assert.Equal(storedId, after.Id);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Continent, after.Continent);
        Assert.Equal(before.CountryCode, after.CountryCode);
        Assert.Equal(before.Iso2, after.Iso2);
        Assert.Equal(before.Iso3, after.Iso3);
        Assert.Equal(before.CreatedDate, after.CreatedDate);
        Assert.Equal(before.ModifiedDate, after.ModifiedDate);
    }
}

/// <summary>Real HTTP regression for source native Int32 routes; no replacement binder or IAM authority.</summary>
[Collection("Country lifecycle external configuration")]
public sealed class CountryLegacyRouteBindingHttpTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    private const string CacheKey = "legacy:country:all:v1";
    private static readonly string[] Verbs = ["GET", "PUT", "DELETE"];
    private static readonly string[] Invalid = ["not-an-integer", "2147483648", "-2147483649", "1.0", "1e0", "1,000", "0x100000000"];
    private static readonly string[] Compatible = ["1", "+1", " 1 ", " +1 ", "01", "0x1", "0X1", "&h1", "&H1", "#1"];

    /// <inheritdoc />
    public Task InitializeAsync() => fixture.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Constraint regression must not turn source binding400 into routing404.</summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("fr-FR")]
    public async Task InvalidRoute_AllLegacyVerbs_ReturnNative400WithoutDatabaseOrCacheMutation(string culture)
    {
        await fixture.SeedAsync("Before", "TH", "THA");
        var before = await SnapshotAsync();
        var boundary = new RouteIamTransport(allowed: true);
        await using var app = App(culture, boundary);
        using var client = Client(app, authorized: true);
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        byte[] marker = [57, 1, 3, 8];
        await cache.SetAsync(CacheKey, marker);
        foreach (var verb in Verbs)
        {
            foreach (var text in Invalid)
            {
                using var response = await SendAsync(client, verb, text);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
                Assert.Contains(problem.RootElement.GetProperty("errors").EnumerateObject(),
                    error => error.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(before, await SnapshotAsync());
                Assert.Equal(marker, await cache.GetAsync(CacheKey));
            }
        }
        Assert.Equal(Invalid.Length, boundary.LiveCalls);
    }

    /// <summary>Compatible source signs, whitespace and hexadecimal forms reach the real actions.</summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("fr-FR")]
    public async Task CompatibleRoute_ReadAndUpdate_BindSourceInt32UnderInvariantRouteCulture(string culture)
    {
        await using var app = App(culture, new RouteIamTransport(allowed: true));
        using var client = Client(app, authorized: true);
        foreach (var text in Compatible)
        {
            await fixture.ResetAsync();
            var id = await fixture.SeedAsync("Before", "TH", "THA");
            Assert.Equal(1, id);
            await using var beforeContext = fixture.CreateContext();
            var before = await beforeContext.Countries.AsNoTracking().SingleAsync();
            using var read = await SendAsync(client, "GET", text);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var wire = await read.Content.ReadFromJsonAsync<CountryResponse>();
            Assert.NotNull(wire);
            Assert.Equal(id, wire.Id);
            using var update = await SendAsync(client, "PUT", text);
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
            await using var afterContext = fixture.CreateContext();
            var after = await afterContext.Countries.AsNoTracking().SingleAsync();
            Assert.Equal(id, after.Id);
            Assert.Equal("Changed", after.Name);
            Assert.Equal("Asia", after.Continent);
            Assert.Equal("764", after.CountryCode);
            Assert.Equal("TH", after.Iso2);
            Assert.Equal("THA", after.Iso3);
            Assert.Equal(before.CreatedDate, after.CreatedDate);
            Assert.NotNull(after.ModifiedDate);
        }
        foreach (var verb in new[] { "GET", "PUT" })
        {
            foreach (var text in new[] { "2147483647", "-2147483648", "-1", "0", "-0", "+0" })
            {
                using var response = await SendAsync(client, verb, text);
                Assert.Equal(verb == "PUT" && (text == "0" || text == "-0" || text == "+0")
                    ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound, response.StatusCode);
            }
        }
    }

    /// <summary>Authentication and current permission gates run before native invalid-id validation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidRoute_AnonymousOrDenied_Preserves401403BeforeBindingWithoutMutation(bool authenticated)
    {
        await fixture.SeedAsync("Before", "TH", "THA");
        var before = await SnapshotAsync();
        var boundary = new RouteIamTransport(allowed: false);
        await using var app = App("th-TH", boundary);
        using var client = Client(app, authorized: false, anonymous: !authenticated);
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        byte[] marker = [57, 4, 0, 3];
        await cache.SetAsync(CacheKey, marker);
        foreach (var verb in Verbs)
        {
            foreach (var text in new[] { "not-an-integer", "2147483648", "0x1" })
            {
                using var response = await SendAsync(client, verb, text);
                Assert.Equal(authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
                if (!authenticated)
                    Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
                Assert.Equal(before, await SnapshotAsync());
                Assert.Equal(marker, await cache.GetAsync(CacheKey));
            }
        }
        if (authenticated) Assert.True(boundary.Calls > 0);
        else Assert.Equal(0, boundary.Calls);
        Assert.Equal(authenticated ? 3 : 0, boundary.LiveCalls);
    }

    private WebApplicationFactory<Program> App(string culture, RouteIamTransport boundary)
    {
        var credential = Guid.NewGuid().ToString("N");
        boundary.LiveCredential = credential;
        return fixture.CreateDocumentationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IAM:LivePermissionChecks:Credential", credential);
            CountryTestWorkloadExchange.Prepare(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new RouteCulture(culture));
                services.AddHttpClient("IAMService", http => http.BaseAddress = new Uri("https://country57-iam.invalid"))
                    .ConfigurePrimaryHttpMessageHandler(() => boundary);
            });
        });
    }

    private HttpClient Client(WebApplicationFactory<Program> app, bool authorized, bool anonymous = false)
    {
        var client = app.CreateClient();
        if (!anonymous)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                authorized ? fixture.Token("valid", CountryPermissions.CountriesRead,
                    CountryPermissions.CountriesUpdate, CountryPermissions.CountriesDelete) : fixture.Token("valid"));
        return client;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string verb, string text)
    {
        var path = "/Countries/" + Uri.EscapeDataString(text);
        return verb switch
        {
            "GET" => client.GetAsync(path),
            "PUT" => client.PutAsJsonAsync(path, new UpsertCountryRequest("Changed", "Asia", "764", "TH", "THA")),
            "DELETE" => client.DeleteAsync(path),
            _ => throw new ArgumentOutOfRangeException(nameof(verb))
        };
    }

    private async Task<string> SnapshotAsync()
    {
        await using var context = fixture.CreateContext();
        return JsonSerializer.Serialize(await context.Countries.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.Continent, row.CountryCode, row.Iso2, row.Iso3,
                row.CreatedDate, row.ModifiedDate, Version = EF.Property<uint>(row, "xmin") }).ToArrayAsync());
    }

    private sealed class RouteCulture(string name) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (_, continuation) =>
            {
                var previous = CultureInfo.CurrentCulture;
                var previousUi = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                    await continuation();
                }
                finally
                {
                    CultureInfo.CurrentCulture = previous;
                    CultureInfo.CurrentUICulture = previousUi;
                }
            });
            next(app);
        };
    }

    private sealed class RouteIamTransport(bool allowed) : HttpMessageHandler
    {
        internal string LiveCredential = "";
        internal int Calls, LiveCalls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/iam/v1/auth/check-permission", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = json.RootElement;
            Assert.Equal("employee:country-acceptance", root.GetProperty("principalId").GetString());
            Assert.Equal("global", root.GetProperty("resourcePath").GetString());
            var permission = root.GetProperty("permissionId").GetString();
            Assert.NotNull(permission);
            Assert.Contains(permission, new[] { CountryPermissions.CountriesRead, CountryPermissions.CountriesUpdate, CountryPermissions.CountriesDelete });
            var live = permission == CountryPermissions.CountriesDelete;
            Assert.Equal(live, root.GetProperty("bypassCache").GetBoolean());
            Assert.Equal(live, request.Headers.Contains("X-Maliev-IAM-Live-Check-Key"));
            if (live)
            {
                LiveCalls++;
                Assert.Equal(LiveCredential, Assert.Single(request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key")));
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { allowed }) };
        }
    }
}

/// <summary>Controlled external live IAM with unchanged normal JWT, permission, PostgreSQL and Redis composition.</summary>
[Collection("Country lifecycle external configuration")]
public sealed class CountryLegacyRouteLiveDeleteHttpTests(CountryNormalIamFixture fixture)
    : IClassFixture<CountryNormalIamFixture>
{
    /// <summary>Native source forms cannot bypass critical live permission and invalidate cache only after deletion.</summary>
    [Theory]
    [InlineData("decimal")]
    [InlineData("plus")]
    [InlineData("space")]
    [InlineData("0x")]
    [InlineData("&h")]
    [InlineData("#")]
    public async Task CompatibleDelete_NormalLivePermission_DeletesOnlyBoundRowAndInvalidatesCache(string form)
    {
        var row = await fixture.SeedAsync();
        var kept = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        await using var app = fixture.App(boundary);
        using var client = fixture.Client(app);
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        const string cacheKey = "legacy:country:all:v1";
        await cache.SetAsync(cacheKey, new byte[] { 57, 2, 0, 4 });
        var text = form switch
        {
            "plus" => "+" + row.Id.ToString(CultureInfo.InvariantCulture),
            "space" => " " + row.Id.ToString(CultureInfo.InvariantCulture) + " ",
            "0x" or "&h" or "#" => form + row.Id.ToString("X", CultureInfo.InvariantCulture),
            _ => row.Id.ToString(CultureInfo.InvariantCulture)
        };
        using var response = await client.DeleteAsync("/Countries/" + Uri.EscapeDataString(text));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, boundary.LoginCalls);
        Assert.Equal(1, boundary.IamCalls);
        await using var context = fixture.Context();
        Assert.False(await context.Countries.AnyAsync(value => value.Id == row.Id));
        await fixture.AssertUnchangedAsync(kept);
        Assert.Null(await cache.GetAsync(cacheKey));
    }

    /// <summary>Real PostgreSQL/xmin and Redis survive anonymous, denied and allowed malformed DELETE requests.</summary>
    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("denied", HttpStatusCode.Forbidden)]
    [InlineData("allow", HttpStatusCode.BadRequest)]
    public async Task InvalidDelete_NormalComposition_OrdersAuthorizationBeforeBindingWithoutMutation(string mode, HttpStatusCode expected)
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary { Mode = mode == "denied" ? "denied" : "allow" };
        await using var app = fixture.App(boundary);
        using var client = fixture.Client(app, profile: mode == "anonymous" ? "anonymous" : "employee");
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        const string cacheKey = "legacy:country:all:v1";
        byte[] marker = [57, 4, 0, 0];
        await cache.SetAsync(cacheKey, marker);
        foreach (var text in new[] { "not-an-integer", "2147483648", "-2147483649" })
        {
            using var response = await client.DeleteAsync("/Countries/" + Uri.EscapeDataString(text));
            Assert.Equal(expected, response.StatusCode);
            await fixture.AssertUnchangedAsync(row);
            Assert.Equal(marker, await cache.GetAsync(cacheKey));
        }
        Assert.Equal(mode == "anonymous" ? 0 : 3, boundary.IamCalls);
        if (mode == "anonymous") Assert.Equal(0, boundary.LoginCalls);
        else Assert.True(boundary.LoginCalls > 0);
    }

    /// <summary>Typed zero, negative and boundary identifiers remain missing rather than malformed for DELETE.</summary>
    [Fact]
    public async Task MissingDelete_NormalLivePermission_Preserves404AndStoredVersionAndCache()
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        await using var app = fixture.App(boundary);
        using var client = fixture.Client(app);
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        const string cacheKey = "legacy:country:all:v1";
        byte[] marker = [57, 4, 0, 4];
        await cache.SetAsync(cacheKey, marker);
        foreach (var text in new[] { "0", "-1", "-2147483648", "2147483647" })
        {
            using var response = await client.DeleteAsync("/Countries/" + text);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await fixture.AssertUnchangedAsync(row);
            Assert.Equal(marker, await cache.GetAsync(cacheKey));
        }
        Assert.Equal(4, boundary.IamCalls);
    }
}
