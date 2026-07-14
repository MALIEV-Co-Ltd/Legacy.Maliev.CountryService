using Legacy.Maliev.CountryService.Application.Models;

namespace Legacy.Maliev.CountryService.Application.Interfaces;

/// <summary>Caches read-heavy country responses.</summary>
public interface ICountryCache
{
    /// <summary>Gets the complete country list.</summary>
    Task<IReadOnlyList<CountryResponse>?> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Stores the complete country list.</summary>
    Task SetAllAsync(IReadOnlyList<CountryResponse> countries, CancellationToken cancellationToken);

    /// <summary>Invalidates all country entries.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}
