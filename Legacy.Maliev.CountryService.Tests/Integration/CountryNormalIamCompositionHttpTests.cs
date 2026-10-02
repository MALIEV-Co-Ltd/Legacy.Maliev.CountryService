using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.CountryService.Data;
using Legacy.Maliev.CountryService.Domain;
using Maliev.Aspire.ServiceDefaults.IAM;
using Maliev.Aspire.ServiceDefaults.LegacyAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CountryService.Tests.Integration;

/// <summary>Normal production composition with real JWT/EF and controlled external HTTP, never fixture IAM registration.</summary>
public sealed class CountryNormalIamCompositionHttpTests(CountryNormalIamFixture fixture)
    : IClassFixture<CountryNormalIamFixture>
{
    [Theory]
    [InlineData("IAMService", "Production", "https://country40-iam.invalid", "https://country40-iam.invalid/")]
    [InlineData("IAMService", "Production", "https+http://IAMService", "https+http://iamservice/")]
    [InlineData("IAMService", "Production", "https+http://LEGACY-MALIEV-IAM-SERVICE", "https+http://legacy-maliev-iam-service/")]
    [InlineData("LegacyAuthServiceTokenExchange", "Production", "https://country40-auth.invalid", "https://country40-auth.invalid/")]
    [InlineData("IAMService", "Testing", "http://127.0.0.1:12345", "http://127.0.0.1:12345/")]
    [InlineData("LegacyAuthServiceTokenExchange", "Development", "http://[::1]:12345", "http://[::1]:12345/")]
    public void NamedOrigin_ApprovedOriginHasBoundedNormalClient(string name, string environment, string origin, string expected)
    {
        using var app = fixture.App(new(), originName: name, origin: origin, environment: environment);
        using var bootstrap = app.CreateClient();
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        Assert.Equal(expected, client.BaseAddress?.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
    }

    [Theory]
    [InlineData("IAMService", "")]
    [InlineData("IAMService", "https://user:pass@country40-iam.invalid")]
    [InlineData("IAMService", "https://country40-iam.invalid/path")]
    [InlineData("IAMService", "https://country40-iam.invalid/?key=value")]
    [InlineData("IAMService", "https://country40-iam.invalid/#fragment")]
    [InlineData("IAMService", " https://country40-iam.invalid")]
    [InlineData("IAMService", "http://127.0.0.1:12345")]
    [InlineData("IAMService", "https+http://remote.invalid")]
    [InlineData("IAMService", "https+http://IAMService:12345")]
    [InlineData("IAMService", "https+http://IAMService/.")]
    [InlineData("IAMService", "https+http://IAMService\\")]
    [InlineData("LegacyAuthServiceTokenExchange", "")]
    [InlineData("LegacyAuthServiceTokenExchange", "https://user:pass@country40-auth.invalid")]
    [InlineData("LegacyAuthServiceTokenExchange", "https://country40-auth.invalid/path")]
    [InlineData("LegacyAuthServiceTokenExchange", "https://country40-auth.invalid/?key=value")]
    [InlineData("LegacyAuthServiceTokenExchange", "https://country40-auth.invalid/#fragment")]
    [InlineData("LegacyAuthServiceTokenExchange", "https://country40-auth.invalid ")]
    [InlineData("LegacyAuthServiceTokenExchange", "http://country40-auth.invalid")]
    public void NamedOrigin_ExplicitUnsafeOriginFailsBeforeSend(string name, string origin)
    {
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary, originName: name, origin: origin);
        using var bootstrap = app.CreateClient();
        Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name));
        Assert.Equal(0, boundary.LoginCalls);
        Assert.Equal(0, boundary.IamCalls);
    }

    [Theory]
    [InlineData("IAMService")]
    [InlineData("LegacyAuthServiceTokenExchange")]
    public void NamedPrimary_ActualConfiguredHandlerDisablesRedirects(string name)
    {
        var boundary = new CountryNormalIamBoundary { InspectPrimary = true };
        using var app = fixture.App(boundary);
        using var bootstrap = app.CreateClient();
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        Assert.True(boundary.RedirectsDisabled);
        Assert.Equal(0, boundary.LoginCalls);
        Assert.Equal(0, boundary.IamCalls);
    }

    [Fact]
    public void NamedOrigin_AbsentIamOnlyRetainsKnownLogicalRoutingWithoutGrant()
    {
        using var app = fixture.App(new(), omitOriginName: "IAMService");
        using var bootstrap = app.CreateClient();
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("IAMService");
        Assert.Equal("https+http://iamservice/", client.BaseAddress?.AbsoluteUri);
    }

    [Fact]
    public void NamedOrigin_AbsentAuthDoesNotInventAnUnreviewedAuthority()
    {
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary, omitOriginName: LegacyServiceAccessTokenProvider.HttpClientName);
        using var bootstrap = app.CreateClient();
        Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(LegacyServiceAccessTokenProvider.HttpClientName));
        Assert.Equal(0, boundary.LoginCalls);
    }

    [Theory]
    [InlineData("https://country40-origin.invalid/.")]
    [InlineData("https://country40-origin.invalid/x/..")]
    [InlineData("https://country40-origin.invalid/%2e")]
    [InlineData("https://country40-origin.invalid\\")]
    [InlineData("https://country40-or\tigin.invalid")]
    [InlineData("HTTPS://country40-origin.invalid")]
    public void CanonicalOrigin_NormalizedOrAmbiguousInputRejectsBeforeTransport(string origin)
    {
        foreach (var name in new[] { "IAMService", "LegacyAuthServiceTokenExchange" })
        {
            var boundary = new CountryNormalIamBoundary();
            using var app = fixture.App(boundary, originName: name, origin: origin);
            using var bootstrap = app.CreateClient();
            Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name));
            Assert.Equal(0, boundary.IamCalls);
            Assert.Equal(0, boundary.LoginCalls);
        }
    }

    [Fact]
    public void CanonicalOrigin_OverlongInputRejectsBeforeTransport()
    {
        var origin = "https://" + new string('x', 2050) + ".invalid";
        foreach (var name in new[] { "IAMService", "LegacyAuthServiceTokenExchange" })
        {
            using var app = fixture.App(new(), originName: name, origin: origin);
            using var bootstrap = app.CreateClient();
            Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name));
        }
    }

    [Theory]
    [InlineData("short-login")]
    [InlineData("long-login")]
    public async Task DeclaredBodyLength_MismatchRejectsBeforeReturningBufferedContent(string mode)
    {
        var boundary = new CountryNormalIamBoundary { Mode = mode };
        using var app = fixture.App(boundary);
        using var bootstrap = app.CreateClient();
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(LegacyServiceAccessTokenProvider.HttpClientName);
        using var request = LoginRequest(boundary);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
        Assert.Equal(1, boundary.LoginCalls);
        Assert.Equal(mode == "short-login" ? 31 : 33, boundary.BodyBytesRead);
        Assert.True(boundary.BodyDisposed);
    }

    [Theory]
    [InlineData("GET", "https://country40-auth.invalid/auth/v1/service/login", false)]
    [InlineData("POST", "https://other.invalid/auth/v1/service/login", false)]
    [InlineData("POST", "https://country40-auth.invalid/other", false)]
    [InlineData("POST", "https://country40-auth.invalid/auth/v1/service/login", true)]
    public async Task WorkloadExchange_RequestGuardRejectsCredentialMisroutingBeforeTransport(string method, string uri, bool callerBearer)
    {
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary);
        using var bootstrap = app.CreateClient();
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(LegacyServiceAccessTokenProvider.HttpClientName);
        using var request = new HttpRequestMessage(new HttpMethod(method), uri) { Content = JsonContent.Create(new { clientId = "legacy-country", clientSecret = boundary.ExpectedSecret }) };
        if (callerBearer) request.Headers.Authorization = new("Bearer", "synthetic-caller-not-forwarded");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
        Assert.Equal(0, boundary.LoginCalls);
    }

    [Fact]
    public async Task WorkloadExchange_UnknownLengthOversizedBodyStopsAtActualByteBudget()
    {
        var boundary = new CountryNormalIamBoundary { Mode = "oversized-login" };
        using var app = fixture.App(boundary);
        using var bootstrap = app.CreateClient();
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(LegacyServiceAccessTokenProvider.HttpClientName);
        using var request = LoginRequest(boundary);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
        Assert.Equal(1, boundary.LoginCalls);
        Assert.Equal(32769, boundary.BodyBytesRead);
        Assert.True(boundary.BodyDisposed);
        Assert.Equal(0, boundary.IamCalls);
    }

    [Fact]
    public async Task WorkloadExchange_ExactDeclaredByteBudgetReturnsCompleteBoundedContent()
    {
        var boundary = new CountryNormalIamBoundary { Mode = "exact-login" };
        using var app = fixture.App(boundary);
        using var bootstrap = app.CreateClient();
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(LegacyServiceAccessTokenProvider.HttpClientName);
        using var request = LoginRequest(boundary);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(32768, (await response.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(32768, boundary.BodyBytesRead);
        Assert.True(boundary.BodyDisposed);
        Assert.Equal(1, boundary.LoginCalls);
    }

    [Fact]
    public async Task WorkloadExchange_StalledBodyHonorsTenSecondExistingClockBudgetWithNoneCallerToken()
    {
        var boundary = new CountryNormalIamBoundary { Mode = "stalled-login", Clock = new FakeTimeProvider() };
        using var app = fixture.App(boundary);
        using var bootstrap = app.CreateClient();
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(LegacyServiceAccessTokenProvider.HttpClientName);
        using var request = LoginRequest(boundary);
        var pending = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        await boundary.BodyEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        boundary.Clock.Advance(TimeSpan.FromSeconds(9));
        Assert.False(pending.IsCompleted);
        boundary.Clock.Advance(TimeSpan.FromSeconds(1));
        var exception = await Record.ExceptionAsync(async () => { using var response = await pending.WaitAsync(TimeSpan.FromSeconds(2)); });
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(boundary.BodyDisposed);
        Assert.Equal(1, boundary.LoginCalls);
        Assert.Equal(0, boundary.IamCalls);
    }

    [Theory]
    [InlineData("Testing", "http://remote.invalid")]
    [InlineData("Development", "http://0.0.0.0:12345")]
    public void NonProduction_RemoteOrWildcardHttpIsNotLoopbackAuthority(string environment, string origin)
    {
        foreach (var name in new[] { "IAMService", "LegacyAuthServiceTokenExchange" })
        {
            var boundary = new CountryNormalIamBoundary();
            using var app = fixture.App(boundary, originName: name, origin: origin, environment: environment);
            using var bootstrap = app.CreateClient();
            Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name));
            Assert.Equal(0, boundary.LoginCalls);
            Assert.Equal(0, boundary.IamCalls);
        }
    }

    [Theory]
    [InlineData("oversized-login")]
    [InlineData("stalled-login")]
    public async Task NormalDelete_SharedProviderBodyFailureCannotReachIamOrMutate(string mode)
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary { Mode = mode, Clock = new FakeTimeProvider() };
        using var app = fixture.App(boundary);
        using var client = fixture.Client(app);
        var pending = client.DeleteAsync($"/Countries/{row.Id}");
        if (mode == "stalled-login")
        {
            var first = await Task.WhenAny(boundary.BodyEntered.Task, pending).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(boundary.BodyEntered.Task, first);
            boundary.Clock.Advance(TimeSpan.FromSeconds(10));
        }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, boundary.LoginCalls);
        Assert.Equal(0, boundary.IamCalls);
        Assert.True(boundary.BodyDisposed);
        if (mode == "oversized-login") Assert.Equal(32769, boundary.BodyBytesRead);
        await fixture.AssertUnchangedAsync(row);
    }

    private static HttpRequestMessage LoginRequest(CountryNormalIamBoundary boundary) => new(HttpMethod.Post, "https://country40-auth.invalid/auth/v1/service/login")
    { Content = JsonContent.Create(new { clientId = "legacy-country", clientSecret = boundary.ExpectedSecret }) };

    [Fact]
    public async Task PreparedFixtureExchange_NormalClientsUseOwnServiceWithoutPermissionClaims()
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary, usePreparedExchange: true);
        using var client = fixture.Client(app);
        using var response = await client.DeleteAsync($"/Countries/{row.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, boundary.PreparedExchange!.Calls);
        Assert.Equal(1, boundary.IamCalls); // Real IAM transport also asserts own RS256 subject and absence of permission claims.
        await using var db = fixture.Context();
        Assert.False(await db.Countries.AnyAsync(value => value.Id == row.Id));
    }

    [Fact]
    public void NormalProgram_ResolvesRealIamWithoutFixtureRegistration()
    {
        using var app = fixture.App(new());
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        Assert.IsType<IamServiceClient>(scope.ServiceProvider.GetService<IIamServiceClient>());
    }

    [Fact]
    public async Task LiveAllow_NormalWorkloadExchangeDeletesPersistedLegacyIntegerRow()
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary);
        using var client = fixture.Client(app);
        using var response = await client.DeleteAsync($"/Countries/{row.Id}");
        await using var db = fixture.Context();
        var exists = await db.Countries.AnyAsync(value => value.Id == row.Id);
        Assert.Equal(1, boundary.IamCalls);
        Assert.Equal(1, boundary.LoginCalls);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(exists);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("unavailable")]
    [InlineData("network")]
    [InlineData("malformed")]
    public async Task ReachedIamFailure_StaleCallerGrantCannotDelete(string mode)
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary { Mode = mode };
        using var app = fixture.App(boundary);
        using var client = fixture.Client(app);
        using var response = await client.DeleteAsync($"/Countries/{row.Id}");
        await fixture.AssertUnchangedAsync(row);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, boundary.IamCalls); // An early missing-registration 403 is not a reached denial proof.
        Assert.Equal(1, boundary.LoginCalls);
    }

    [Fact]
    public async Task MissingLiveCredential_RealClientRejectsBeforeExternalSend()
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary, liveCredential: "");
        using var client = fixture.Client(app);
        using var response = await client.DeleteAsync($"/Countries/{row.Id}");
        await fixture.AssertUnchangedAsync(row);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, boundary.IamCalls);
        Assert.Equal(0, boundary.LoginCalls);
        using var scope = app.Services.CreateScope();
        Assert.IsType<IamServiceClient>(scope.ServiceProvider.GetService<IIamServiceClient>());
    }

    [Fact]
    public async Task MissingWorkloadCredential_RealClientFailsClosedWithoutIamMutation()
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary, workloadSecret: "");
        using var client = fixture.Client(app);
        using var response = await client.DeleteAsync($"/Countries/{row.Id}");
        await fixture.AssertUnchangedAsync(row);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, boundary.IamCalls);
        Assert.Equal(0, boundary.LoginCalls);
        using var scope = app.Services.CreateScope();
        Assert.IsType<IamServiceClient>(scope.ServiceProvider.GetService<IIamServiceClient>());
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    public async Task InvalidCaller_NormalJwtRejectsBeforeExternalSend(string profile)
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary);
        using var client = fixture.Client(app, profile);
        using var response = await client.DeleteAsync($"/Countries/{row.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, boundary.LoginCalls);
        Assert.Equal(0, boundary.IamCalls);
        await fixture.AssertUnchangedAsync(row);
    }

    [Fact]
    public async Task CallerAbort_AfterActualLiveIamEntryPreservesCountry()
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary { Mode = "wait" };
        using var app = fixture.App(boundary);
        using var client = fixture.Client(app);
        using var abort = new CancellationTokenSource();
        var pending = client.DeleteAsync($"/Countries/{row.Id}", abort.Token);
        var first = await Task.WhenAny(boundary.Entered.Task, pending).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(boundary.Entered.Task, first);
        abort.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var response = await pending; });
        Assert.Equal(1, boundary.IamCalls);
        await fixture.AssertUnchangedAsync(row);
    }

    [Fact]
    public async Task AllowedDelete_RealXminRaceReturnsConflictAndKeepsWinningRow()
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        boundary.Save.BeforeSave = async token =>
        {
            await using var winner = fixture.Context();
            var value = await winner.Countries.SingleAsync(value => value.Id == row.Id, token);
            value.Name = "Committed Country40 winner";
            await winner.SaveChangesAsync(token);
        };
        using var app = fixture.App(boundary);
        using var client = fixture.Client(app);
        using var response = await client.DeleteAsync($"/Countries/{row.Id}");
        await using var db = fixture.Context();
        var stored = await db.Countries.SingleAsync(value => value.Id == row.Id);
        Assert.Equal(1, boundary.IamCalls);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Committed Country40 winner", stored.Name);
        Assert.DoesNotContain("xmin", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AllowedDelete_MissingLegacyIntegerIdReachesNotFound()
    {
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary);
        using var client = fixture.Client(app);
        using var response = await client.DeleteAsync("/Countries/2147483647");
        Assert.Equal(1, boundary.IamCalls);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCollection_PreservesOriginalRouteWireWithoutIam()
    {
        var row = await fixture.SeedAsync();
        var boundary = new CountryNormalIamBoundary();
        using var app = fixture.App(boundary);
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/Countries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var stored = Assert.Single(document.RootElement.EnumerateArray(), value => value.GetProperty("id").GetInt32() == row.Id);
        Assert.Equal("Country40 original", stored.GetProperty("name").GetString());
        Assert.Equal("TH", stored.GetProperty("iso2").GetString());
        Assert.Equal(0, boundary.LoginCalls);
        Assert.Equal(0, boundary.IamCalls);
    }
}

