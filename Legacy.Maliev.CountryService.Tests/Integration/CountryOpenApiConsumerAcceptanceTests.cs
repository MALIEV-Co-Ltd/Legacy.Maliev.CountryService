using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CountryService.Tests.Integration;

/// <summary>Published contracts paired with actual normal HTTP and disposable PostgreSQL behavior.</summary>
[Collection("Country lifecycle external configuration")]
public sealed class CountryOpenApiConsumerAcceptanceTests(CountryLifecycleFixture fixture, CountryNormalIamFixture normal)
    : IClassFixture<CountryLifecycleFixture>, IClassFixture<CountryNormalIamFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_NullableInput_PersistsIntegerIdentityAndLocation_Advertised201Agrees()
    {
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate, CountryPermissions.CountriesRead);
        using var response = await client.PostAsJsonAsync("/Countries", new { name = "Thailand" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CountryResponse>();
        Assert.NotNull(body);
        Assert.True(body.Id > 0);
        Assert.NotNull(response.Headers.Location);
        using var get = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(body.Id, (await get.Content.ReadFromJsonAsync<CountryResponse>())!.Id);
        await using var db = fixture.CreateContext();
        var stored = await db.Countries.AsNoTracking().SingleAsync(value => value.Id == body.Id);
        Assert.Equal("Thailand", stored.Name);
        Assert.Null(stored.Continent);
        Assert.Null(stored.CountryCode);
        Assert.Null(stored.Iso2);
        Assert.Null(stored.Iso3);
        using var document = await DocumentAsync();
        var schema = Schema(document, Operation(document, "/Countries", "post").GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.Equal(new[] { "name" }, schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        AssertResponse(document, "/Countries", "post", "201");
        var createdSchema = Schema(document, Operation(document, "/Countries", "post").GetProperty("responses")
            .GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        var idType = createdSchema.GetProperty("properties").GetProperty("id").GetProperty("type");
        Assert.True(idType.ValueKind == JsonValueKind.String ? idType.GetString() == "integer"
            : idType.ValueKind == JsonValueKind.Array && idType.EnumerateArray().Select(value => value.GetString()).SequenceEqual(["integer"]),
            "The published response identity must be nonnullable integer only: " + idType.GetRawText());
        Assert.Equal("int32", createdSchema.GetProperty("properties").GetProperty("id").GetProperty("format").GetString());
    }

    [Fact]
    public async Task OversizedName_ActualValidationProblemAndNoWrite_Advertised400Agrees()
    {
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate);
        using var response = await client.PostAsJsonAsync("/Countries", new { name = new string('x', 51) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Contains(problem.RootElement.GetProperty("errors").EnumerateObject(), value =>
            value.Name.Equals("Name", StringComparison.OrdinalIgnoreCase));
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Countries.AsNoTracking().ToArrayAsync());
        using var document = await DocumentAsync();
        var schema = Schema(document, Operation(document, "/Countries", "post").GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.Equal(50, schema.GetProperty("properties").GetProperty("name").GetProperty("maxLength").GetInt32());
        AssertResponse(document, "/Countries", "post", "400");
    }

    [Fact]
    public async Task ProtectedRead_Actual401And403_DoNotExposeRow_AdvertisedAuthAgrees()
    {
        var id = await fixture.SeedAsync("Thailand", "TH", "THA");
        using var anonymous = fixture.CreateAnonymousClient();
        using var unauthorized = await anonymous.GetAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Contains(unauthorized.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        var credential = Guid.NewGuid().ToString("N");
        var denied = new DeniedReadIamTransport();
        CountryWorkloadExchangeObservation? exchange = null;
        await using var deniedApp = fixture.CreateDocumentationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("IAM:LivePermissionChecks:Credential", credential);
            exchange = CountryTestWorkloadExchange.Prepare(builder);
            builder.ConfigureTestServices(services => services.AddHttpClient("IAMService", http =>
                http.BaseAddress = new Uri("https://country32-iam.invalid"))
                .ConfigurePrimaryHttpMessageHandler(() => denied));
        });
        using var forbiddenClient = deniedApp.CreateClient();
        forbiddenClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            fixture.Token("valid", CountryPermissions.CountriesCreate));
        using var forbidden = await forbiddenClient.GetAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.True(denied.Calls > 0);
        Assert.NotNull(exchange);
        Assert.True(exchange.Calls > 0);
        Assert.Equal(HttpMethod.Post, denied.Method);
        Assert.Equal("/iam/v1/auth/check-permission", denied.Path);
        Assert.Equal("Bearer", denied.BearerScheme);
        Assert.Equal("employee:country-acceptance", denied.Principal);
        Assert.Equal(CountryPermissions.CountriesRead, denied.Permission);
        Assert.Equal("global", denied.Resource);
        Assert.False(denied.BypassCache);
        Assert.False(denied.HasLiveCredential);
        await using var db = fixture.CreateContext();
        Assert.Equal("Thailand", (await db.Countries.AsNoTracking().SingleAsync(value => value.Id == id)).Name);
        using var document = await DocumentAsync();
        Assert.NotEmpty(Operation(document, "/Countries/{id}", "get").GetProperty("security").EnumerateArray());
        AssertResponse(document, "/Countries/{id}", "get", "401");
        AssertResponse(document, "/Countries/{id}", "get", "403");
    }

    [Fact]
    public async Task Delete_GenuinePostgresXminRace_PreservesWinner_Advertised409Agrees()
    {
        var row = await normal.SeedAsync();
        await using var before = normal.Context();
        var xmin = await before.Countries.Where(value => value.Id == row.Id)
            .Select(value => EF.Property<uint>(value, "xmin")).SingleAsync();
        var boundary = new CountryNormalIamBoundary();
        boundary.Save.BeforeSave = async token =>
        {
            await using var winner = normal.Context();
            var changed = await winner.Countries.SingleAsync(value => value.Id == row.Id, token);
            changed.Name = "Concurrent winner";
            await winner.SaveChangesAsync(token);
        };
        await using var app = normal.App(boundary, environment: "Development");
        using var client = normal.Client(app);
        using var response = await client.DeleteAsync($"/Countries/{row.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("The country changed during this request.", await response.Content.ReadAsStringAsync());
        Assert.True(boundary.LoginCalls > 0);
        Assert.True(boundary.IamCalls > 0);
        await using var readback = normal.Context();
        Assert.Equal("Concurrent winner", (await readback.Countries.AsNoTracking().SingleAsync(value => value.Id == row.Id)).Name);
        Assert.NotEqual(xmin, await readback.Countries.Where(value => value.Id == row.Id)
            .Select(value => EF.Property<uint>(value, "xmin")).SingleAsync());
        using var document = JsonDocument.Parse(await client.GetStringAsync("/countries/openapi/v1.json"));
        AssertResponse(document, "/Countries/{id}", "delete", "409");
        var conflictType = Operation(document, "/Countries/{id}", "delete").GetProperty("responses")
            .GetProperty("409").GetProperty("content").GetProperty("text/plain").GetProperty("schema")
            .GetProperty("type");
        Assert.True(conflictType.ValueKind == JsonValueKind.String ? conflictType.GetString() == "string"
            : conflictType.ValueKind == JsonValueKind.Array && conflictType.EnumerateArray().Select(value => value.GetString()).SequenceEqual(["string"]),
            "The published conflict body must be nonnullable string only: " + conflictType.GetRawText());
    }

    [Fact]
    public async Task IntegerId_ActualReadAndNonintegerRejection_AdvertisedParameterHasSourceDescription()
    {
        var id = await fixture.SeedAsync("Thailand", "TH", "THA");
        using var client = fixture.CreateClient(CountryPermissions.CountriesRead);
        using var response = await client.GetAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id, body.RootElement.GetProperty("id").GetInt32());
        using var noninteger = await client.GetAsync("/Countries/not-an-integer");
        Assert.Equal(HttpStatusCode.NotFound, noninteger.StatusCode);
        using var document = await DocumentAsync();
        var parameter = Assert.Single(Operation(document, "/Countries/{id}", "get").GetProperty("parameters")
            .EnumerateArray(), value => value.GetProperty("name").GetString() == "id");
        Assert.Equal("path", parameter.GetProperty("in").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.Equal("integer", parameter.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("int32", parameter.GetProperty("schema").GetProperty("format").GetString());
        Assert.True(parameter.TryGetProperty("description", out var description)
            && !string.IsNullOrWhiteSpace(description.GetString()), "Original source documents the identifier parameter; consumers need its semantics.");
    }

    [Fact]
    public async Task SupportedProperties_RealCreateAndImportedNullDates_HaveSourceBackedPublishedDescriptions()
    {
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate, CountryPermissions.CountriesRead);
        using var response = await client.PostAsJsonAsync("/Countries", new
        {
            name = "Thailand",
            continent = "Asia",
            countryCode = "+66",
            iso2 = "TH",
            iso3 = "THA"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CountryResponse>();
        Assert.NotNull(created);
        Assert.True(created.Id > 0);
        await using var db = fixture.CreateContext();
        var stored = await db.Countries.AsNoTracking().SingleAsync(value => value.Id == created.Id);
        Assert.Equal("Thailand", stored.Name);
        Assert.Equal("Asia", stored.Continent);
        Assert.Equal("+66", stored.CountryCode);
        Assert.Equal("TH", stored.Iso2);
        Assert.Equal("THA", stored.Iso3);
        Assert.NotNull(stored.CreatedDate);
        Assert.NotNull(stored.ModifiedDate);
        Assert.NotNull(created.CreatedDate);
        Assert.NotNull(created.ModifiedDate);
        using var persistedResponse = await client.GetAsync($"/Countries/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, persistedResponse.StatusCode);
        var persisted = await persistedResponse.Content.ReadFromJsonAsync<CountryResponse>();
        Assert.NotNull(persisted);
        Assert.Equal(stored.CreatedDate, persisted.CreatedDate);
        Assert.Equal(stored.ModifiedDate, persisted.ModifiedDate);
        var importedId = await fixture.SeedAsync("Imported country");
        // INSERT defaults populate dates; model an explicitly null imported row by updating this owned synthetic row.
        await db.Countries.Where(value => value.Id == importedId).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.CreatedDate, (DateTime?)null)
            .SetProperty(value => value.ModifiedDate, (DateTime?)null));
        using var importedResponse = await client.GetAsync($"/Countries/{importedId}");
        Assert.Equal(HttpStatusCode.OK, importedResponse.StatusCode);
        var imported = await importedResponse.Content.ReadFromJsonAsync<CountryResponse>();
        Assert.NotNull(imported);
        Assert.Null(imported.CreatedDate);
        Assert.Null(imported.ModifiedDate);
        var importedStored = await db.Countries.AsNoTracking().SingleAsync(value => value.Id == importedId);
        Assert.Null(importedStored.CreatedDate);
        Assert.Null(importedStored.ModifiedDate);

        using var document = await DocumentAsync();
        var post = Operation(document, "/Countries", "post");
        var request = Schema(document, post.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"));
        var output = Schema(document, post.GetProperty("responses").GetProperty("201")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        var anchors = new Dictionary<string, string>
        {
            ["id"] = "identifier",
            ["name"] = "name",
            ["continent"] = "continent",
            ["countryCode"] = "country code",
            ["iso2"] = "iso2",
            ["iso3"] = "iso3",
            ["createdDate"] = "created date",
            ["modifiedDate"] = "modified date"
        };
        var lengths = new Dictionary<string, int>
        {
            ["name"] = 50,
            ["continent"] = 50,
            ["countryCode"] = 30,
            ["iso2"] = 2,
            ["iso3"] = 3
        };
        Assert.Equal(lengths.Keys.Order(), request.GetProperty("properties").EnumerateObject().Select(value => value.Name).Order());
        Assert.Equal(new[] { "name" }, request.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.All(lengths, entry => Assert.Equal(entry.Value,
            request.GetProperty("properties").GetProperty(entry.Key).GetProperty("maxLength").GetInt32()));
        var descriptions = anchors.Select(entry => (Output: true, Property: entry.Key, Anchor: entry.Value))
            .Concat(lengths.Keys.Select(name => (Output: false, Property: name, Anchor: anchors[name])));
        Assert.All(descriptions, entry => AssertSourceDescription(entry.Output ? output : request, entry.Property, entry.Anchor));
    }

    private static void AssertSourceDescription(JsonElement schema, string property, string anchor)
    {
        var field = schema.GetProperty("properties").GetProperty(property);
        Assert.True(field.TryGetProperty("description", out var description)
            && description.ValueKind == JsonValueKind.String
            && description.GetString()!.Contains(anchor, StringComparison.OrdinalIgnoreCase),
            $"Published property {property} must retain the source comment anchor '{anchor}'.");
    }

    private async Task<JsonDocument> DocumentAsync()
    {
        await using var app = fixture.CreateDocumentationFactory().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/countries/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Operation(JsonDocument document, string path, string method) =>
        document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static void AssertResponse(JsonDocument document, string path, string method, string status) =>
        Assert.True(Operation(document, path, method).GetProperty("responses").TryGetProperty(status, out _),
            $"Published {method} {path} must describe reached HTTP {status}.");

    private static JsonElement Schema(JsonDocument document, JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference)
            ? document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1])
            : schema;

    private sealed class DeniedReadIamTransport : HttpMessageHandler
    {
        internal int Calls;
        internal HttpMethod? Method;
        internal string? Path, BearerScheme, Principal, Permission, Resource;
        internal bool? BypassCache;
        internal bool HasLiveCredential;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            BearerScheme = request.Headers.Authorization?.Scheme;
            HasLiveCredential = request.Headers.Contains("X-Maliev-IAM-Live-Check-Key");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Principal = body.RootElement.GetProperty("principalId").GetString();
            Permission = body.RootElement.GetProperty("permissionId").GetString();
            Resource = body.RootElement.GetProperty("resourcePath").GetString();
            BypassCache = body.RootElement.GetProperty("bypassCache").GetBoolean();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { allowed = false }) };
        }
    }
}
