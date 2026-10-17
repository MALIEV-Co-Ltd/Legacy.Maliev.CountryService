using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Stj = System.Text.Json.JsonSerializer;

namespace Legacy.Maliev.CountryService.Tests.Integration;

[Collection("Country lifecycle external configuration")]
public sealed class CountryOriginalBodyHttpTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    private const string CacheKey = "legacy:country:all:v1";
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public static IEnumerable<object?[]> SourceSuccessBodies()
    {
        foreach (var method in new[] { "POST", "PUT" })
        {
            yield return [method, "{Name:'Thailand',CountryCode:0x10}", "Thailand", "0x10"];
            yield return [method, "/*before*/{\"Name\":\"Thailand\",\"CountryCode\":010,}", "Thailand", "010"];
            yield return [method, "{\"Name\":\"Thailand\",\"CountryCode\":.5}", "Thailand", ".5"];
            yield return [method, "{\"Name\":\"Thailand\",\"CountryCode\":NaN}", "Thailand", "NaN"];
            yield return [method, "{\"$ref\":null,\"Name\":\"Thailand\",\"CountryCode\":764,\"COUNTRYCODE\":null}", "Thailand", "764"];
            yield return [method, "{\"$id\":\"1\",\"$type\":\"unresolvable, private-marker\",\"Name\":\"Thailand\",\"CountryCode\":true}", "Thailand", "true"];
            yield return [method, "{\"Name\":\"Thailand\",\"$ref\":1,\"CountryCode\":\"late-metadata\"}", "Thailand", "late-metadata"];
            yield return [method, "{\"Name\":\"Thailand\",\"Id\":\"2147483647\",\"CreatedDate\":\"1900-01-01T00:00:00Z\",\"ModifiedDate\":\"/Date(0+0700)/\"}", "Thailand", null];
            yield return [method, "{\"Name\":\"\\uD800\"}", "\uFFFD", null];
            yield return [method, "{\"Name\":\"\\uDC00\"}", "\uFFFD", null];
            yield return [method, "{\"Name\":\"\\uD83D\\uDE00\"}", "😀", null];
            yield return [method, "{\"Name\":\"\\uFFFD\"}", "\uFFFD", null];
        }
    }

    [Theory]
    [MemberData(nameof(SourceSuccessBodies))]
    public async Task OriginalBody_ProductionCompositionPersistsOnlyConsumedValuesAndInvalidatesCache(
        string method, string json, string expectedName, string? expectedCode)
    {
        var source = await SourceAsync(json);
        Assert.False(source.HasError);
        var original = Assert.IsType<CountryOriginalBodyBoundaryTests.SourceCountry>(source.Model);
        Assert.Equal(expectedName, original.Name);
        Assert.Equal(expectedCode, original.CountryCode);
        var id = await fixture.SeedAsync("Before", "TH", "THA");
        var before = Assert.Single(await SnapshotAsync());
        using var baseFactory = fixture.CreateDocumentationFactory();
        using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = Client(app, CountryPermissions.CountriesCreate, CountryPermissions.CountriesUpdate, CountryPermissions.CountriesRead);
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        await cache.SetAsync(CacheKey, [7, 4, 1]);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = method == "POST" ? await client.PostAsync("/Countries", content) : await client.PutAsync($"/Countries/{id}", content);
        Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        var rows = await SnapshotAsync();
        var changed = method == "POST" ? Assert.Single(rows, value => value.Id != id) : Assert.Single(rows);
        Assert.Equal(expectedName, changed.Name);
        Assert.Equal(expectedCode, changed.CountryCode);
        Assert.Null(changed.Continent);
        Assert.Null(changed.Iso2);
        Assert.Null(changed.Iso3);
        Assert.NotEqual(int.MaxValue, changed.Id);
        Assert.True(changed.ModifiedDate.HasValue && changed.ModifiedDate.Value.Year > 2000);
        Assert.Null(await cache.GetAsync(CacheKey));
        if (method == "POST")
        {
            Assert.Equal(before, Assert.Single(rows, value => value.Id == id));
            Assert.True(changed.CreatedDate.HasValue && changed.CreatedDate.Value.Year > 2000);
            Assert.EndsWith($"/Countries/{changed.Id}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
            using var wire = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expectedName, wire.RootElement.GetProperty("name").GetString());
            Assert.Equal(changed.Id, wire.RootElement.GetProperty("id").GetInt32());
            Assert.DoesNotContain(wire.RootElement.EnumerateObject(), property => property.Name.StartsWith('$'));
        }
        else
        {
            Assert.Equal(before.Id, changed.Id);
            Assert.Equal(before.CreatedDate, changed.CreatedDate);
            Assert.NotEqual(before.Version, changed.Version);
        }
        using var detail = await client.GetAsync($"/Countries/{changed.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var read = (await detail.Content.ReadFromJsonAsync<CountryResponse>())!;
        Assert.Equal(changed.Name, read.Name);
        Assert.Equal(changed.CountryCode, read.CountryCode);
        Assert.Equal(changed.CreatedDate, read.CreatedDate);
        Assert.Equal(changed.ModifiedDate, read.ModifiedDate);
    }

    public static IEnumerable<object[]> RejectedBodies()
    {
        foreach (var method in new[] { "POST", "PUT" })
            foreach (var json in new[]
            {
                "{\"$ref\":1,\"Name\":\"private-marker\"}",
                "{\"$ref\":true,\"Name\":\"private-marker\"}",
                "{\"$ref\":\"missing\",\"Name\":\"private-marker\"}",
                "{\"$values\":[],\"Name\":\"private-marker\"}",
                "{\"Id\":2147483648,\"Id\":1,\"Name\":\"private-marker\"}",
                "{\"CreatedDate\":\"private-marker\",\"CreatedDate\":null,\"Name\":\"Thailand\"}",
                "{\"ModifiedDate\":{},\"Name\":\"Thailand\"}",
                "{\"Name\":{},\"Name\":\"private-marker\"}",
                "{\"Name\":\"Thailand\",\"unknown\":[}",
            }) yield return [method, json];
    }

    [Theory]
    [MemberData(nameof(RejectedBodies))]
    public async Task InvalidCompleteBody_ReturnsPrivate400WithoutRowsVersionsOrCacheMutation(string method, string json)
    {
        Assert.True((await SourceAsync(json)).HasError);
        var id = await fixture.SeedAsync("Before", "TH", "THA");
        var before = await SnapshotAsync();
        using var baseFactory = fixture.CreateDocumentationFactory();
        using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = Client(app, CountryPermissions.CountriesCreate, CountryPermissions.CountriesUpdate);
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        byte[] marker = [7, 4, 1];
        await cache.SetAsync(CacheKey, marker);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = method == "POST" ? await client.PostAsync("/Countries", content) : await client.PutAsync($"/Countries/{id}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.TryGetProperty("errors", out _));
        var text = problem.RootElement.GetRawText();
        Assert.DoesNotContain("private-marker", text, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(marker, await cache.GetAsync(CacheKey));
    }

    [Theory]
    [InlineData("POST", "anonymous", 401)]
    [InlineData("PUT", "anonymous", 401)]
    [InlineData("POST", "wrong-permission", 403)]
    [InlineData("PUT", "wrong-permission", 403)]
    public async Task NormalJwtPermissionOrdering_PrecedesInvalidSourceBodyWithoutStateChanges(string method, string profile, int status)
    {
        var id = await fixture.SeedAsync("Before");
        var before = await SnapshotAsync();
        using var baseFactory = fixture.CreateDocumentationFactory();
        using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (profile != "anonymous")
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token("valid", CountryPermissions.CountriesRead));
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        byte[] marker = [2, 8];
        await cache.SetAsync(CacheKey, marker);
        using var content = new StringContent("{\"$ref\":1,\"Name\":\"private-marker\"}", Encoding.UTF8, "application/json");
        using var response = method == "POST" ? await client.PostAsync("/Countries", content) : await client.PutAsync($"/Countries/{id}", content);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(marker, await cache.GetAsync(CacheKey));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task ProjectedDto_StillAppliesAllExistingRequiredAndLengthRules(string method)
    {
        var id = await fixture.SeedAsync("Before");
        var before = await SnapshotAsync();
        using var baseFactory = fixture.CreateDocumentationFactory();
        using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = Client(app, CountryPermissions.CountriesCreate, CountryPermissions.CountriesUpdate);
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        byte[] marker = [4, 2];
        await cache.SetAsync(CacheKey, marker);
        foreach (var (field, value) in new (string Field, string? Value)[]
        {
            ("Name", null), ("Name", ""), ("Name", " "), ("Name", new string('n', 51)),
            ("Continent", new string('c', 51)), ("CountryCode", new string('c', 31)), ("Iso2", "AAA"), ("Iso3", "AAAA"),
        })
        {
            var values = new Dictionary<string, string?> { ["Name"] = "Thailand", [field] = value };
            var json = Stj.Serialize(values);
            Assert.False((await SourceAsync(json)).HasError);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = method == "POST" ? await client.PostAsync("/Countries", content) : await client.PutAsync($"/Countries/{id}", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await SnapshotAsync());
            Assert.Equal(marker, await cache.GetAsync(CacheKey));
        }
    }

    [Theory]
    [InlineData("POST", "")]
    [InlineData("PUT", "")]
    [InlineData("POST", "null")]
    [InlineData("PUT", "null")]
    [InlineData("POST", "\"\"")]
    [InlineData("PUT", "\"\"")]
    public async Task SourceNoValue_Remains400AtBodyModelBinding(string method, string json)
    {
        var id = await fixture.SeedAsync("Before");
        var before = await SnapshotAsync();
        var original = await SourceAsync(json);
        Assert.False(original.HasError);
        Assert.False(original.IsModelSet);
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate, CountryPermissions.CountriesUpdate);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = method == "POST" ? await client.PostAsync("/Countries", content) : await client.PutAsync($"/Countries/{id}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task SourceSyntax_DoesNotChangeZeroIdentifierOrMissingRowOutcomes()
    {
        await fixture.SeedAsync("Before");
        var before = await SnapshotAsync();
        using var client = fixture.CreateClient(CountryPermissions.CountriesUpdate);
        foreach (var (id, status) in new[] { (0, HttpStatusCode.BadRequest), (int.MaxValue, HttpStatusCode.NotFound) })
        {
            using var content = new StringContent("{Name:'Thailand'}", Encoding.UTF8, "application/json");
            using var response = await client.PutAsync($"/Countries/{id}", content);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(before, await SnapshotAsync());
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task ParsedNul_DoesNotBypassTheUnchangedPostgresStorageBoundary(string method)
    {
        const string json = "{\"Name\":\"\\u0000\"}";
        var source = await SourceAsync(json);
        Assert.Equal("\0", Assert.IsType<CountryOriginalBodyBoundaryTests.SourceCountry>(source.Model).Name);
        var id = await fixture.SeedAsync("Before");
        var before = await SnapshotAsync();
        using var baseFactory = fixture.CreateDocumentationFactory();
        using var app = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = Client(app, CountryPermissions.CountriesCreate, CountryPermissions.CountriesUpdate);
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        byte[] marker = [1, 9];
        await cache.SetAsync(CacheKey, marker);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = method == "POST" ? await client.PostAsync("/Countries", content) : await client.PutAsync($"/Countries/{id}", content);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(marker, await cache.GetAsync(CacheKey));
        Assert.DoesNotContain("StackTrace", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingDirectStjConfigurationAndRequestResponseMetadata_RemainRegistered()
    {
        using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        Assert.Single(factory.Services.GetServices<IConfigureOptions<JsonOptions>>(), CountryRequestNativeOptions.IsOwnedConfiguration);
        Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertCountryRequest>("{\"Name\":\"Thailand\"} {}", options));
        Assert.Equal("764", Stj.Deserialize<UpsertCountryRequest>("{\"CountryCode\":764}", options)!.CountryCode);
        var request = new UpsertCountryRequest("Thailand", "Asia", "764", "TH", "THA");
        Assert.Equal("{\"name\":\"Thailand\",\"continent\":\"Asia\",\"countryCode\":\"764\",\"iso2\":\"TH\",\"iso3\":\"THA\"}", Stj.Serialize(request, options));
    }

    private HttpClient Client(WebApplicationFactory<Program> factory, params string[] permissions)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token("valid", permissions));
        return client;
    }

    internal async Task<Stored[]> SnapshotAsync()
    {
        await using var context = fixture.CreateContext();
        return await context.Countries.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new Stored(row.Id, row.Name, row.Continent, row.CountryCode, row.Iso2, row.Iso3,
                row.CreatedDate, row.ModifiedDate, EF.Property<uint>(row, "xmin"))).ToArrayAsync();
    }

    internal sealed record Stored(int Id, string Name, string? Continent, string? CountryCode, string? Iso2,
        string? Iso3, DateTime? CreatedDate, DateTime? ModifiedDate, uint Version);

    internal static async Task<InputFormatterResult> SourceAsync(string json)
    {
        var options = new MvcNewtonsoftJsonOptions();
        options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        var formatter = new NewtonsoftJsonInputFormatter(NullLogger.Instance, options.SerializerSettings,
            ArrayPool<char>.Shared, new DefaultObjectPoolProvider(), new MvcOptions(), options);
        var http = new DefaultHttpContext();
        http.Request.ContentType = "application/json; charset=utf-8";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = new InputFormatterContext(http, "request", new ModelStateDictionary(),
            new EmptyModelMetadataProvider().GetMetadataForType(typeof(CountryOriginalBodyBoundaryTests.SourceCountry)),
            (stream, encoding) => new StreamReader(stream, encoding, true, 1024, leaveOpen: true));
        return await formatter.ReadAsync(context);
    }
}
