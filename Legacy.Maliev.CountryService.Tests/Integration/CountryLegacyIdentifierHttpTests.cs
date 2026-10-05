using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.EntityFrameworkCore;

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
