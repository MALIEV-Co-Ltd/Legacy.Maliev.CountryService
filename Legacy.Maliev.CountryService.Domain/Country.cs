namespace Legacy.Maliev.CountryService.Domain;

/// <summary>Represents the legacy country record without changing its wire shape.</summary>
public sealed class Country
{
    /// <summary>Gets or sets the legacy integer identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the country name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the continent.</summary>
    public string? Continent { get; set; }

    /// <summary>Gets or sets the legacy country code.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Gets or sets the ISO alpha-2 code.</summary>
    public string? Iso2 { get; set; }

    /// <summary>Gets or sets the ISO alpha-3 code.</summary>
    public string? Iso3 { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }

    /// <summary>Gets or sets the last modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }
}
