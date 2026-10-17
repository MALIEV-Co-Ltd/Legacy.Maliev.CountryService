using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Stj = System.Text.Json.JsonSerializer;

namespace Legacy.Maliev.CountryService.Tests.Integration;

[Collection("Country lifecycle external configuration")]
public sealed class CountryDiscardedInputReaderTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>
{
    public static IEnumerable<object[]> TypedCases()
    {
        foreach (var token in new[] { "null", "0", "-1", "2147483647", "-2147483648", "2147483648",
            "-2147483649", "1.0", "1e0", "true", "false", "\"\"", "\"  +12  \"", "\"2147483648\"",
            "\"private-marker\"", "{}", "[]", "\"12.0\"", "\"0x10\"" })
            yield return ["id", token];
        foreach (var field in new[] { "createdDate", "modifiedDate" })
            foreach (var token in new[] { "null", "\"\"", "\"2026-10-17T00:00:00Z\"",
                "\"2026-10-17T07:30:00+07:00\"", "\"2026-10-17\"", "\"10/17/2026\"",
                "\"0001-01-01T00:00:00Z\"", "\"9999-12-31T23:59:59.9999999Z\"",
                "\"2026-10-17T24:00:00Z\"", "\"/Date(0)/\"", "\"/Date(0+0700)/\"",
                "\"/Date(-62135596800000)/\"", "\"/Date(253402300800000)/\"",
                "\"2026-02-30\"", "\"17/10/2026\"", "\"private-marker\"", "\" \"",
                "0", "1.0", "true", "false", "{}", "[]" })
                yield return [field, token];
    }

    [Theory]
    [MemberData(nameof(TypedCases))]
    public void DiscardedProperty_EveryScalarMatchesPinnedTypedPublicReader(string field, string token)
    {
        using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        AssertSameBinding("{\"name\":\"Thailand\",\"" + field + "\":" + token + "}", options);
    }

    [Theory]
    [MemberData(nameof(TypedCases))]
    public void DiscardedProperty_EveryOccurrenceIsValidatedBeforeDiscard(string field, string token)
    {
        using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var valid = field == "id" ? "12" : "\"2026-10-17T00:00:00Z\"";
        foreach (var suffix in new[] { valid, "null" })
        {
            AssertSameBinding("{\"name\":\"Thailand\",\"" + field + "\":" + token
                + ",\"" + field.ToUpperInvariant() + "\":" + suffix + "}", options);
            AssertSameBinding("{\"name\":\"Thailand\",\"" + field + "\":" + suffix
                + ",\"" + field.ToUpperInvariant() + "\":" + token + "}", options);
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("fr-FR")]
    public void DiscardedProperty_InvariantDatesAndIntegersDoNotFollowCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var factory = fixture.CreateDocumentationFactory();
            using var client = factory.CreateClient();
            var options = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
            foreach (var item in TypedCases())
                AssertSameBinding("{\"name\":\"Thailand\",\"" + item[0] + "\":" + item[1] + "}", options);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void DiscardedProperties_KeepFiveStringDtoAndNativeWriter()
    {
        using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        var candidate = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var native = CountryRequestNativeOptions.Create(factory.Services);
        CountryRequestNativeOptions.AssertNativeScope(native);
        const string json = "{\"name\":\"Thailand\",\"id\":{},\"createdDate\":[],\"modifiedDate\":true}";
        Assert.Equal(new UpsertCountryRequest("Thailand", null, null, null, null),
            Stj.Deserialize<UpsertCountryRequest>(json, native));
        Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertCountryRequest>(json, candidate));
        var value = new UpsertCountryRequest("Thailand", null, "764", "TH", "THA");
        Assert.Equal(Stj.Serialize(value, native), Stj.Serialize(value, candidate));
        Assert.Equal(5, typeof(UpsertCountryRequest).GetProperties().Length);
    }

    private static void AssertSameBinding(string json, JsonSerializerOptions options)
    {
        bool sourceAccepted;
        try
        {
            _ = JsonConvert.DeserializeObject<SourceInput>(json,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            sourceAccepted = true;
        }
        catch (Exception error) when (error is Newtonsoft.Json.JsonException or ArgumentException or OverflowException)
        {
            sourceAccepted = false;
        }
        if (sourceAccepted)
            Assert.Equal(new UpsertCountryRequest("Thailand", null, null, null, null),
                Stj.Deserialize<UpsertCountryRequest>(json, options));
        else
        {
            var error = Assert.Throws<System.Text.Json.JsonException>(() =>
                Stj.Deserialize<UpsertCountryRequest>(json, options));
            Assert.DoesNotContain("private-marker", error.Message, StringComparison.Ordinal);
        }
    }

    // Source-shape oracle against the already-pinned public reader, not a historical MVC binary.
    private sealed class SourceInput
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Continent { get; set; }
        public string? CountryCode { get; set; }
        public string? Iso2 { get; set; }
        public string? Iso3 { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
