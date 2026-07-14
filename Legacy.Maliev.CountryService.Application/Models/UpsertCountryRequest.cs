using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.CountryService.Application.Models;

/// <summary>Legacy-compatible country create and update payload.</summary>
public sealed record UpsertCountryRequest(
    [property: Required, StringLength(50)] string Name,
    [property: StringLength(50)] string? Continent,
    [property: StringLength(30)] string? CountryCode,
    [property: StringLength(2)] string? Iso2,
    [property: StringLength(3)] string? Iso3);
