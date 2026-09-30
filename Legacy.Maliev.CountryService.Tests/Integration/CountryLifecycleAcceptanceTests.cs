using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Models;
using Legacy.Maliev.CountryService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CountryService.Tests.Integration;

[Collection("Country lifecycle external configuration")]
public sealed class CountryLifecycleAcceptanceTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AuthenticatedCreate_ActualProductionPipeline_PersistsLegacyCountry()
    {
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate);
        using var response = await client.PostAsJsonAsync("/Countries",
            new UpsertCountryRequest("Thailand", "Asia", "764", "TH", "THA"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CountryResponse>();
        Assert.NotNull(created);
        await using var context = fixture.CreateContext();
        var stored = await context.Countries.AsNoTracking().SingleAsync(value => value.Id == created.Id);
        Assert.Equal("Thailand", stored.Name);
        Assert.Equal("TH", stored.Iso2);
        Assert.NotNull(stored.CreatedDate);
        Assert.NotNull(stored.ModifiedDate);
        Assert.Equal(DateTimeKind.Unspecified, stored.CreatedDate.Value.Kind);
        Assert.Equal(created.CreatedDate!.Value.Ticks / 10 * 10, stored.CreatedDate.Value.Ticks);
    }

    [Fact]
    public async Task CollectionReads_EmptyAndPopulated_PreserveDistinctRoutesAndWire()
    {
        using var anonymous = fixture.CreateAnonymousClient();
        using var legacyEmpty = await anonymous.GetAsync("/Countries");
        using var versionedEmpty = await anonymous.GetAsync("/country/v1/countries");
        Assert.Equal(HttpStatusCode.NotFound, legacyEmpty.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versionedEmpty.StatusCode);
        Assert.Empty((await versionedEmpty.Content.ReadFromJsonAsync<CountryResponse[]>())!);

        await fixture.SeedAsync("Thailand", "TH", "THA");
        await fixture.SeedAsync("Japan", "JP", "JPN");
        await fixture.InvalidateCacheAsync();
        foreach (var route in new[] { "/Countries", "/country/v1/countries" })
        {
            using var response = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rows = json.RootElement.EnumerateArray().ToArray();
            Assert.Equal(new[] { "Japan", "Thailand" }, rows.Select(row => row.GetProperty("name").GetString()));
            Assert.Equal("TH", rows[1].GetProperty("iso2").GetString());
            Assert.False(rows[1].TryGetProperty("Name", out _));
            Assert.False(rows[1].TryGetProperty("timezones", out _));
        }
        using var unsupported = await anonymous.PostAsJsonAsync("/country/v1/countries",
            new UpsertCountryRequest("Unsupported", null, null, null, null));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, unsupported.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedLifecycle_UpdateDeleteAndMissingRows_InvalidateActualCollectionCache()
    {
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate, CountryPermissions.CountriesRead,
            CountryPermissions.CountriesUpdate, CountryPermissions.CountriesDelete);
        using var createdResponse = await client.PostAsJsonAsync("/Countries", new
        {
            name = new string('n', 50),
            continent = new string('c', 50),
            countryCode = new string('1', 30),
            iso2 = "th",
            iso3 = "tha",
            id = 99999,
            createdDate = "1900-01-01",
            modifiedDate = "1900-01-01",
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<CountryResponse>())!;
        Assert.NotEqual(99999, created.Id);
        Assert.Equal(DateTimeKind.Utc, created.CreatedDate!.Value.Kind);
        Assert.Equal(created.CreatedDate, created.ModifiedDate);
        Assert.Equal($"/Countries/{created.Id}", createdResponse.Headers.Location!.AbsolutePath);
        Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!);

        using var updated = await client.PutAsJsonAsync($"/Countries/{created.Id}", new UpsertCountryRequest("Japan", null, null, "JP", "JPN"));
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var collection = await client.GetFromJsonAsync<CountryResponse[]>("/country/v1/countries");
        Assert.Equal("Japan", Assert.Single(collection!).Name);
        await using (var context = fixture.CreateContext())
        {
            var stored = await context.Countries.SingleAsync();
            Assert.Equal(created.CreatedDate.Value.Ticks / 10 * 10, stored.CreatedDate!.Value.Ticks);
            Assert.True(stored.ModifiedDate >= stored.CreatedDate);
            Assert.Equal(DateTimeKind.Unspecified, stored.ModifiedDate!.Value.Kind);
            Assert.Null(stored.Continent);
            Assert.Null(stored.CountryCode);
        }
        using var missingUpdate = await client.PutAsJsonAsync("/Countries/99999", new UpsertCountryRequest("Missing", null, null, null, null));
        using var invalidId = await client.PutAsJsonAsync("/Countries/0", new UpsertCountryRequest("Invalid", null, null, null, null));
        Assert.Equal(HttpStatusCode.NotFound, missingUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);
        using var deleted = await client.DeleteAsync($"/Countries/{created.Id}");
        using var missingDelete = await client.DeleteAsync($"/Countries/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingDelete.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CountryResponse[]>("/country/v1/countries"))!);
        await using var verify = fixture.CreateContext();
        Assert.Empty(await verify.Countries.ToArrayAsync());
    }

    [Fact]
    public async Task LegacyTimestampMapping_PreservesRawImportedTicksAndNullsWithoutTimezoneConversion()
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Country" ("Name", "CreatedDate", "ModifiedDate")
            VALUES ('Imported', TIMESTAMP '1999-12-31 23:59:59.123456', NULL), ('Null dates', NULL, NULL)
            """);
        var rows = await context.Countries.OrderBy(row => row.Id).ToArrayAsync();
        var expected = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Unspecified).AddTicks(1234560);
        Assert.Equal(expected, rows[0].CreatedDate);
        Assert.Equal(DateTimeKind.Unspecified, rows[0].CreatedDate!.Value.Kind);
        Assert.Null(rows[0].ModifiedDate);
        Assert.Null(rows[1].CreatedDate);
        Assert.Null(rows[1].ModifiedDate);
        rows[0].ModifiedDate = DateTime.SpecifyKind(expected, DateTimeKind.Utc);
        await context.SaveChangesAsync();
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"ModifiedDate\" FROM \"Country\" WHERE \"Name\" = 'Imported'";
        var raw = Assert.IsType<DateTime>(await command.ExecuteScalarAsync());
        Assert.Equal(expected.Ticks, raw.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, raw.Kind);
        rows[0].ModifiedDate = null;
        await context.SaveChangesAsync();
        Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RepositoryXmin_ConcurrentUpdateAndDelete_RejectStaleTrackedWrites()
    {
        var id = await fixture.SeedAsync("Original");
        await using var winnerContext = fixture.CreateContext();
        await using var staleContext = fixture.CreateContext();
        var winnerRepository = new CountryRepository(winnerContext);
        var staleRepository = new CountryRepository(staleContext);
        var winner = (await winnerRepository.GetByIdForUpdateAsync(id, CancellationToken.None))!;
        var stale = (await staleRepository.GetByIdForUpdateAsync(id, CancellationToken.None))!;
        winner.Name = "Winner";
        await winnerRepository.UpdateAsync(winner, CancellationToken.None);
        stale.Name = "Stale";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleRepository.UpdateAsync(stale, CancellationToken.None));
        await using var verify = fixture.CreateContext();
        Assert.Equal("Winner", (await verify.Countries.AsNoTracking().SingleAsync()).Name);
        staleContext.ChangeTracker.Clear();
        var staleDelete = (await staleRepository.GetByIdForUpdateAsync(id, CancellationToken.None))!;
        await winnerRepository.DeleteAsync(winner, CancellationToken.None);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleRepository.DeleteAsync(staleDelete, CancellationToken.None));
        Assert.Empty(await verify.Countries.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task CacheFaults_ActualServiceFallsBackAndRecoversAfterCommittedMutation()
    {
        await fixture.SeedAsync("Thailand");
        using var client = fixture.CreateClient(CountryPermissions.CountriesUpdate);
        fixture.SetCacheFaults(reads: true, writes: true);
        Assert.Equal("Thailand", Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!).Name);
        fixture.SetCacheFaults();
        Assert.Equal("Thailand", Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!).Name);
        fixture.SetCacheFaults(reads: true, removal: true);
        using var update = await client.PutAsJsonAsync("/Countries/1", new UpsertCountryRequest("Japan", null, null, null, null));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal("Japan", Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!).Name);
        fixture.SetCacheFaults();
        await fixture.InvalidateCacheAsync();
        Assert.Equal("Japan", Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!).Name);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ProtectedMutation_RequiresItsExactGrantAndLeavesExistingRowUnchanged(string method)
    {
        var id = await fixture.SeedAsync("Thailand");
        using var anonymous = fixture.CreateAnonymousClient();
        using var wrong = fixture.CreateClient(CountryPermissions.CountriesCreate);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (wrong, HttpStatusCode.Forbidden) })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/Countries/{id}");
            if (method == "PUT") request.Content = JsonContent.Create(new UpsertCountryRequest("Denied", null, null, null, null));
            using var response = await client.SendAsync(request);
            Assert.Equal(expected, response.StatusCode);
        }
        await using var context = fixture.CreateContext();
        Assert.Equal("Thailand", Assert.Single(await context.Countries.ToArrayAsync()).Name);
    }

    [Fact]
    public async Task Update_InvalidName_DoesNotMutateOrInvalidatePrimedCache()
    {
        var id = await fixture.SeedAsync("Thailand");
        using var client = fixture.CreateClient(CountryPermissions.CountriesUpdate);
        Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!);
        using var response = await client.PutAsJsonAsync($"/Countries/{id}", new UpsertCountryRequest(" ", null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = fixture.CreateContext();
        Assert.Equal("Thailand", Assert.Single(await context.Countries.ToArrayAsync()).Name);
        Assert.Equal("Thailand", Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!).Name);
    }

    [Fact]
    public async Task ApiDocumentation_ProductionHiddenAndNonProductionGeneratedFromRealProgram()
    {
        using var production = fixture.CreateAnonymousClient();
        using var hidden = await production.GetAsync("/countries/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        await using var factory = fixture.CreateDocumentationFactory();
        using var documentation = factory.CreateClient();
        using var response = await documentation.GetAsync("/countries/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Legacy MALIEV Country Service API", json.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/country/v1/countries", out _));
    }

    [Fact]
    public async Task DesignTimeContext_RequiresExternalConfigurationAndUsesActualPostgres()
    {
        const string key = "ConnectionStrings__CountryDbContext";
        var original = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, null);
            Assert.Throws<InvalidOperationException>(() => new CountryDbContextFactory().CreateDbContext([]));
            Environment.SetEnvironmentVariable(key, " ");
            Assert.Throws<InvalidOperationException>(() => new CountryDbContextFactory().CreateDbContext([]));
            await using var configured = fixture.CreateContext();
            Environment.SetEnvironmentVariable(key, configured.Database.GetConnectionString());
            await using var designTime = new CountryDbContextFactory().CreateDbContext([]);
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", designTime.Database.ProviderName);
            Assert.True(await designTime.Database.CanConnectAsync());
            Assert.Empty(await designTime.Countries.ToArrayAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [Fact]
    public async Task ActualService_CancellationIsNotSwallowedAsCacheRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.ReadWithCancellationAsync(cancellation.Token));
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("algorithm")]
    public async Task ProtectedWrite_InvalidIdentity_Returns401WithoutMutation(string mode)
    {
        using var client = fixture.CreateAnonymousClient();
        if (mode != "anonymous") client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token(mode, CountryPermissions.CountriesCreate));
        using var response = await client.PostAsJsonAsync("/Countries", new UpsertCountryRequest("Denied", null, null, null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Countries.ToArrayAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("country.countries.create")]
    [InlineData("legacy-country.countries.read")]
    public async Task ProtectedWrite_MissingOrWrongPermission_Returns403WithoutMutation(string permission)
    {
        using var client = fixture.CreateClient(permission);
        using var response = await client.PostAsJsonAsync("/Countries", new UpsertCountryRequest("Denied", null, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var context = fixture.CreateContext();
        Assert.Empty(await context.Countries.ToArrayAsync());
    }

    [Fact]
    public async Task ProtectedDetail_ExactReadGrantAndMissingRow_PreserveReadBoundary()
    {
        var id = await fixture.SeedAsync("Thailand", "TH", "THA");
        using var anonymous = fixture.CreateAnonymousClient();
        using var wrong = fixture.CreateClient(CountryPermissions.CountriesCreate);
        using var allowed = fixture.CreateClient(CountryPermissions.CountriesRead);
        using var deniedIdentity = await anonymous.GetAsync($"/Countries/{id}");
        using var deniedGrant = await wrong.GetAsync($"/Countries/{id}");
        using var response = await allowed.GetAsync($"/Countries/{id}");
        using var missing = await allowed.GetAsync("/Countries/99999");
        Assert.Equal(HttpStatusCode.Unauthorized, deniedIdentity.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedGrant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Thailand", (await response.Content.ReadFromJsonAsync<CountryResponse>())!.Name);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("null-name")]
    [InlineData("empty-name")]
    [InlineData("blank-name")]
    [InlineData("name-length")]
    [InlineData("continent-length")]
    [InlineData("code-length")]
    [InlineData("iso2-length")]
    [InlineData("iso3-length")]
    public async Task Create_InvalidExistingFieldRules_Returns400LeavesDatabaseUnchangedAndReadsCurrentState(string mode)
    {
        var id = await fixture.SeedAsync("Thailand", "TH", "THA");
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate);
        using var prime = await client.GetAsync("/Countries");
        Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
        await using (var context = fixture.CreateContext())
        {
            var country = await context.Countries.SingleAsync(row => row.Id == id);
            country.Name = "Stored-only change";
            await context.SaveChangesAsync();
        }
        var payload = new Dictionary<string, object?> { ["name"] = "Valid", ["continent"] = null, ["countryCode"] = null, ["iso2"] = null, ["iso3"] = null };
        var (field, value) = mode switch
        {
            "null-name" => ("name", (string?)null),
            "empty-name" => ("name", ""),
            "blank-name" => ("name", " "),
            "name-length" => ("name", new string('n', 51)),
            "continent-length" => ("continent", new string('c', 51)),
            "code-length" => ("countryCode", new string('c', 31)),
            "iso2-length" => ("iso2", "AAA"),
            _ => ("iso3", "AAAA"),
        };
        payload[field] = value;
        using var response = await client.PostAsJsonAsync("/Countries", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(errors.RootElement.TryGetProperty("errors", out _));
        await using var verify = fixture.CreateContext();
        Assert.Equal("Stored-only change", Assert.Single(await verify.Countries.ToArrayAsync()).Name);
        var authoritative = await client.GetFromJsonAsync<CountryResponse[]>("/Countries");
        Assert.Equal("Stored-only change", Assert.Single(authoritative!).Name);
    }
}

public sealed class CountryLifecycleFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.1-bookworm").Build();
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);
    private readonly ControlledCache _cache = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            foreach (var setting in new Dictionary<string, string>
            {
                ["ConnectionStrings:CountryDbContext"] = _postgres.GetConnectionString(),
                ["Cache:RedisEnabled"] = "false",
                ["CORS:AllowedOrigins:0"] = "https://example.test",
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_rsa.ExportSubjectPublicKeyInfoPem())),
                ["Jwt:Issuer"] = "country-lifecycle-tests",
                ["Jwt:Audience"] = "country-lifecycle-tests",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            }) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDistributedCache>();
                services.AddSingleton<IDistributedCache>(_cache);
            });
        });
    }

    public CountryDbContext CreateContext() => new(new DbContextOptionsBuilder<CountryDbContext>()
        .UseNpgsql(_postgres.GetConnectionString()).Options);

    public async Task ResetAsync()
    {
        _cache.Reset();
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Country\" RESTART IDENTITY");
    }

    public async Task<int> SeedAsync(string name, string? iso2 = null, string? iso3 = null)
    {
        await using var context = CreateContext();
        var country = new Legacy.Maliev.CountryService.Domain.Country { Name = name, Iso2 = iso2, Iso3 = iso3 };
        context.Countries.Add(country);
        await context.SaveChangesAsync();
        return country.Id;
    }

    public async Task InvalidateCacheAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICountryCache>().InvalidateAsync(CancellationToken.None);
    }

    public async Task ReadWithCancellationAsync(CancellationToken token)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICountryService>().GetAllAsync(token);
    }

    public void SetCacheFaults(bool reads = false, bool writes = false, bool removal = false)
    {
        _cache.FailReads = reads;
        _cache.FailWrites = writes;
        _cache.FailRemoval = removal;
    }

    public HttpClient CreateClient(params string[] permissions)
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("valid", permissions));
        return client;
    }

    public HttpClient CreateAnonymousClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public WebApplicationFactory<Program> CreateDocumentationFactory() => _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    public string Token(string mode, params string[] permissions)
    {
        if (mode == "malformed") return "not-a-token";
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(mode == "issuer" ? "other-issuer" : "country-lifecycle-tests", mode == "audience" ? "other-audience" : "country-lifecycle-tests",
            new[] { new Claim(JwtRegisteredClaimNames.Sub, "employee:country-acceptance") }
                .Concat(permissions.Select(permission => new Claim("permission", permission))),
            now.AddMinutes(-20), mode == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(mode == "signature" ? _otherRsa : _rsa), mode == "algorithm" ? SecurityAlgorithms.RsaSha384 : SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        _rsa.Dispose();
        _otherRsa.Dispose();
        await _postgres.DisposeAsync();
    }

    private sealed class ControlledCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public bool FailRemoval { get; set; }
        public void Reset()
        {
            FailReads = FailWrites = FailRemoval = false;
            _inner.Remove("legacy:country:all:v1");
        }
        public byte[]? Get(string key) => FailReads ? throw new IOException("controlled-cache-private-marker") : _inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { token.ThrowIfCancellationRequested(); return Task.FromResult(Get(key)); }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { if (FailWrites) throw new IOException("controlled-cache-private-marker"); _inner.Set(key, value, options); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { token.ThrowIfCancellationRequested(); Set(key, value, options); return Task.CompletedTask; }
        public void Remove(string key) { if (FailRemoval) throw new IOException("controlled-cache-private-marker"); _inner.Remove(key); }
        public Task RemoveAsync(string key, CancellationToken token = default) { token.ThrowIfCancellationRequested(); Remove(key); return Task.CompletedTask; }
        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
    }
}

[CollectionDefinition("Country lifecycle external configuration", DisableParallelization = true)]
public sealed class CountryLifecycleExternalConfigurationCollection;
