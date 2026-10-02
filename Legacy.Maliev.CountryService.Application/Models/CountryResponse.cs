namespace Legacy.Maliev.CountryService.Application.Models;

/// <summary>Legacy-compatible country response.</summary>
/// <param name="Id">The server-assigned country identifier.</param>
/// <param name="Name">The country name.</param>
/// <param name="Continent">The optional continent.</param>
/// <param name="CountryCode">The optional country code.</param>
/// <param name="Iso2">The optional iso2 code.</param>
/// <param name="Iso3">The optional iso3 code.</param>
/// <param name="CreatedDate">The optional created date.</param>
/// <param name="ModifiedDate">The optional modified date.</param>
public sealed record CountryResponse(
    int Id,
    string Name,
    string? Continent,
    string? CountryCode,
    string? Iso2,
    string? Iso3,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);
