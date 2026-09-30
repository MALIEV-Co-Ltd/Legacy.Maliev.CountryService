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
        // explicit and remove only the CLR Kind on writes: Npgsql requires Unspecified
        // for this column type. Preserve wall-clock ticks and imported read semantics.
        country.Property(entity => entity.CreatedDate)
            .HasConversion(value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : (DateTime?)null, value => value)
            .HasColumnType("timestamp without time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");
        country.Property(entity => entity.ModifiedDate)
            .HasConversion(value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : (DateTime?)null, value => value)
            .HasColumnType("timestamp without time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'");
        country.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();
    }
}
