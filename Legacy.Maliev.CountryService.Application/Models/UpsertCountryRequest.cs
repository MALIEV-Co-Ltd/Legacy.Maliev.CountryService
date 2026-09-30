using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.CountryService.Application.Models;

/// <summary>Legacy-compatible country create and update payload.</summary>
public sealed record UpsertCountryRequest(
    [Required, StringLength(50)] string Name,
    [StringLength(50)] string? Continent,
    [StringLength(30)] string? CountryCode,
    [StringLength(2)] string? Iso2,
    [StringLength(3)] string? Iso3);
