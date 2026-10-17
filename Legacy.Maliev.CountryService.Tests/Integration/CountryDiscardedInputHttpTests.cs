using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CountryService.Tests.Integration;

[Collection("Country lifecycle external configuration")]
public sealed class CountryDiscardedInputHttpTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    public static IEnumerable<object[]> InvalidInputs()
    {
        foreach (var token in new[] { "2147483648", "-2147483649", "1.0", "true", "{}", "[]", "\"private-marker\"" })
            yield return ["id", token];
        foreach (var field in new[] { "createdDate", "modifiedDate" })
            foreach (var token in new[] { "0", "true", "{}", "[]", "\"private-marker\"",
                "\"2026-02-30\"", "\"/Date(253402300800000)/\"" })
                yield return [field, token];
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task InvalidDiscardedInput_PostAndPutReturnPrivate400WithoutChangingAnyStoredField(string field, string token)
    {
        var id = await fixture.SeedAsync("Before", "TH", "THA");
        await SeedAuditDatesAsync(id);
        var before = await SnapshotAsync();
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate, CountryPermissions.CountriesUpdate);
        foreach (var method in new[] { "POST", "PUT" })
        {
            foreach (var input in new[]
            {
                "\""+ field + "\":" + token,
                "\""+ field + "\":" + token + ",\"" + field.ToUpperInvariant() + "\":null",
                "\""+ field + "\":null,\"" + field.ToUpperInvariant() + "\":" + token,
            })
            {
                using var content = new StringContent("{\"name\":\"Changed\"," + input + "}", Encoding.UTF8, "application/json");
                using var response = method == "POST"
                    ? await client.PostAsync("/Countries", content)
                    : await client.PutAsync($"/Countries/{id}", content);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("private-marker", body, StringComparison.Ordinal);
                Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, await SnapshotAsync());
            }
        }
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task ValidDiscardedInputs_DoNotAcquireIdentityOrAuditWriteAuthority(string method)
    {
        var id = method == "PUT" ? await fixture.SeedAsync("Before", "TH", "THA") : 0;
        if (method == "PUT") await SeedAuditDatesAsync(id);
        var before = await SnapshotAsync();
        using var client = fixture.CreateClient(CountryPermissions.CountriesCreate,
            CountryPermissions.CountriesUpdate, CountryPermissions.CountriesRead);
        const string json = "{\"name\":\"Thailand\",\"continent\":\"Asia\",\"countryCode\":\"764\",\"iso2\":\"TH\",\"iso3\":\"THA\",\"id\":\"2147483647\",\"ID\":null,\"createdDate\":\"1900-01-01T00:00:00Z\",\"CREATEDDATE\":\"\",\"modifiedDate\":\"/Date(0+0700)/\",\"MODIFIEDDATE\":null}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = method == "POST"
            ? await client.PostAsync("/Countries", content)
            : await client.PutAsync($"/Countries/{id}", content);
        Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.NoContent, response.StatusCode);
        var stored = Assert.Single(await SnapshotAsync());
        Assert.NotEqual(int.MaxValue, stored.Id);
        Assert.Equal(new UpsertCountryRequest("Thailand", "Asia", "764", "TH", "THA"),
            new UpsertCountryRequest(stored.Name, stored.Continent, stored.CountryCode, stored.Iso2, stored.Iso3));
        Assert.True(stored.ModifiedDate.HasValue && stored.ModifiedDate.Value.Year > 2000);
        if (method == "POST")
        {
            Assert.Empty(before);
            Assert.True(stored.Id > 0);
            Assert.True(stored.CreatedDate.HasValue && stored.CreatedDate.Value.Year > 2000);
            Assert.EndsWith($"/Countries/{stored.Id}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(id, stored.Id);
            Assert.Equal(Assert.Single(before).CreatedDate, stored.CreatedDate);
            Assert.NotEqual(Assert.Single(before).ModifiedDate, stored.ModifiedDate);
        }
        using var read = await client.GetAsync($"/Countries/{stored.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var wire = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(8, wire.RootElement.EnumerateObject().Count());
        Assert.Equal(stored.Id, wire.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(stored.CreatedDate, wire.RootElement.GetProperty("createdDate").GetDateTime());
        Assert.Equal(stored.ModifiedDate, wire.RootElement.GetProperty("modifiedDate").GetDateTime());
    }

    private async Task SeedAuditDatesAsync(int id)
    {
        await using var context = fixture.CreateContext();
        var row = await context.Countries.SingleAsync(country => country.Id == id);
        row.CreatedDate = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        row.ModifiedDate = new DateTime(2021, 2, 3, 0, 0, 0, DateTimeKind.Utc);
        row.Continent = "Original continent";
        row.CountryCode = "original-code";
        await context.SaveChangesAsync();
    }

    private async Task<CountryResponse[]> SnapshotAsync()
    {
        await using var context = fixture.CreateContext();
        return await context.Countries.AsNoTracking().OrderBy(country => country.Id)
            .Select(country => new CountryResponse(country.Id, country.Name, country.Continent,
                country.CountryCode, country.Iso2, country.Iso3, country.CreatedDate, country.ModifiedDate))
            .ToArrayAsync();
    }
}
