using System.Reflection;
using Legacy.Maliev.CountryService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Legacy.Maliev.CountryService.Tests.Data;

public sealed class CountryPostgresConcurrencyMigrationTests
{
    [Fact]
    public void CountryConcurrencyUsesPostgreSqlSystemXminWithoutCreatingAUserColumn()
    {
        var options = new DbContextOptionsBuilder<CountryDbContext>()
            .UseNpgsql("Host=localhost;Database=country;Username=test;Password=test")
            .Options;

        using var context = new CountryDbContext(options);
        var entityType = context.Model.GetEntityTypes()
            .Single(entity => entity.FindProperty("xmin") is not null);
        var property = entityType.FindProperty("xmin");

        Assert.NotNull(property);
        Assert.Equal("xmin", property!.GetColumnName());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.True(property.IsConcurrencyToken);

        var migration = Path.Combine(
            Path.GetDirectoryName(typeof(CountryPostgresConcurrencyMigrationTests).Assembly.Location)!,
            "..", "..", "..", "..", "Legacy.Maliev.CountryService.Data", "Migrations",
            "20260714120451_InitialPostgresCompatibility.cs");
        var migrationSource = File.ReadAllText(Path.GetFullPath(migration));

        Assert.DoesNotContain("xmin = table.Column", migrationSource, StringComparison.Ordinal);
    }
}
