using System.Buffers;
using System.Globalization;
using System.Text;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Stj = System.Text.Json.JsonSerializer;
using StjException = System.Text.Json.JsonException;

namespace Legacy.Maliev.CountryService.Tests.Integration;

[Collection("Country lifecycle external configuration")]
public sealed class CountryRequestReaderTests(CountryLifecycleFixture fixture)
    : IClassFixture<CountryLifecycleFixture>
{
    public static IEnumerable<object[]> ScalarCases()
    {
        foreach (var field in new[] { "name", "continent", "countryCode", "iso2", "iso3" })
        {
            foreach (var (token, expected) in new[]
            {
                ("\"\"", ""), ("\"ไทย\"", "ไทย"), ("\"2026-10-17T00:00:00Z\"", "2026-10-17T00:00:00Z"),
                ("0", "0"), ("-12", "-12"), ("1.2300", "1.2300"), ("1e+02", "1e+02"),
                ("9007199254740993", "9007199254740993"), ("true", "true"), ("false", "false")
            })
                yield return [field, token, expected];
        }
    }

    [Theory]
    [MemberData(nameof(ScalarCases))]
    public void RegisteredReader_ConsumedScalarsMatchTypedSource(string field, string token, string expected)
    {
        var json = "{\"" + field + "\":" + token + "}";
        var source = Source(json);
        Assert.Equal(expected, Value(source, field));
        var candidate = Stj.Deserialize<UpsertCountryRequest>(json, CandidateOptions())!;
        Assert.Equal(expected, Value(candidate, field));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("fr-FR")]
    public void RegisteredReader_NumberLexemeDoesNotDependOnCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var json = "{\"name\":\"Thailand\",\"countryCode\":1.2300e+02}";
            Assert.Equal("1.2300e+02", Source(json).CountryCode);
            Assert.Equal("1.2300e+02", Stj.Deserialize<UpsertCountryRequest>(json, CandidateOptions())!.CountryCode);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("{\"countryCode\":\"764\",\"countryCode\":null}", "764")]
    [InlineData("{\"countryCode\":null,\"countryCode\":764}", "764")]
    [InlineData("{\"countryCode\":764,\"COUNTRYCODE\":392}", "392")]
    [InlineData("{\"countryCode\":\"764\",\"countryCode\":\"\"}", "")]
    [InlineData("{\"CountryCode\":true,\"countrycode\":false}", "false")]
    public void RegisteredReader_DuplicateOccurrencesRetainSourceNullIgnoreAndLastNonNull(string json, string expected)
    {
        Assert.Equal(expected, Source(json).CountryCode);
        Assert.Equal(expected, Stj.Deserialize<UpsertCountryRequest>(json, CandidateOptions())!.CountryCode);
    }

    [Fact]
    public void RegisteredReader_OmissionNullUnknownNestedAndEscapedNamesMatchSource()
    {
        const string json = "{\"name\":\"Thailand\",\"unknown\":{\"countryCode\":[{},true,null]},\"continent\":null,\"\\u0063ountryCode\":764,\"iſo2\":\"not-iso2\"}";
        var source = Source(json);
        var candidate = Stj.Deserialize<UpsertCountryRequest>(json, CandidateOptions())!;
        Assert.Equal(new UpsertCountryRequest("Thailand", null, "764", null, null), candidate);
        Assert.Equal(source.CountryCode, candidate.CountryCode);
        Assert.Null(source.Iso2);
        Assert.Null(Stj.Deserialize<UpsertCountryRequest>("{}", CandidateOptions())!.Name);
    }

    [Theory]
    [InlineData("{\"name\":{}}", "$.name")]
    [InlineData("{\"continent\":[]}", "$.continent")]
    [InlineData("{\"countryCode\":{\"private-marker\":1}}", "$.countryCode")]
    [InlineData("{\"iso2\":[\"TH\"]}", "$.iso2")]
    [InlineData("{\"iso3\":{}}", "$.iso3")]
    public void RegisteredReader_ConsumedContainersRejectWithoutEchoingTheirContents(string json, string expectedPath)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Source(json));
        var error = Assert.Throws<StjException>(() => Stj.Deserialize<UpsertCountryRequest>(json, CandidateOptions()));
        Assert.Equal(expectedPath, error.Path);
        Assert.DoesNotContain("private-marker", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"\"")]
    [InlineData("true")]
    [InlineData("{\"name\":\"Thailand\"} {}")]
    [InlineData("{\"name\":")]
    [InlineData("{\"unknown\":[}")]
    public void RegisteredReader_InvalidRootTrailingAndMalformedDataRemainRejected(string json)
    {
        Assert.Throws<StjException>(() => Stj.Deserialize<UpsertCountryRequest>(json, CandidateOptions()));
    }

    [Fact]
    public void SourceCharacterization_NativeStringReaderDoesNotAlreadyImplementCompatibility()
    {
        const string json = "{\"name\":\"Thailand\",\"countryCode\":764}";
        Assert.Equal("764", Source(json).CountryCode);
        Assert.Throws<StjException>(() => Stj.Deserialize<UpsertCountryRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void RegisteredReader_PreservesNullRootAndDoesNotChangeOtherTypesOrWrites()
    {
        var options = CandidateOptions();
        Assert.Null(Stj.Deserialize<UpsertCountryRequest>("null", options));
        Assert.Throws<StjException>(() => Stj.Deserialize<CountryResponse>("{\"name\":764}", options));
        Assert.Throws<StjException>(() => Stj.Deserialize<UnrelatedInput>("{\"value\":764}", options));
        Assert.Throws<StjException>(() => Stj.Deserialize<NativeOwnerId>("{\"id\":true}", options));
        var value = new UpsertCountryRequest("Thailand", null, "764", "TH", "THA");
        Assert.Equal(Stj.Serialize(value, NativeOptions()), Stj.Serialize(value, options));
    }

    [Fact]
    public void RegisteredReader_HasExactlyOneOwnedPublicConfigurationAndIndependentNativeMetadata()
    {
        using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        Assert.Single(factory.Services.GetServices<IConfigureOptions<JsonOptions>>(),
            CountryRequestNativeOptions.IsOwnedConfiguration);
        Assert.IsType<OptionsFactory<JsonOptions>>(factory.Services.GetRequiredService<IOptionsFactory<JsonOptions>>());
        var native = CountryRequestNativeOptions.Create(factory.Services);
        CountryRequestNativeOptions.AssertNativeScope(native);
        Assert.Throws<StjException>(() => Stj.Deserialize<UpsertCountryRequest>("{\"countryCode\":764}", native));
        Assert.Equal("764", Stj.Deserialize<UpsertCountryRequest>("{\"countryCode\":764}", CandidateOptions())!.CountryCode);
    }

    [Fact]
    public void NativeOracle_PreservesForeignNamedOrderPostConfigurationAndAllValidators()
    {
        using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        var owned = Assert.Single(factory.Services.GetServices<IConfigureOptions<JsonOptions>>(),
            CountryRequestNativeOptions.IsOwnedConfiguration);
        var events = new List<string>();
        var name = Microsoft.Extensions.Options.Options.DefaultName;
        IConfigureOptions<JsonOptions>[] setups =
        [
            new ConfigureNamedOptions<JsonOptions>(name, _ => events.Add("configure0")),
            owned,
            new ConfigureNamedOptions<JsonOptions>("other-name", _ => events.Add("wrong-name")),
            new ConfigureNamedOptions<JsonOptions>(null, _ => events.Add("configure1")),
        ];
        IPostConfigureOptions<JsonOptions>[] posts =
        [
            new PostConfigureOptions<JsonOptions>(name, options =>
            {
                events.Add("post0");
                options.AllowInputFormatterExceptionMessages = false;
            }),
            new PostConfigureOptions<JsonOptions>(name, options =>
            {
                Assert.False(options.AllowInputFormatterExceptionMessages);
                events.Add("post1");
            }),
        ];
        IValidateOptions<JsonOptions>[] validations =
        [
            new ValidateOptions<JsonOptions>(name, options =>
            {
                events.Add("validate0");
                return !options.AllowInputFormatterExceptionMessages;
            }, "postconfiguration must run"),
            new ValidateOptions<JsonOptions>(name, _ =>
            {
                events.Add("validate1");
                return true;
            }, "second validator must run"),
        ];
        var native = CountryRequestNativeOptions.Construct(setups, posts, validations);
        Assert.False(native.AllowInputFormatterExceptionMessages);
        CountryRequestNativeOptions.AssertNativeScope(native.JsonSerializerOptions);
        Assert.Equal(new[] { "configure0", "configure1", "post0", "post1", "validate0", "validate1" }, events);
        events.Clear();
        IValidateOptions<JsonOptions>[] failures =
        [
            new ValidateOptions<JsonOptions>(name, _ => { events.Add("fail0"); return false; }, "first failure"),
            new ValidateOptions<JsonOptions>(name, _ => { events.Add("fail1"); return false; }, "second failure"),
        ];
        var error = Assert.Throws<OptionsValidationException>(() =>
            CountryRequestNativeOptions.Construct([owned], [], failures));
        Assert.Equal(new[] { "first failure", "second failure" }, error.Failures);
        Assert.Equal(new[] { "fail0", "fail1" }, events);
    }

    [Fact]
    public void RegisteredReader_DoesNotAlterDeclaredRequestConstructorOrValidationAttributes()
    {
        var constructor = Assert.Single(typeof(UpsertCountryRequest).GetConstructors());
        Assert.Equal(new[] { "Name", "Continent", "CountryCode", "Iso2", "Iso3" },
            constructor.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(new[] { 50, 50, 30, 2, 3 }, constructor.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<System.ComponentModel.DataAnnotations.StringLengthAttribute>()!.MaximumLength));
        Assert.NotNull(constructor.GetParameters()[0].GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>());
    }


    [Theory]
    [InlineData("name")]
    [InlineData("continent")]
    [InlineData("countryCode")]
    [InlineData("iso2")]
    [InlineData("iso3")]
    public void RegisteredReader_EveryConsumedFieldIgnoresLaterNull(string field)
    {
        var json = "{\"" + field + "\":\"first\",\"" + field + "\":null}";
        Assert.Equal("first", Value(Source(json), field));
        Assert.Equal("first", Value(Stj.Deserialize<UpsertCountryRequest>(json, CandidateOptions())!, field));
    }

    [Fact]
    public void RegisteredReader_NumberCrossingInputSegmentsRetainsTheEntireLexeme()
    {
        var first = new InputSegment(Encoding.UTF8.GetBytes("{\"countryCode\":1.23"));
        var last = first.Append(Encoding.UTF8.GetBytes("00e+02}"));
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        var reader = new Utf8JsonReader(sequence, isFinalBlock: true, state: default);
        var value = Stj.Deserialize<UpsertCountryRequest>(ref reader, CandidateOptions())!;
        Assert.Equal("1.2300e+02", value.CountryCode);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("continent")]
    [InlineData("countryCode")]
    [InlineData("iso2")]
    [InlineData("iso3")]
    public void RegisteredReader_InvalidEarlierOccurrenceCannotBeHiddenByLaterValidValue(string field)
    {
        var json = "{\"" + field + "\":{},\"" + field + "\":\"valid\"}";
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Source(json));
        Assert.Throws<StjException>(() => Stj.Deserialize<UpsertCountryRequest>(json, CandidateOptions()));
    }

    internal JsonSerializerOptions CandidateOptions()
    {
        using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        return new JsonSerializerOptions(factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions);
    }

    private JsonSerializerOptions NativeOptions()
    {
        using var factory = fixture.CreateDocumentationFactory();
        using var client = factory.CreateClient();
        Assert.IsType<OptionsFactory<JsonOptions>>(factory.Services.GetRequiredService<IOptionsFactory<JsonOptions>>());
        var native = CountryRequestNativeOptions.Create(factory.Services);
        CountryRequestNativeOptions.AssertNativeScope(native);
        return native;
    }

    private static SourceCountry Source(string json) => JsonConvert.DeserializeObject<SourceCountry>(json,
        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })!;

    private static string? Value(object value, string field) => field switch
    {
        "name" => (string?)value.GetType().GetProperty("Name")!.GetValue(value),
        "continent" => (string?)value.GetType().GetProperty("Continent")!.GetValue(value),
        "countryCode" => (string?)value.GetType().GetProperty("CountryCode")!.GetValue(value),
        "iso2" => (string?)value.GetType().GetProperty("Iso2")!.GetValue(value),
        "iso3" => (string?)value.GetType().GetProperty("Iso3")!.GetValue(value),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private sealed class SourceCountry
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

    private sealed class InputSegment : ReadOnlySequenceSegment<byte>
    {
        public InputSegment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public InputSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new InputSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }

    private sealed record UnrelatedInput(string? Value);
    private sealed record NativeOwnerId(int Id);
}

