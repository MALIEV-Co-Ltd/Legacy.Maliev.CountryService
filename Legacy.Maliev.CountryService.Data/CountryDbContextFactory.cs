using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Legacy.Maliev.CountryService.Data;

/// <summary>Creates the context for explicit design-time migration commands.</summary>
public sealed class CountryDbContextFactory : IDesignTimeDbContextFactory<CountryDbContext>
{
    /// <inheritdoc />
    public CountryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CountryDbContext");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__CountryDbContext is required for design-time migration commands.");
        }

        var options = new DbContextOptionsBuilder<CountryDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CountryDbContext(options);
    }
}
