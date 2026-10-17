using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CountryService.Api.Serialization;

/// <summary>Registers source-compatible readers only for the five consumed country request strings.</summary>
public static class CountryRequestJsonOptions
{
    /// <summary>Preserves native MVC traversal and validation while adapting the existing request's string properties.</summary>
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
        return metadata;
    }
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
