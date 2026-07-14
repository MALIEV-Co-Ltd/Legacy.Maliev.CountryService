namespace Legacy.Maliev.CountryService.Api.Authorization;

/// <summary>Permissions for mutating temporary legacy country data.</summary>
public static class CountryPermissions
{
    /// <summary>Read one protected country record.</summary>
    public const string CountriesRead = "legacy-country.countries.read";

    /// <summary>Create country records.</summary>
    public const string CountriesCreate = "legacy-country.countries.create";

    /// <summary>Update country records.</summary>
    public const string CountriesUpdate = "legacy-country.countries.update";

    /// <summary>Delete country records.</summary>
    public const string CountriesDelete = "legacy-country.countries.delete";
}
