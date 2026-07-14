using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Models;
using Legacy.Maliev.CountryService.Application.Services;
using Legacy.Maliev.CountryService.Domain;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Legacy.Maliev.CountryService.Tests.Application;

public sealed class CountryApplicationServiceTests
{
    [Fact]
    public async Task GetAllAsync_CacheHit_DoesNotQueryDatabase()
    {
        var expected = new[] { new CountryResponse(1, "Japan", "Asia", "392", "JP", "JPN", null, null) };
        var repository = new Mock<ICountryRepository>(MockBehavior.Strict);
        var cache = new Mock<ICountryCache>(MockBehavior.Strict);
        cache.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var service = new CountryApplicationService(repository.Object, cache.Object, TimeProvider.System);

        var actual = await service.GetAllAsync(CancellationToken.None);

        Assert.Same(expected, actual);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_SetsUtcTimestampsAndInvalidatesCache()
    {
        var now = new DateTimeOffset(2026, 7, 14, 10, 30, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var request = new UpsertCountryRequest("Thailand", "Asia", "764", "TH", "THA");
        var repository = new Mock<ICountryRepository>(MockBehavior.Strict);
        repository.Setup(x => x.AddAsync(It.IsAny<Country>(), It.IsAny<CancellationToken>()))
            .Callback<Country, CancellationToken>((country, _) => country.Id = 7)
            .Returns(Task.CompletedTask);
        var cache = new Mock<ICountryCache>(MockBehavior.Strict);
        cache.Setup(x => x.InvalidateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new CountryApplicationService(repository.Object, cache.Object, clock);

        var created = await service.CreateAsync(request, CancellationToken.None);

        Assert.Equal(7, created.Id);
        Assert.Equal(now.UtcDateTime, created.CreatedDate);
        Assert.Equal(now.UtcDateTime, created.ModifiedDate);
        cache.VerifyAll();
    }
}
