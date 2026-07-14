using Legacy.Maliev.CountryService.Data;
using Legacy.Maliev.CountryService.Domain;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CountryService.Tests.Integration;

public sealed class CountryRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.1-bookworm").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task AddAndRead_RoundTripsLegacyCountryShape()
    {
        var options = new DbContextOptionsBuilder<CountryDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new CountryDbContext(options);
        await context.Database.MigrateAsync();
        var repository = new CountryRepository(context);
        var country = new Country
        {
            Name = "Thailand",
            Continent = "Asia",
            CountryCode = "764",
            Iso2 = "TH",
            Iso3 = "THA",
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow,
        };

        await repository.AddAsync(country, CancellationToken.None);
        var loaded = await repository.GetByIdAsync(country.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("Thailand", loaded.Name);
        Assert.Equal("TH", loaded.Iso2);
    }
}