internal static class CountryRequestNativeOptions
{
    private const string ConfigurationName = "Legacy.Maliev.CountryService.Api.Serialization.CountryRequestJsonOptions";

    internal static bool IsOwnedConfiguration(IConfigureOptions<JsonOptions> configuration)
        => configuration is ConfigureNamedOptions<JsonOptions> { Name: "", Action: { } action }
            && action.Target is null
            && action.GetInvocationList().Length == 1
            && action.Method.Name == "Configure"
            && action.Method.DeclaringType?.FullName == ConfigurationName
            && action.Method.DeclaringType?.Assembly == typeof(Program).Assembly;

    internal static JsonSerializerOptions Create(IServiceProvider services)
        => Construct(
            services.GetServices<IConfigureOptions<JsonOptions>>().ToArray(),
            services.GetServices<IPostConfigureOptions<JsonOptions>>().ToArray(),
            services.GetServices<IValidateOptions<JsonOptions>>().ToArray()).JsonSerializerOptions;

    internal static JsonOptions Construct(
        IConfigureOptions<JsonOptions>[] setups,
        IPostConfigureOptions<JsonOptions>[] posts,
        IValidateOptions<JsonOptions>[] validations)
    {
        Assert.Single(setups, IsOwnedConfiguration);
        var retained = setups.Where(configuration => !IsOwnedConfiguration(configuration)).ToArray();
        Assert.Equal(setups.Length - 1, retained.Length);
        var index = 0;
        foreach (var configuration in setups)
        {
            if (IsOwnedConfiguration(configuration)) continue;
            Assert.Same(configuration, retained[index++]);
        }
        Assert.Equal(retained.Length, index);
        // A fresh public OptionsFactory runs all other ordered Configure/PostConfigure/Validate
        // services. Candidate resolver metadata is never inspected, cloned or unwrapped.
        return new OptionsFactory<JsonOptions>(retained, posts, validations)
            .Create(Microsoft.Extensions.Options.Options.DefaultName);
    }

    internal static void RemoveOwnedConfiguration(IServiceCollection services)
    {
        var before = services.ToArray();
        var owned = Assert.Single(before, descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<JsonOptions>)
            && descriptor.ImplementationInstance is IConfigureOptions<JsonOptions> configuration
            && IsOwnedConfiguration(configuration));
        Assert.True(services.Remove(owned));
        var retained = before.Where(descriptor => !ReferenceEquals(descriptor, owned)).ToArray();
        Assert.Equal(retained.Length, services.Count);
        for (var index = 0; index < retained.Length; index++) Assert.Same(retained[index], services[index]);
    }

    internal static void AssertNativeScope(JsonSerializerOptions options)
    {
        options.MakeReadOnly();
        var metadata = options.GetTypeInfo(typeof(UpsertCountryRequest));
        Assert.Equal(JsonTypeInfoKind.Object, metadata.Kind);
        Assert.Null(metadata.CreateObject);
        Assert.All(metadata.Properties, property => Assert.Null(property.CustomConverter));
    }
}
