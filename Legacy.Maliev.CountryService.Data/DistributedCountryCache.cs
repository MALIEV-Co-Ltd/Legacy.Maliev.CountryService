using System.Text.Json;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.CountryService.Data;

/// <summary>Redis-backed cache for the stable legacy country list.</summary>
public sealed class DistributedCountryCache(
    IDistributedCache distributedCache,
    ILogger<DistributedCountryCache>? logger = null) : ICountryCache
{
    private const string AllCountriesKey = "legacy:country:all:v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CountryResponse>?> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await distributedCache.GetAsync(AllCountriesKey, cancellationToken);
            return bytes is null
                ? null
                : JsonSerializer.Deserialize<CountryResponse[]>(bytes, JsonOptions);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Country cache read failed; falling back to PostgreSQL");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAllAsync(
        IReadOnlyList<CountryResponse> countries,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(countries, JsonOptions);
            await distributedCache.SetAsync(
                AllCountriesKey,
                bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) },
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Country cache write failed; continuing without cache");
        }
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await distributedCache.RemoveAsync(AllCountriesKey, cancellationToken);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Country cache invalidation failed");
        }
    }
}
