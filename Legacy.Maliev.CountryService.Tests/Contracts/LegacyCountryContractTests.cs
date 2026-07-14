using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.CountryService.Application.Models;

namespace Legacy.Maliev.CountryService.Tests.Contracts;

public sealed class LegacyCountryContractTests
{
    [Fact]
    public void CountryResponse_SerializesWithLegacyCamelCaseShape()
    {
        var response = new CountryResponse(
            7,
            "Thailand",
            "Asia",
            "764",
            "TH",
            "THA",
            new DateTime(2026, 7, 14, 1, 2, 3, DateTimeKind.Utc),
            null);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, options));
        var root = document.RootElement;

        Assert.Equal(7, root.GetProperty("id").GetInt32());
        Assert.Equal("Thailand", root.GetProperty("name").GetString());
        Assert.Equal("Asia", root.GetProperty("continent").GetString());
        Assert.Equal("764", root.GetProperty("countryCode").GetString());
        Assert.Equal("TH", root.GetProperty("iso2").GetString());
        Assert.Equal("THA", root.GetProperty("iso3").GetString());
        Assert.True(root.TryGetProperty("createdDate", out _));
        Assert.False(root.TryGetProperty("modifiedDate", out _));
        Assert.Equal(7, root.EnumerateObject().Count());
    }
}
