using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Models;
using Legacy.Maliev.CountryService.Domain;

namespace Legacy.Maliev.CountryService.Application.Services;

/// <summary>Implements legacy-compatible country behavior.</summary>
public sealed class CountryApplicationService(
    ICountryRepository repository,
    ICountryCache cache,
    TimeProvider timeProvider) : ICountryService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CountryResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.GetAllAsync(cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var countries = (await repository.GetAllAsync(cancellationToken))
            .OrderBy(country => country.Name, StringComparer.Ordinal)
            .Select(ToResponse)
            .ToArray();
        await cache.SetAllAsync(countries, cancellationToken);
        return countries;
    }

    /// <inheritdoc />
    public async Task<CountryResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var country = await repository.GetByIdAsync(id, cancellationToken);
        return country is null ? null : ToResponse(country);
    }

    /// <inheritdoc />
    public async Task<CountryResponse> CreateAsync(
        UpsertCountryRequest request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var country = new Country
        {
            Name = request.Name,
            Continent = request.Continent,
            CountryCode = request.CountryCode,
            Iso2 = request.Iso2,
            Iso3 = request.Iso3,
            CreatedDate = now,
            ModifiedDate = now,
        };
        await repository.AddAsync(country, cancellationToken);
        await cache.InvalidateAsync(cancellationToken);
        return ToResponse(country);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        int id,
        UpsertCountryRequest request,
        CancellationToken cancellationToken)
    {
        var country = await repository.GetByIdForUpdateAsync(id, cancellationToken);
        if (country is null)
        {
            return false;
        }

        country.Name = request.Name;
        country.Continent = request.Continent;
        country.CountryCode = request.CountryCode;
        country.Iso2 = request.Iso2;
        country.Iso3 = request.Iso3;
        country.ModifiedDate = timeProvider.GetUtcNow().UtcDateTime;
        await repository.UpdateAsync(country, cancellationToken);
        await cache.InvalidateAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var country = await repository.GetByIdForUpdateAsync(id, cancellationToken);
        if (country is null)
        {
            return false;
        }

        await repository.DeleteAsync(country, cancellationToken);
        await cache.InvalidateAsync(cancellationToken);
        return true;
    }

    private static CountryResponse ToResponse(Country country) => new(
        country.Id,
        country.Name,
        country.Continent,
        country.CountryCode,
        country.Iso2,
        country.Iso3,
        country.CreatedDate,
        country.ModifiedDate);
}
