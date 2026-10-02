using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Models;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Data;
using Legacy.Maliev.CountryService.Domain;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CountryService.Tests.Integration;

[Collection("Country lifecycle external configuration")]
public sealed class CountryMutationBoundaryAcceptanceTests(CountryMutationBoundaryFixture fixture)
    : IClassFixture<CountryMutationBoundaryFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("deny")]
    [InlineData("unavailable")]
    [InlineData("malformed")]
    public async Task Delete_StaleSignedGrantCannotOverrideCurrentIamRefusal(string mode)
    {
        var id = await fixture.SeedAsync();
        fixture.Iam.Mode = mode;
        using var client = fixture.Client(CountryPermissions.CountriesDelete);
        using var response = await client.DeleteAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Original", await fixture.NameAsync(id));
        fixture.AssertLiveDeleteRequest();
    }

    [Fact]
    public async Task Delete_CurrentIamAllowUsesExactLiveRequestAndRealPersistence()
    {
        var id = await fixture.SeedAsync();
        using var client = fixture.Client(CountryPermissions.CountriesDelete);
        using var response = await client.DeleteAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await fixture.NameAsync(id));
        fixture.AssertLiveDeleteRequest();
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    public async Task Delete_MissingIamOrAnonymous_CannotMutate(bool anonymous, HttpStatusCode expected)
    {
        var id = await fixture.SeedAsync();
        using var client = anonymous ? fixture.AnonymousClient() : fixture.NoIamClient();
        using var response = await client.DeleteAsync($"/Countries/{id}");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("Original", await fixture.NameAsync(id));
        Assert.Empty(fixture.Iam.Requests);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task XminRace_ActualRegisteredHttpReturnsConflictWithoutOverwritingWinner(string method)
    {
        var id = await fixture.SeedAsync();
        fixture.Save.BeforeSave = async token =>
        {
            await using var winner = fixture.Context();
            var row = await winner.Countries.SingleAsync(value => value.Id == id, token);
            row.Name = "Committed winner";
            await winner.SaveChangesAsync(token);
        };
        using var client = fixture.Client(method == "PUT" ? CountryPermissions.CountriesUpdate : CountryPermissions.CountriesDelete);
        using var response = await fixture.SendAsync(client, method, id);
        Assert.Equal("Committed winner", await fixture.NameAsync(id));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("xmin", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Committed winner", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    [InlineData("PUT", true)]
    [InlineData("DELETE", true)]
    public async Task ArbitrarySaveFailure_PreservesExistingErrorClassificationAndRow(string method, bool invalidOperation)
    {
        var id = await fixture.SeedAsync();
        fixture.Save.BeforeSave = _ => throw (invalidOperation
            ? new InvalidOperationException("private-country-fault")
            : new IOException("private-country-fault"));
        using var client = fixture.Client(method == "PUT" ? CountryPermissions.CountriesUpdate : CountryPermissions.CountriesDelete);
        using var response = await fixture.SendAsync(client, method, id);
        Assert.Equal(invalidOperation ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Original", await fixture.NameAsync(id));
        Assert.DoesNotContain("private-country-fault", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task MissingRow_IsNotConcurrencyConflict(string method)
    {
        using var client = fixture.Client(method == "PUT" ? CountryPermissions.CountriesUpdate : CountryPermissions.CountriesDelete);
        using var response = await fixture.SendAsync(client, method, 99999);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ConcurrencyExceptionWithoutCountryEntries_IsNotTranslatedTo409(string method)
    {
        var id = await fixture.SeedAsync();
        fixture.Save.BeforeSave = _ => throw new DbUpdateConcurrencyException("private-country-fault");
        using var client = fixture.Client(method == "PUT" ? CountryPermissions.CountriesUpdate : CountryPermissions.CountriesDelete);
        using var response = await fixture.SendAsync(client, method, id);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Original", await fixture.NameAsync(id));
        Assert.DoesNotContain("private-country-fault", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task CallerAbort_AtRealSaveBoundaryPropagatesAndLeavesRow(string method)
    {
        var id = await fixture.SeedAsync();
        var reached = new TaskCompletionSource(CreationOptions);
        var canceled = new TaskCompletionSource(CreationOptions);
        fixture.Save.BeforeSave = async token =>
        {
            reached.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                canceled.SetResult();
                throw;
            }
        };
        using var client = fixture.Client(method == "PUT" ? CountryPermissions.CountriesUpdate : CountryPermissions.CountriesDelete);
        using var abort = new CancellationTokenSource();
        var request = fixture.SendAsync(client, method, id, abort.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        abort.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Original", await fixture.NameAsync(id));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task CanceledXminConflict_RemainsCallerCancellationNotConflictResponse(string method)
    {
        var id = await fixture.SeedAsync();
        using var abort = new CancellationTokenSource();
        var reached = new TaskCompletionSource(CreationOptions);
        fixture.Save.BeforeSave = async token =>
        {
            await using var winner = fixture.Context();
            var row = await winner.Countries.SingleAsync(value => value.Id == id, token);
            row.Name = "Committed winner";
            await winner.SaveChangesAsync(token);
        };
        var observed = new TaskCompletionSource(CreationOptions);
        fixture.Save.OnConcurrency = async token =>
        {
            reached.SetResult();
            abort.Cancel();
            while (!token.IsCancellationRequested) await Task.Yield();
            observed.SetResult();
        };
        using var client = fixture.Client(method == "PUT" ? CountryPermissions.CountriesUpdate : CountryPermissions.CountriesDelete);
        var request = fixture.SendAsync(client, method, id, abort.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal("Committed winner", await fixture.NameAsync(id));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task RegisteredService_CanceledGenuineXminConflictPropagatesCancellation(string method)
    {
        var id = await fixture.SeedAsync();
        using var abort = new CancellationTokenSource();
        fixture.Save.BeforeSave = async token =>
        {
            await using var winner = fixture.Context();
            var row = await winner.Countries.SingleAsync(value => value.Id == id, token);
            row.Name = "Committed winner";
            await winner.SaveChangesAsync(token);
        };
        var observed = false;
        fixture.Save.OnConcurrency = token =>
        {
            abort.Cancel();
            observed = token.IsCancellationRequested;
            return Task.CompletedTask;
        };
        var exception = await Record.ExceptionAsync(() => fixture.MutateServiceAsync(method, id, abort.Token));
        Assert.True(observed);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal("Committed winner", await fixture.NameAsync(id));
    }

    [Fact]
    public async Task RealRedisPoison_DoesNotOverrideAuthoritativeCollectionsOrDetail()
    {
        var id = await fixture.SeedAsync();
        await fixture.PoisonCacheAsync();
        using var client = fixture.Client(CountryPermissions.CountriesRead);
        foreach (var route in new[] { "/Countries", "/country/v1/countries" })
            Assert.Equal("Original", Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>(route))!).Name);
        Assert.Equal("Original", (await client.GetFromJsonAsync<CountryResponse>($"/Countries/{id}"))!.Name);
        Assert.True(await fixture.CacheContainsPoisonAsync());
    }

    private const TaskCreationOptions CreationOptions = TaskCreationOptions.RunContinuationsAsynchronously;
}

public sealed class CountryMutationBoundaryFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.1-bookworm").Build();
    private readonly IContainer _redis = new ContainerBuilder("redis:8-alpine")
        .WithPortBinding(6379, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly string _credential = Guid.NewGuid().ToString("N");
    private WebApplicationFactory<Program> _factory = null!;
    private readonly List<WebApplicationFactory<Program>> _peers = [];
    public CountryIamTransport Iam { get; } = new();
    public CountrySaveBoundary Save { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await using var context = Context();
        await context.Database.MigrateAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            CountryTestWorkloadExchange.Prepare(builder);
            foreach (var pair in new Dictionary<string, string>
            {
                ["ConnectionStrings:CountryDbContext"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:redis"] = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}",
                ["Cache:RedisEnabled"] = "true",
                ["Jwt:Issuer"] = "country-mutation-tests",
                ["Jwt:Audience"] = "country-mutation-tests",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_rsa.ExportSubjectPublicKeyInfoPem())),
                ["IAM:LivePermissionChecks:Credential"] = _credential,
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            }) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IIamServiceClient, IamServiceClient>();
                services.AddHttpClient("IAMService", client => client.BaseAddress = new Uri("https://controlled-iam.invalid"))
                    .ConfigurePrimaryHttpMessageHandler(() => Iam);
                services.AddDbContext<CountryDbContext>((_, options) => options.AddInterceptors(Save));
            });
        });
    }

    public CountryDbContext Context() => new(new DbContextOptionsBuilder<CountryDbContext>()
        .UseNpgsql(_postgres.GetConnectionString()).Options);

    public async Task ResetAsync()
    {
        Save.BeforeSave = null;
        Save.OnConcurrency = null;
        Iam.Reset();
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Country\" RESTART IDENTITY");
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedCache>().RemoveAsync("legacy:country:all:v1");
    }

    public async Task<int> SeedAsync()
    {
        await using var context = Context();
        var row = new Country { Name = "Original" };
        context.Countries.Add(row);
        await context.SaveChangesAsync();
        return row.Id;
    }

    public async Task<string?> NameAsync(int id)
    {
        await using var context = Context();
        return await context.Countries.Where(value => value.Id == id).Select(value => value.Name).SingleOrDefaultAsync();
    }

    public HttpClient Client(string permission)
    {
        var client = _factory.CreateClient();
        Sign(client, permission);
        return client;
    }

    public HttpClient AnonymousClient() => _factory.CreateClient();

    public HttpClient NoIamClient()
    {
        var peer = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.RemoveAll<IIamServiceClient>()));
        _peers.Add(peer);
        var client = peer.CreateClient();
        Sign(client, CountryPermissions.CountriesDelete);
        return client;
    }

    public async Task MutateServiceAsync(string method, int id, CancellationToken token)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICountryService>();
        if (method == "PUT") await service.UpdateAsync(id, new UpsertCountryRequest("Attempted loser", null, null, null, null), token);
        else await service.DeleteAsync(id, token);
    }

    private void Sign(HttpClient client, string permission)
    {
        var token = new JwtSecurityToken("country-mutation-tests", "country-mutation-tests",
            [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()), new Claim("permission", permission)],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    public Task<HttpResponseMessage> SendAsync(HttpClient client, string method, int id, CancellationToken token = default) =>
        method == "PUT" ? client.PutAsJsonAsync($"/Countries/{id}", new UpsertCountryRequest("Attempted loser", null, null, null, null), token)
            : client.DeleteAsync($"/Countries/{id}", token);

    public void AssertLiveDeleteRequest()
    {
        var request = Assert.Single(Iam.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/iam/v1/auth/check-permission", request.Path);
        Assert.Equal("legacy-country.countries.delete", request.Permission);
        Assert.Equal("global", request.Resource);
        Assert.True(request.BypassCache);
        Assert.Equal(_credential, request.Credential);
        Assert.True(Guid.TryParse(request.Principal, out _));
    }

    public async Task PoisonCacheAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDistributedCache>().SetStringAsync("legacy:country:all:v1", "poisoned-country-projection");
    }

    public async Task<bool> CacheContainsPoisonAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDistributedCache>().GetStringAsync("legacy:country:all:v1") == "poisoned-country-projection";
    }

    public async Task DisposeAsync()
    {
        foreach (var peer in _peers) await peer.DisposeAsync();
        if (_factory is not null) await _factory.DisposeAsync();
        _rsa.Dispose();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed class CountrySaveBoundary : SaveChangesInterceptor
{
    public Func<CancellationToken, Task>? BeforeSave { get; set; }
    public Func<CancellationToken, Task>? OnConcurrency { get; set; }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (BeforeSave is { } action && eventData.Context!.ChangeTracker.Entries<Country>()
            .Any(value => value.State is EntityState.Modified or EntityState.Deleted))
            await action(cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (OnConcurrency is { } action) await action(cancellationToken);
        return result;
    }
}

public sealed class CountryIamTransport : HttpMessageHandler
{
    public string Mode { get; set; } = "allow";
    public List<CountryIamRequest> Requests { get; } = [];
    public void Reset() { Mode = "allow"; Requests.Clear(); }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root = body.RootElement;
        Requests.Add(new(request.Method, request.RequestUri!.AbsolutePath, root.GetProperty("principalId").GetString()!,
            root.GetProperty("permissionId").GetString()!, root.GetProperty("resourcePath").GetString(),
            root.GetProperty("bypassCache").GetBoolean(), request.Headers.TryGetValues("X-Maliev-IAM-Live-Check-Key", out var values) ? Assert.Single(values) : null));
        return new HttpResponseMessage(Mode == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            Content = Mode == "malformed" ? new StringContent("not-json") : JsonContent.Create(new { allowed = Mode == "allow" }),
        };
    }
}

public sealed record CountryIamRequest(HttpMethod Method, string Path, string Principal, string Permission,
    string? Resource, bool BypassCache, string? Credential);

public sealed class CountryLifecycleLiveDeleteTransport(string credential) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root = body.RootElement;
        var allowed = request.Method == HttpMethod.Post
            && request.RequestUri!.AbsolutePath == "/iam/v1/auth/check-permission"
            && root.GetProperty("principalId").GetString() == "employee:country-acceptance"
            && root.GetProperty("permissionId").GetString() == "legacy-country.countries.delete"
            && root.GetProperty("resourcePath").GetString() == "global"
            && root.GetProperty("bypassCache").GetBoolean()
            && request.Headers.TryGetValues("X-Maliev-IAM-Live-Check-Key", out var values)
            && values.Single() == credential;
        Assert.True(allowed, "Opt-in lifecycle IAM transport received a request outside its exact live-delete contract.");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { allowed }) };
    }
}