/// <summary>Fresh local tmpfs PostgreSQL18/Redis; unique rows and a normal Program instance per case.</summary>
public sealed class CountryNormalIamFixture : IAsyncLifetime
{
    private const string Issuer = "https://country40-auth.invalid";
    private const string Audience = "country40-services";
    private readonly RSA key = RSA.Create(2048);
    private readonly string run = Guid.NewGuid().ToString("N");
    private readonly string credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly Dictionary<int, uint> seededVersions = [];
    private PostgreSqlContainer? postgres;
    private IContainer? redis;

    public CountryDbContext Context() => new(new DbContextOptionsBuilder<CountryDbContext>()
        .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres!.GetConnectionString()) { Pooling = false }.ConnectionString).Options);

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_CONTEXT")))
            throw new InvalidOperationException("Country40 requires no ambient Docker override.");
        using var observed = JsonDocument.Parse(await DockerReadAsync());
        var endpoint = observed.RootElement.GetString() ?? throw new InvalidOperationException("Missing Docker endpoint.");
        const string prefix = "npipe:////./pipe/";
        if (endpoint.StartsWith(prefix, StringComparison.Ordinal))
        {
            var pipe = endpoint[prefix.Length..];
            if (pipe.Length == 0 || pipe.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '_' and not '-' and not '.'))
                throw new InvalidOperationException("Invalid local Docker pipe.");
            endpoint = "npipe://./pipe/" + pipe;
        }
        else if (endpoint != "unix:///var/run/docker.sock") throw new InvalidOperationException("Remote Docker authority refused.");
        postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDockerEndpoint(endpoint)
            .WithName($"country40-pg-{run}").WithLabel("maliev.proof.owner", "country40").WithLabel("maliev.proof.run", run)
            .WithDatabase("country40").WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
            .WithCreateParameterModifier(parameters => Storage(parameters, "5432/tcp", "/var/lib/postgresql", 268435456)).Build();
        redis = new ContainerBuilder("redis:8-alpine").WithDockerEndpoint(endpoint)
            .WithName($"country40-redis-{run}").WithLabel("maliev.proof.owner", "country40").WithLabel("maliev.proof.run", run)
            .WithPortBinding(6379, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379))
            .WithCreateParameterModifier(parameters => Storage(parameters, "6379/tcp", "/data", 16777216)).Build();
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    private static void Storage(CreateContainerParameters parameters, string port, string path, int bytes)
    {
        parameters.HostConfig ??= new HostConfig();
        parameters.HostConfig.PortBindings ??= new Dictionary<string, IList<PortBinding>>();
        parameters.HostConfig.PortBindings[port] = [new PortBinding { HostIP = "127.0.0.1", HostPort = "" }];
        parameters.HostConfig.Tmpfs = new Dictionary<string, string> { [path] = $"rw,noexec,nosuid,size={bytes}" };
    }

    public async Task<Country> SeedAsync()
    {
        await using var db = Context();
        var row = new Country
        {
            Name = "Country40 original",
            Continent = "Asia",
            CountryCode = "+66",
            Iso2 = "TH",
            Iso3 = "THA",
            CreatedDate = new DateTime(2026, 10, 1, 2, 3, 4),
            ModifiedDate = new DateTime(2026, 10, 1, 3, 3, 4)
        };
        db.Countries.Add(row);
        await db.SaveChangesAsync();
        seededVersions[row.Id] = db.Entry(row).Property<uint>("xmin").CurrentValue;
        return row;
    }

    public async Task AssertUnchangedAsync(Country row)
    {
        await using var db = Context();
        var stored = await db.Countries.AsNoTracking().SingleAsync(value => value.Id == row.Id);
        Assert.Equal(row.Name, stored.Name);
        Assert.Equal(row.Continent, stored.Continent);
        Assert.Equal(row.CountryCode, stored.CountryCode);
        Assert.Equal(row.Iso2, stored.Iso2);
        Assert.Equal(row.Iso3, stored.Iso3);
        Assert.Equal(row.CreatedDate, stored.CreatedDate);
        Assert.Equal(row.ModifiedDate, stored.ModifiedDate);
        Assert.Equal(seededVersions[row.Id], await db.Countries.Where(value => value.Id == row.Id)
            .Select(value => EF.Property<uint>(value, "xmin")).SingleAsync());
    }

    public WebApplicationFactory<Program> App(CountryNormalIamBoundary boundary, string? liveCredential = null, string? workloadSecret = null,
        string? originName = null, string? origin = null, string environment = "Production", bool usePreparedExchange = false, string? omitOriginName = null)
    {
        boundary.ExpectedCredential = liveCredential ?? credential;
        boundary.ExpectedSecret = workloadSecret ?? secret;
        return new Factory(this, boundary, originName, origin, environment, usePreparedExchange, omitOriginName);
    }

    public HttpClient Client(WebApplicationFactory<Program> app, string profile = "employee")
    {
        var client = app.CreateClient();
        if (profile != "anonymous") client.DefaultRequestHeaders.Authorization = new("Bearer", Token("employee:country40", profile, true));
        return client;
    }

    private string Token(string subject, string profile = "employee", bool withPermission = false) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        profile == "wrong-issuer" ? "https://untrusted.invalid" : Issuer,
        profile == "wrong-audience" ? "wrong-country40-audience" : Audience,
        withPermission ? [new Claim("sub", subject), new Claim("permission", "legacy-country.countries.delete")]
            : [new Claim("sub", subject), new Claim("identity_kind", "service")],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)));

    public async Task DisposeAsync()
    {
        if (redis is not null) await redis.DisposeAsync();
        if (postgres is not null) await postgres.DisposeAsync();
        key.Dispose();
    }

    private static async Task<string> DockerReadAsync()
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "context", "inspect", "--format", "{{json .Endpoints.docker.Host}}" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker inspection unavailable.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout.Token);
        var error = ReadBoundedAsync(process.StandardError.BaseStream, timeout.Token);
        var exit = process.WaitForExitAsync(timeout.Token);
        try
        {
            var pending = new List<Task> { output, error, exit };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }
            if (process.ExitCode != 0) throw new InvalidOperationException("Docker inspection refused.");
            return Encoding.UTF8.GetString(await output).Trim();
        }
        catch
        {
            timeout.Cancel();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(exit, output, error).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { /* Preserve fixed inspection failure; never print Docker output. */ }
            throw new InvalidOperationException("Bounded local Docker inspection failed.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var result = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, 16385 - (int)result.Length)), token);
            if (read == 0) return result.ToArray();
            if (result.Length + read > 16384) throw new IOException("Docker inspection exceeded its output budget.");
            result.Write(buffer, 0, read);
        }
    }

    private sealed class Factory(CountryNormalIamFixture fixture, CountryNormalIamBoundary boundary,
        string? originName, string? origin, string environment, bool usePreparedExchange, string? omitOriginName) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            foreach (var setting in new Dictionary<string, string>
            {
                ["ConnectionStrings:CountryDbContext"] = new NpgsqlConnectionStringBuilder(fixture.postgres!.GetConnectionString()) { Pooling = false }.ConnectionString,
                ["ConnectionStrings:redis"] = $"127.0.0.1:{fixture.redis!.GetMappedPublicPort(6379)}",
                ["Cache:RedisEnabled"] = "true",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.key.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Services:Auth:BaseUrl"] = Issuer,
                ["Services:IAM:BaseUrl"] = "https://country40-iam.invalid",
                ["ServiceAuthentication:ClientId"] = "legacy-country",
                ["ServiceAuthentication:ClientSecret"] = boundary.ExpectedSecret,
                ["IAM:LivePermissionChecks:Credential"] = boundary.ExpectedCredential,
                ["Features:FailOpenOnIAMError"] = "false",
                ["Features:AllowExactServiceClaimsForLiveCheck"] = "false",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                ["Observability:RuntimeMetricsEnabled"] = "false"
            })
            {
                if (!(omitOriginName == "IAMService" && setting.Key == "Services:IAM:BaseUrl")
                    && !(omitOriginName == LegacyServiceAccessTokenProvider.HttpClientName && setting.Key == "Services:Auth:BaseUrl"))
                    builder.UseSetting(setting.Key, setting.Value);
            }
            if (originName is not null) builder.UseSetting(originName == "IAMService" ? "Services:IAM:BaseUrl" : "Services:Auth:BaseUrl", origin);
            if (usePreparedExchange) boundary.PreparedExchange = CountryTestWorkloadExchange.Prepare(builder);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                // External transports only. Never Add/Remove IIam, auth handlers, policy providers, principals or filters.
                services.Configure<HttpClientFactoryOptions>("IAMService", options => options.HttpMessageHandlerBuilderActions.Add(
                    http => InspectOrReplace(http, boundary, new IamTransport(boundary))));
                if (!usePreparedExchange) services.Configure<HttpClientFactoryOptions>(LegacyServiceAccessTokenProvider.HttpClientName,
                    options => options.HttpMessageHandlerBuilderActions.Add(http => InspectOrReplace(http, boundary, new LoginTransport(boundary, fixture.Token("service:legacy-country")))));
                services.AddDbContext<CountryDbContext>((_, options) => options.AddInterceptors(boundary.Save));
                if (boundary.Clock is not null) services.AddSingleton<TimeProvider>(boundary.Clock);
            });
        }

        private static void InspectOrReplace(HttpMessageHandlerBuilder http, CountryNormalIamBoundary boundary, HttpMessageHandler transport)
        {
            if (boundary.InspectPrimary)
            {
                transport.Dispose();
                boundary.RedirectsDisabled = http.PrimaryHandler switch
                {
                    SocketsHttpHandler sockets => !sockets.AllowAutoRedirect,
                    HttpClientHandler handler => !handler.AllowAutoRedirect,
                    _ => false
                };
            }
            else http.PrimaryHandler = transport;
        }
    }

    private sealed class IamTransport(CountryNormalIamBoundary boundary) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            boundary.IamCalls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://country40-iam.invalid/iam/v1/auth/check-permission", request.RequestUri!.AbsoluteUri);
            Assert.Equal(boundary.ExpectedCredential, Assert.Single(request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key")));
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var workload = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter);
            Assert.Equal("service:legacy-country", workload.Subject);
            Assert.Equal(SecurityAlgorithms.RsaSha256, workload.Header.Alg);
            Assert.DoesNotContain(workload.Claims, value => value.Type is "permission" or "permissions");
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var body = document.RootElement;
            Assert.Equal(4, body.EnumerateObject().Count());
            Assert.Equal("employee:country40", body.GetProperty("principalId").GetString());
            Assert.Equal("legacy-country.countries.delete", body.GetProperty("permissionId").GetString());
            Assert.Equal("global", body.GetProperty("resourcePath").GetString());
            Assert.True(body.GetProperty("bypassCache").GetBoolean());
            boundary.Entered.TrySetResult();
            if (boundary.Mode == "wait") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (boundary.Mode == "network") throw new HttpRequestException("Synthetic IAM transport failure.");
            return new(boundary.Mode == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = boundary.Mode == "malformed" ? new StringContent("not-json") : JsonContent.Create(new { allowed = boundary.Mode != "denied" })
            };
        }
    }

    private sealed class LoginTransport(CountryNormalIamBoundary boundary, string accessToken) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            boundary.LoginCalls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Issuer + "/auth/v1/service/login", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(2, document.RootElement.EnumerateObject().Count());
            Assert.Equal("legacy-country", document.RootElement.GetProperty("clientId").GetString());
            Assert.Equal(boundary.ExpectedSecret, document.RootElement.GetProperty("clientSecret").GetString());
            if (boundary.Mode is "oversized-login" or "stalled-login" or "short-login" or "long-login" or "exact-login")
            {
                var body = new StreamContent(new ObservedLoginBody(boundary));
                if (boundary.Mode is "short-login" or "long-login") body.Headers.ContentLength = 32;
                if (boundary.Mode == "exact-login") body.Headers.ContentLength = 32768;
                return new(HttpStatusCode.OK) { Content = body };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { accessToken, expiresIn = 300 }) };
        }
    }

    private sealed class ObservedLoginBody(CountryNormalIamBoundary boundary) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            boundary.BodyEntered.TrySetResult();
            if (boundary.Mode == "stalled-login") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (boundary.Mode is "short-login" or "long-login" or "exact-login")
            {
                var remaining = (boundary.Mode == "exact-login" ? 32768 : boundary.Mode == "short-login" ? 31 : 33) - boundary.BodyBytesRead;
                var read = Math.Min(buffer.Length, remaining);
                buffer.Span[..read].Fill((byte)'x');
                boundary.BodyBytesRead += read;
                return read;
            }
            buffer.Span.Fill((byte)'x');
            boundary.BodyBytesRead += buffer.Length;
            return buffer.Length;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { boundary.BodyDisposed = true; base.Dispose(disposing); }
    }
}

