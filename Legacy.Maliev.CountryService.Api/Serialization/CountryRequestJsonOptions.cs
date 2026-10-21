using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CountryService.Api.Serialization;

/// <summary>Registers request-local readers without granting discarded inputs write authority.</summary>
public static class CountryRequestJsonOptions
{
    /// <summary>Preserves native MVC traversal and validation while adapting only the existing country request's input metadata.</summary>
    /// <param name="options">The existing MVC JSON options.</param>
    public static void Configure(JsonOptions options)
    {
        var serializer = options.JsonSerializerOptions;
        if (serializer.TypeInfoResolver is CountryRequestJsonResolver) return;
        serializer.TypeInfoResolver = new CountryRequestJsonResolver(
            serializer.TypeInfoResolver ?? throw new InvalidOperationException("MVC JSON metadata resolver is required."));
    }
}

internal sealed class CountryRequestJsonResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    private static readonly string[] Fields =
        [nameof(UpsertCountryRequest.Name), nameof(UpsertCountryRequest.Continent),
         nameof(UpsertCountryRequest.CountryCode), nameof(UpsertCountryRequest.Iso2), nameof(UpsertCountryRequest.Iso3)];
    private static readonly JsonConverter<string> TextReader = new CountryConsumedStringReader();

    // Allows the exact owned layer to be removed through public metadata APIs in native-oracle tests.
    public IJsonTypeInfoResolver Inner { get; } = inner;

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var metadata = Inner.GetTypeInfo(type, options);
        if (type != typeof(UpsertCountryRequest)) return metadata;
        if (metadata is null || metadata.Kind != JsonTypeInfoKind.Object)
            throw new InvalidOperationException("Native country request object metadata is required.");

        var names = new HashSet<string>(Fields.Select(name => options.PropertyNamingPolicy?.ConvertName(name) ?? name),
            StringComparer.Ordinal);
        if (names.Count != Fields.Length)
            throw new InvalidOperationException("Distinct consumed country property names are required.");
        // These are the original omitted constructor defaults. MVC still validates the unchanged record attributes.
        metadata.CreateObject = static () => new UpsertCountryRequest(null!, null, null, null, null);
        var configured = 0;
        foreach (var property in metadata.Properties)
        {
            if (!names.Contains(property.Name)) continue;
            if (property.PropertyType != typeof(string) || property.Set is not { } setter)
                throw new InvalidOperationException("Consumed country string setter is required.");
            property.CustomConverter = TextReader;
            property.Set = (target, value) =>
            {
                // Original Newtonsoft NullValueHandling.Ignore applies at each occurrence, not after aggregation.
                if (value is not null) setter(target, value);
            };
            configured++;
        }
        if (configured != Fields.Length)
            throw new InvalidOperationException("Exactly five consumed country properties are required.");
        AddDiscarded(metadata, options, "Id", new CountryDiscardedScalarReader(integer: true));
        AddDiscarded(metadata, options, "CreatedDate", new CountryDiscardedScalarReader(integer: false));
        AddDiscarded(metadata, options, "ModifiedDate", new CountryDiscardedScalarReader(integer: false));
        return metadata;
    }

    private static void AddDiscarded(JsonTypeInfo metadata, JsonSerializerOptions options,
        string name, JsonConverter converter)
    {
        var property = metadata.CreateJsonPropertyInfo(typeof(object),
            options.PropertyNamingPolicy?.ConvertName(name) ?? name);
        property.CustomConverter = converter;
        property.Set = static (_, _) => { };
        property.ShouldSerialize = static (_, _) => false;
        metadata.Properties.Add(property);
    }
}

internal sealed class CountryDiscardedScalarReader(bool integer) : JsonConverter<object>
{
    public override bool HandleNull => true;

    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Validate each occurrence independently. Never retain identity/audit inputs or replay the request body.
        string scalar;
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                scalar = "null";
                break;
            case JsonTokenType.String:
                scalar = JsonSerializer.Serialize(reader.GetString());
                break;
            case JsonTokenType.Number:
                scalar = reader.HasValueSequence
                    ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                    : Encoding.UTF8.GetString(reader.ValueSpan);
                break;
            default:
                throw new JsonException("Expected a compatible country identity or audit scalar.");
        }
        try
        {
            using var text = new StringReader(scalar);
            using var source = new Newtonsoft.Json.JsonTextReader(text)
            {
                Culture = CultureInfo.InvariantCulture,
            };
            if (integer) _ = source.ReadAsInt32();
            else _ = source.ReadAsDateTime();
        }
        catch (Exception error) when (error is Newtonsoft.Json.JsonException or ArgumentException or OverflowException)
        {
            // Source reader errors can contain private values; the native serializer attaches the real path.
            throw new JsonException("Expected a compatible country identity or audit scalar.");
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        => throw new NotSupportedException("Discarded country inputs have no output representation.");
}

internal sealed class CountryConsumedStringReader : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Number:
                var lexeme = reader.HasValueSequence
                    ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                    : Encoding.UTF8.GetString(reader.ValueSpan);
                if (double.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return lexeme;
                break;
        }
        // Let the native serializer attach the real path; do not buffer/replay the body or echo input values.
        throw new JsonException("Expected a compatible country string primitive.");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
