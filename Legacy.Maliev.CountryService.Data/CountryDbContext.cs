using Legacy.Maliev.CountryService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CountryService.Data;

/// <summary>PostgreSQL context preserving the legacy Country schema contract.</summary>
public sealed class CountryDbContext(DbContextOptions<CountryDbContext> options) : DbContext(options)
{
    /// <summary>Gets the country records.</summary>
    public DbSet<Country> Countries => Set<Country>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var country = modelBuilder.Entity<Country>();
        country.ToTable("Country");
        country.HasKey(entity => entity.Id);
        country.Property(entity => entity.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        country.Property(entity => entity.Name).HasMaxLength(50).IsRequired();
        country.Property(entity => entity.Continent).HasMaxLength(50);
        country.Property(entity => entity.CountryCode).HasMaxLength(30);
        country.Property(entity => entity.Iso2).HasColumnName("ISO2").HasMaxLength(2);
        country.Property(entity => entity.Iso3).HasColumnName("ISO3").HasMaxLength(3);
        // Legacy datetime values were imported into the PostgreSQL schema as
        // UTC wall-clock values (`timestamp without time zone`). Keep the provider mapping
        // explicit so Npgsql does not try to write UTC DateTime values to a timestamptz
        // column, and keep new defaults in the same UTC representation.
        country.Property(entity => entity.CreatedDate)
            .HasColumnType("timestamp without time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");
        country.Property(entity => entity.ModifiedDate)
            .HasColumnType("timestamp without time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");
        country.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();
    }
}