/// <summary>Per-case observed external boundary and real EF save hook, not an IAM authority substitute.</summary>
public sealed class CountryNormalIamBoundary
{
    public string Mode { get; init; } = "allow";
    public string ExpectedCredential { get; set; } = "";
    public string ExpectedSecret { get; set; } = "";
    public int IamCalls;
    public int LoginCalls;
    public bool InspectPrimary { get; init; }
    public bool RedirectsDisabled { get; set; }
    public FakeTimeProvider? Clock { get; init; }
    public int BodyBytesRead;
    public bool BodyDisposed;
    public TaskCompletionSource BodyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CountryWorkloadExchangeObservation? PreparedExchange { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CountryNormalSaveBoundary Save { get; } = new();
}

/// <summary>Injects one concurrent committed update after the real repository loaded a country.</summary>
public sealed class CountryNormalSaveBoundary : SaveChangesInterceptor
{
    public Func<CancellationToken, Task>? BeforeSave { get; set; }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (BeforeSave is { } action && eventData.Context!.ChangeTracker.Entries<Country>().Any(value => value.State == EntityState.Deleted))
            await action(cancellationToken);
        return result;
    }
}

/// <summary>External Auth transport preparation only; no IAM registration, grant, caller or policy replacement.</summary>
internal static class CountryTestWorkloadExchange
{
    private const string Origin = "https://country40-workload-fixture.invalid";

