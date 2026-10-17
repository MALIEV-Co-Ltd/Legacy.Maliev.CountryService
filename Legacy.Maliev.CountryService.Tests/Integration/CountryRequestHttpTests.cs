using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CountryService.Tests.Integration;

[Collection("Country lifecycle external configuration")]
public sealed class CountryRequestHttpTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("POST", "name", "12", "12")]
    [InlineData("POST", "continent", "true", "true")]
    [InlineData("POST", "countryCode", "764", "764")]
    [InlineData("POST", "iso2", "12", "12")]
    [InlineData("POST", "iso3", "-12", "-12")]
    [InlineData("PUT", "name", "12", "12")]
    [InlineData("PUT", "continent", "true", "true")]
    [InlineData("PUT", "countryCode", "764", "764")]
    [InlineData("PUT", "iso2", "12", "12")]
    [InlineData("PUT", "iso3", "-12", "-12")]
    public async Task ConsumedScalar_ActualRegisteredMutationPersistsStringAndReadWire(string method, string field, string token, string expected)
    {
        var id = method == "PUT" ? await fixture.SeedAsync("Before") : 0;
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate,
            CountryPermissions.CountriesUpdate, CountryPermissions.CountriesRead);
        var json = field == "name" ? "{\"name\":" + token + "}" : "{\"name\":\"Thailand\",\"" + field + "\":" + token + "}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = method == "POST"
            ? await client.PostAsync("/Countries", content)
            : await client.PutAsync($"/Countries/{id}", content);
        Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        if (method == "POST")
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("id").GetInt32();
            Assert.True(id > 0);
            Assert.EndsWith($"/Countries/{id}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
            Assert.Equal(JsonValueKind.String, created.RootElement.GetProperty(field).ValueKind);
            Assert.Equal(expected, created.RootElement.GetProperty(field).GetString());
        }
        await using var context = fixture.CreateContext();
        var stored = await context.Countries.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.Equal(expected, field switch
        {
            "name" => stored.Name,
            "continent" => stored.Continent,
            "countryCode" => stored.CountryCode,
            "iso2" => stored.Iso2,
            "iso3" => stored.Iso3,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        });
        Assert.NotNull(stored.ModifiedDate);
        using var detail = await client.GetAsync($"/Countries/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var body = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty(field).ValueKind);
        Assert.Equal(expected, body.RootElement.GetProperty(field).GetString());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task OrderedNullAndUnknownNested_ActualMutationRetainsLastNonNullAndServerOwnership(string method)
    {
        var id = method == "PUT" ? await fixture.SeedAsync("Before") : 0;
        var before = method == "PUT" ? await SnapshotAsync() : null;
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate, CountryPermissions.CountriesUpdate);
        const string json = "{\"name\":\"Thailand\",\"countryCode\":764,\"COUNTRYCODE\":null,\"unknown\":{\"countryCode\":[{}]},\"id\":99999,\"createdDate\":\"1900-01-01T00:00:00Z\",\"modifiedDate\":\"1900-01-01T00:00:00Z\"}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = method == "POST"
            ? await client.PostAsync("/Countries", content)
            : await client.PutAsync($"/Countries/{id}", content);
        Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        await using var context = fixture.CreateContext();
        var stored = await context.Countries.AsNoTracking().SingleAsync();
        Assert.NotEqual(99999, stored.Id);
        Assert.Equal("764", stored.CountryCode);
        Assert.Equal("Thailand", stored.Name);
        Assert.Null(stored.Continent);
        Assert.Null(stored.Iso2);
        Assert.Null(stored.Iso3);
        Assert.True(stored.ModifiedDate.HasValue && stored.ModifiedDate.Value > new DateTime(2000, 1, 1));
        if (method == "POST") Assert.True(stored.CreatedDate.HasValue && stored.CreatedDate.Value > new DateTime(2000, 1, 1));
        else Assert.Equal(before!.CreatedDate, stored.CreatedDate);
    }

    [Theory]
    [InlineData("{\"name\":\"Thailand\",\"countryCode\":{\"private-marker\":1}}")]
    [InlineData("{\"name\":\"Thailand\",\"iso2\":[]}")]
    [InlineData("{\"name\":\"Thailand\",\"iso2\":false}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\"\"}")]
    [InlineData("{\"name\":\" \"}")]
    [InlineData("{\"name\":\"Thailand\"} {}")]
    [InlineData("{\"name\":\"Thailand\",\"unknown\":[}")]
    public async Task InvalidConsumedInput_ActualPostAndPutLeaveExistingRowAndReturnPrivate400(string json)
    {
        var id = await fixture.SeedAsync("Before", "TH", "THA");
        var before = await SnapshotAsync();
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate, CountryPermissions.CountriesUpdate);
        foreach (var method in new[] { "POST", "PUT" })
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = method == "POST"
                ? await client.PostAsync("/Countries", content)
                : await client.PutAsync($"/Countries/{id}", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private-marker", body, StringComparison.Ordinal);
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
            var stored = await SnapshotAsync();
            Assert.Equal("Before", stored.Name);
            Assert.Equal("TH", stored.Iso2);
            Assert.Equal("THA", stored.Iso3);
            Assert.Equal(before, stored);
        }
    }

    [Fact]
    public async Task NonProductionOpenApi_RequestSchemaMatchesIndependentNativeHostAndExistingFiveFieldBounds()
    {
        using var candidateFactory = fixture.CreateDocumentationFactory();
        using var nativeBaseFactory = fixture.CreateDocumentationFactory();
        using var nativeFactory = nativeBaseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(CountryRequestNativeOptions.RemoveOwnedConfiguration));
        using var candidateClient = candidateFactory.CreateClient();
        using var nativeClient = nativeFactory.CreateClient();
        var nativeOptions = nativeFactory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        Assert.Single(candidateFactory.Services.GetServices<IConfigureOptions<JsonOptions>>(),
            CountryRequestNativeOptions.IsOwnedConfiguration);
        Assert.DoesNotContain(nativeFactory.Services.GetServices<IConfigureOptions<JsonOptions>>(),
            CountryRequestNativeOptions.IsOwnedConfiguration);
        CountryRequestNativeOptions.AssertNativeScope(nativeOptions);
        using var candidateResponse = await candidateClient.GetAsync("/countries/openapi/v1.json");
        using var nativeResponse = await nativeClient.GetAsync("/countries/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, candidateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, nativeResponse.StatusCode);
        var candidate = JsonNode.Parse(await candidateResponse.Content.ReadAsStringAsync())!;
        var native = JsonNode.Parse(await nativeResponse.Content.ReadAsStringAsync())!;
        Assert.True(JsonNode.DeepEquals(native["components"]!["schemas"], candidate["components"]!["schemas"]),
            "Owned request reading must not replace native request or response schema.");
        foreach (var path in new[] { "/Countries", "/Countries/{id}", "/country/v1/countries" })
        {
            foreach (var verb in new[] { "get", "post", "put", "delete" })
            {
                if (native["paths"]![path]![verb] is not { } operation) continue;
                Assert.True(JsonNode.DeepEquals(operation["responses"], candidate["paths"]![path]![verb]!["responses"]));
            }
        }
        var candidateSchema = RequestSchema(candidate);
        Assert.True(JsonNode.DeepEquals(RequestSchema(native), candidateSchema),
            "The scoped request reader must preserve independently generated native request schema.");
        var properties = candidateSchema["properties"]!.AsObject();
        Assert.Equal(new[] { "continent", "countryCode", "iso2", "iso3", "name" },
            properties.Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.Equal(50, properties["name"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(50, properties["continent"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(30, properties["countryCode"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(2, properties["iso2"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(3, properties["iso3"]!["maxLength"]!.GetValue<int>());
        Assert.Contains(candidateSchema["required"]!.AsArray(), value => value!.GetValue<string>() == "name");
    }

    private async Task<CountryResponse> SnapshotAsync()
    {
        await using var context = fixture.CreateContext();
        return await context.Countries.AsNoTracking()
            .Select(country => new CountryResponse(
                country.Id, country.Name, country.Continent, country.CountryCode,
                country.Iso2, country.Iso3, country.CreatedDate, country.ModifiedDate))
            .SingleAsync();
    }

    private static JsonNode RequestSchema(JsonNode document)
    {
        var schema = document["paths"]!["/Countries"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        return schema["$ref"] is JsonValue reference
            ? document["components"]!["schemas"]![reference.GetValue<string>().Split('/')[^1]]!
            : schema;
    }
}
