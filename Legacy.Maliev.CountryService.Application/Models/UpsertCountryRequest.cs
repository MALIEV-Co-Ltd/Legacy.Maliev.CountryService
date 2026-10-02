using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.CountryService.Application.Models;

/// <summary>Legacy-compatible country create and update payload.</summary>
/// <param name="Name">The country name.</param>
/// <param name="Continent">The optional continent.</param>
/// <param name="CountryCode">The optional country code.</param>
/// <param name="Iso2">The optional iso2 code.</param>
/// <param name="Iso3">The optional iso3 code.</param>
public sealed record UpsertCountryRequest(
    [Required, StringLength(50)] string Name,
    [StringLength(50)] string? Continent,
    [StringLength(30)] string? CountryCode,
    [StringLength(2)] string? Iso2,
    [StringLength(3)] string? Iso3);
