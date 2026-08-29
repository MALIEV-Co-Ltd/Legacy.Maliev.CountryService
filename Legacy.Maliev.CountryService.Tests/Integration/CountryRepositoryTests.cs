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
        // Legacy imported datetime values are stored as UTC wall-clock values in
        // PostgreSQL timestamp-without-time-zone columns. Npgsql requires an
        // unspecified DateTime kind at this boundary; the instant itself remains UTC.
        var utcNow = DateTime.UtcNow;
        var storedUtcWallClock = DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified);
        var country = new Country
        {
            Name = "Thailand",
            Continent = "Asia",
            CountryCode = "764",
            Iso2 = "TH",
            Iso3 = "THA",
            CreatedDate = storedUtcWallClock,
            ModifiedDate = storedUtcWallClock,
        };

        await repository.AddAsync(country, CancellationToken.None);
        context.ChangeTracker.Clear();
        var loaded = await repository.GetByIdAsync(country.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("Thailand", loaded.Name);
        Assert.Equal("TH", loaded.Iso2);
        Assert.Equal(DateTimeKind.Unspecified, loaded.CreatedDate!.Value.Kind);
        Assert.InRange(
            (storedUtcWallClock - loaded.CreatedDate.Value).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromTicks(9));
        Assert.Empty(context.ChangeTracker.Entries());

        var tracked = await repository.GetByIdForUpdateAsync(country.Id, CancellationToken.None);
        Assert.NotNull(tracked);
        tracked.Name = "Kingdom of Thailand";
        await repository.UpdateAsync(tracked, CancellationToken.None);
        context.ChangeTracker.Clear();

        Assert.Equal(
            "Kingdom of Thailand",
            await context.Countries.AsNoTracking()
                .Where(value => value.Id == country.Id)
                .Select(value => value.Name)
                .SingleAsync());
    }
}
