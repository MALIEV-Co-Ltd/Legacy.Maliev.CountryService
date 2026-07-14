using Legacy.Maliev.CountryService.Application.Models;

namespace Legacy.Maliev.CountryService.Application.Interfaces;

/// <summary>Provides legacy country operations.</summary>
public interface ICountryService
{
    /// <summary>Returns all countries.</summary>
    Task<IReadOnlyList<CountryResponse>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Returns a country by legacy identifier.</summary>
    Task<CountryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Creates a country.</summary>
    Task<CountryResponse> CreateAsync(UpsertCountryRequest request, CancellationToken cancellationToken);

    /// <summary>Updates a country when it exists.</summary>
    Task<bool> UpdateAsync(int id, UpsertCountryRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a country when it exists.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
