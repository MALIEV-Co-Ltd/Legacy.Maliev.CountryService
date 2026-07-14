using Legacy.Maliev.CountryService.Domain;

namespace Legacy.Maliev.CountryService.Application.Interfaces;

/// <summary>Persists legacy country records.</summary>
public interface ICountryRepository
{
    /// <summary>Returns all records without tracking.</summary>
    Task<IReadOnlyList<Country>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Returns one tracked record.</summary>
    Task<Country?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Adds and saves a record.</summary>
    Task AddAsync(Country country, CancellationToken cancellationToken);

    /// <summary>Saves changes to a record.</summary>
    Task UpdateAsync(Country country, CancellationToken cancellationToken);

    /// <summary>Deletes and saves a record.</summary>
    Task DeleteAsync(Country country, CancellationToken cancellationToken);
}
