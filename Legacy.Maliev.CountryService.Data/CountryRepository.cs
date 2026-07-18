using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CountryService.Data;

/// <summary>EF Core implementation of legacy country persistence.</summary>
public sealed class CountryRepository(CountryDbContext dbContext) : ICountryRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Country>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.Countries.AsNoTracking().OrderBy(country => country.Name)
            .ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Country?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Countries.AsNoTracking()
            .SingleOrDefaultAsync(country => country.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Country?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Countries.SingleOrDefaultAsync(country => country.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Country country, CancellationToken cancellationToken)
    {
        await dbContext.Countries.AddAsync(country, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Country country, CancellationToken cancellationToken)
    {
        dbContext.Countries.Update(country);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Country country, CancellationToken cancellationToken)
    {
        dbContext.Countries.Remove(country);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