    internal static CountryWorkloadExchangeObservation Prepare(IWebHostBuilder builder)
    {
        var observation = new CountryWorkloadExchangeObservation();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        using var key = RSA.Create(2048);
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Origin, "country40-workload-fixture",
            [new Claim("sub", "service:legacy-country"), new Claim("identity_kind", "service")],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)));
        builder.UseSetting("Services:Auth:BaseUrl", Origin);
        builder.UseSetting("ServiceAuthentication:ClientId", "legacy-country");
        builder.UseSetting("ServiceAuthentication:ClientSecret", secret);
        builder.ConfigureTestServices(services => services.Configure<HttpClientFactoryOptions>(
            LegacyServiceAccessTokenProvider.HttpClientName, options => options.HttpMessageHandlerBuilderActions.Add(
                http => http.PrimaryHandler = new RecordingExchange(secret, token, observation))));
        return observation;
    }

    private sealed class RecordingExchange(string secret, string token, CountryWorkloadExchangeObservation observation) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref observation.Calls);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(Origin + "/auth/v1/service/login", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(2, json.RootElement.EnumerateObject().Count());
            Assert.Equal("legacy-country", json.RootElement.GetProperty("clientId").GetString());
            Assert.Equal(secret, json.RootElement.GetProperty("clientSecret").GetString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { accessToken = token, expiresIn = 300 }) };
        }
    }
}

internal sealed class CountryWorkloadExchangeObservation
{
    internal int Calls;
}
