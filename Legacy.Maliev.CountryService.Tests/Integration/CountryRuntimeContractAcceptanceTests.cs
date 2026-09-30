using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CountryService.Tests.Integration;

[Collection("Country lifecycle external configuration")]
public sealed class CountryRuntimeContractAcceptanceTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NonProductionOpenApi_ConsumedLegacyOperations_RetainXmlCommentSummaries()
    {
        await using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/countries/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var (path, method) in new[] { ("/Countries", "get"), ("/Countries", "post"), ("/Countries/{id}", "put") })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            Assert.True(operation.TryGetProperty("summary", out var summary) && !string.IsNullOrWhiteSpace(summary.GetString()),
                "Consumed country documentation must retain human-readable operation semantics from its published XML comments.");
        }
    }

    [Fact]
    public async Task NonProductionOpenApi_ProtectedCountryCalls_ExposeUsableBearerAuthorizationContract()
    {
        await using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/countries/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var components = document.RootElement.GetProperty("components");
        Assert.True(components.TryGetProperty("securitySchemes", out var schemes),
            "The consumed document must describe the bearer credential required by the actual protected Country routes.");
        Assert.Contains(schemes.EnumerateObject(), property =>
            property.Value.TryGetProperty("type", out var type) &&
            ((type.GetString() == "http" && property.Value.TryGetProperty("scheme", out var scheme) && scheme.GetString() == "bearer") ||
             (type.GetString() == "apiKey" && property.Value.TryGetProperty("name", out var name) && name.GetString() == "Authorization")));
        var paths = document.RootElement.GetProperty("paths");
        foreach (var (path, method) in new[] { ("/Countries", "post"), ("/Countries/{id}", "get"), ("/Countries/{id}", "put"), ("/Countries/{id}", "delete") })
            Assert.NotEmpty(paths.GetProperty(path).GetProperty(method).GetProperty("security").EnumerateArray());
        foreach (var path in new[] { "/Countries", "/country/v1/countries" })
        {
            var anonymous = paths.GetProperty(path).GetProperty("get");
            Assert.True(!anonymous.TryGetProperty("security", out var security) || security.GetArrayLength() == 0);
        }
    }

    [Fact]
    public async Task NonProductionOpenApi_CreateSchema_DescribesOnlySupportedLegacyInputAndBounds()
    {
        await using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/countries/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schema = document.RootElement.GetProperty("paths").GetProperty("/Countries").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
            schema = document.RootElement.GetProperty("components").GetProperty("schemas")
                .GetProperty(reference.GetString()!.Split('/')[^1]);
        var properties = schema.GetProperty("properties");
        Assert.Equal(new[] { "continent", "countryCode", "iso2", "iso3", "name" },
            properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.True(properties.GetProperty("name").TryGetProperty("maxLength", out var nameLength),
            "Consumed input documentation must expose the enforced legacy name bound, not an unconstrained payload: " + schema.GetRawText());
        Assert.Equal(50, nameLength.GetInt32());
        Assert.Equal(50, properties.GetProperty("continent").GetProperty("maxLength").GetInt32());
        Assert.Equal(30, properties.GetProperty("countryCode").GetProperty("maxLength").GetInt32());
        Assert.Equal(2, properties.GetProperty("iso2").GetProperty("maxLength").GetInt32());
        Assert.Equal(3, properties.GetProperty("iso3").GetProperty("maxLength").GetInt32());
        Assert.Contains(schema.GetProperty("required").EnumerateArray(), item => item.GetString() == "name");
    }

    [Fact]
    public async Task CommittedCreate_CacheInvalidationFailureThenHealthyRead_DoesNotHidePersistedCountry()
    {
        await fixture.SeedAsync("Thailand", "TH", "THA");
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate);
        Assert.Equal("Thailand", Assert.Single((await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!).Name);
        fixture.SetCacheFaults(removal: true);
        using var response = await client.PostAsJsonAsync("/Countries", new UpsertCountryRequest("Japan", "Asia", "392", "JP", "JPN"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CountryResponse>();
        Assert.NotNull(created);
        await using var context = fixture.CreateContext();
        Assert.Equal("Japan", (await context.Countries.AsNoTracking().SingleAsync(row => row.Id == created.Id)).Name);
        fixture.SetCacheFaults();
        var healthyRead = await client.GetFromJsonAsync<CountryResponse[]>("/Countries");
        Assert.NotNull(healthyRead);
        Assert.Equal(new[] { "Japan", "Thailand" }, healthyRead.Select(row => row.Name));
        Assert.Contains(healthyRead, row => row.Id == created.Id);
    }

    [Fact]
    public async Task NullableInputOmission_ActualHttpAcceptsIt_ConsumedSchemaDoesNotRequireThoseFields()
    {
        using var authorized = fixture.CreateClient(CountryPermissions.CountriesCreate);
        using var createdResponse = await authorized.PostAsJsonAsync("/Countries", new { name = "Thailand" });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<CountryResponse>();
        Assert.NotNull(created);
        await using var context = fixture.CreateContext();
        var stored = await context.Countries.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        Assert.Null(stored.Continent);
        Assert.Null(stored.CountryCode);
        Assert.Null(stored.Iso2);
        Assert.Null(stored.Iso3);
        await using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/countries/openapi/v1.json"));
        var schema = document.RootElement.GetProperty("paths").GetProperty("/Countries").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
            schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
        Assert.Equal(new[] { "name" }, schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task InflightCacheFill_CommittedCreateInvalidatesBeforeFillCompletes_SubsequentReadSeesCommittedRow()
    {
        await fixture.SeedAsync("Thailand", "TH", "THA");
        using var cache = new PendingFillCache();
        await using var factory = fixture.CreateDocumentationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(cache);
        }));
        await using var peerFactory = fixture.CreateDocumentationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(cache);
        }));
        using var client = factory.CreateClient();
        using var peer = peerFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.Token("valid", CountryPermissions.CountriesCreate));
        var oldSnapshot = (await client.GetFromJsonAsync<CountryResponse[]>("/Countries"))!;
        var inflight = cache.SetAsync("legacy:country:all:v1", JsonSerializer.SerializeToUtf8Bytes(oldSnapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new DistributedCacheEntryOptions());
        try
        {
            await cache.FillEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var response = await client.PostAsJsonAsync("/Countries", new UpsertCountryRequest("Japan", null, null, null, null));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<CountryResponse>();
            Assert.NotNull(created);
            await using var context = fixture.CreateContext();
            Assert.Equal("Japan", (await context.Countries.AsNoTracking().SingleAsync(row => row.Id == created.Id)).Name);
            cache.ReleaseFill.TrySetResult();
            await inflight;
            Assert.Equal("Thailand", Assert.Single(JsonSerializer.Deserialize<CountryResponse[]>((await cache.GetAsync("legacy:country:all:v1"))!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!).Name);
            var after = await client.GetFromJsonAsync<CountryResponse[]>("/Countries");
            Assert.NotNull(after);
            Assert.Equal(new[] { "Japan", "Thailand" }, after.Select(row => row.Name));
            var peerRead = await peer.GetFromJsonAsync<CountryResponse[]>("/country/v1/countries");
            Assert.NotNull(peerRead);
            Assert.Equal(new[] { "Japan", "Thailand" }, peerRead.Select(row => row.Name));
        }
        finally
        {
            cache.ReleaseFill.TrySetResult();
            await inflight;
        }
    }

    private sealed class PendingFillCache : IDistributedCache, IDisposable
    {
        private readonly MemoryDistributedCache inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        private int fills;
        public TaskCompletionSource FillEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFill { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[]? Get(string key) => inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);
        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (Interlocked.Increment(ref fills) == 1)
            {
                FillEntered.TrySetResult();
                await ReleaseFill.Task.WaitAsync(token);
            }
            await inner.SetAsync(key, value, options, token);
        }
        public void Remove(string key) => inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
        public void Refresh(string key) => inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
        public void Dispose()
        {
            ReleaseFill.TrySetResult();
            if ((object)inner is IDisposable disposable) disposable.Dispose();
        }
    }
}
