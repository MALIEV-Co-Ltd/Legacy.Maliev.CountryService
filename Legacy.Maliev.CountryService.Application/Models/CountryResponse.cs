namespace Legacy.Maliev.CountryService.Application.Models;

/// <summary>Legacy-compatible country response.</summary>
public sealed record CountryResponse(
    int Id,
    string Name,
    string? Continent,
    string? CountryCode,
    string? Iso2,
    string? Iso3,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);
