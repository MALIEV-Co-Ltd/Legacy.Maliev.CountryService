using Legacy.Maliev.CountryService.Data;
using Legacy.Maliev.CountryService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Legacy.Maliev.CountryService.Tests.Data;

public sealed class CountryModelCompatibilityTests
{
    [Fact]
    public void CountryMapping_PreservesLegacyTableAndColumnContract()
    {
        var options = new DbContextOptionsBuilder<CountryDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new CountryDbContext(options);

        var entity = context.Model.FindEntityType(typeof(Country));
        Assert.NotNull(entity);
        Assert.Equal("Country", entity.GetTableName());
        var table = StoreObjectIdentifier.Table("Country", null);
        Assert.Equal("ID", entity.FindProperty(nameof(Country.Id))!.GetColumnName(table));
        Assert.Equal(50, entity.FindProperty(nameof(Country.Name))!.GetMaxLength());
        Assert.Equal(50, entity.FindProperty(nameof(Country.Continent))!.GetMaxLength());
        Assert.Equal(30, entity.FindProperty(nameof(Country.CountryCode))!.GetMaxLength());
        Assert.Equal(2, entity.FindProperty(nameof(Country.Iso2))!.GetMaxLength());
        Assert.Equal(3, entity.FindProperty(nameof(Country.Iso3))!.GetMaxLength());
    }
}
