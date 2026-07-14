using Legacy.Maliev.CountryService.Application.Models;
using Legacy.Maliev.CountryService.Data;
using Microsoft.Extensions.Caching.Distributed;

namespace Legacy.Maliev.CountryService.Tests.Caching;

public sealed class DistributedCountryCacheTests
{
    [Fact]
    public async Task SetGetInvalidate_RoundTripsLegacyResponse()
    {
        var store = new TestDistributedCache();
        var cache = new DistributedCountryCache(store);
        var expected = new[]
        {
            new CountryResponse(1, "Japan", "Asia", "392", "JP", "JPN", null, null),
        };

        await cache.SetAllAsync(expected, CancellationToken.None);
        var actual = await cache.GetAllAsync(CancellationToken.None);
        await cache.InvalidateAsync(CancellationToken.None);
        var removed = await cache.GetAllAsync(CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal("Japan", Assert.Single(actual).Name);
        Assert.Null(removed);
    }

    private sealed class TestDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = [];

        public byte[]? Get(string key) => _values.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _values.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            _values[key] = value;

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
