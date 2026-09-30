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
    public async Task GetAllAsync_StaleCacheCannotOverrideAuthoritativeRepository()
    {
        var expected = new[] { new CountryResponse(1, "Japan", "Asia", "392", "JP", "JPN", null, null) };
        var repository = new Mock<ICountryRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new Country { Id = 2, Name = "Thailand" } });
        var cache = new Mock<ICountryCache>(MockBehavior.Strict);
        cache.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var service = new CountryApplicationService(repository.Object, cache.Object, TimeProvider.System);

        var actual = await service.GetAllAsync(CancellationToken.None);

        Assert.Equal("Thailand", Assert.Single(actual).Name);
        repository.Verify(x => x.GetAllAsync(CancellationToken.None), Times.Once);
        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAllAsync_RepositoryFailurePropagatesWithoutCacheFallback()
    {
        var repository = new Mock<ICountryRepository>(MockBehavior.Strict);
        var failure = new InvalidOperationException("controlled repository failure");
        repository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        var cache = new Mock<ICountryCache>(MockBehavior.Strict);
        var service = new CountryApplicationService(repository.Object, cache.Object, TimeProvider.System);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAllAsync(CancellationToken.None)));
        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAllAsync_CallerCancellationReachesRepositoryWithoutCacheWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new Mock<ICountryRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetAllAsync(cancellation.Token)).ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var cache = new Mock<ICountryCache>(MockBehavior.Strict);
        var service = new CountryApplicationService(repository.Object, cache.Object, TimeProvider.System);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetAllAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        repository.Verify(x => x.GetAllAsync(cancellation.Token), Times.Once);
        cache.VerifyNoOtherCalls();
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
