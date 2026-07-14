using Legacy.Maliev.CountryService.Api.Controllers;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.CountryService.Tests.Controllers;

public sealed class CountriesControllerCompatibilityTests
{
    [Fact]
    public async Task GetAllCountriesAsync_NoCountries_ReturnsLegacyNotFound()
    {
        var service = new Mock<ICountryService>(MockBehavior.Strict);
        service.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var controller = new CountriesController(service.Object);

        var result = await controller.GetAllCountriesAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAllCountriesAsync_CountriesExist_ReturnsNameSortedLegacyShape()
    {
        var countries = new[]
        {
            new CountryResponse(2, "Thailand", "Asia", "764", "TH", "THA", null, null),
            new CountryResponse(1, "Japan", "Asia", "392", "JP", "JPN", null, null),
        };
        var service = new Mock<ICountryService>(MockBehavior.Strict);
        service.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(countries);
        var controller = new CountriesController(service.Object);

        var result = await controller.GetAllCountriesAsync(CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal(["Japan", "Thailand"], result.Value.Select(x => x.Name));
    }

    [Fact]
    public async Task GetCountryAsync_MissingCountry_ReturnsNotFound()
    {
        var service = new Mock<ICountryService>(MockBehavior.Strict);
        service.Setup(x => x.GetByIdAsync(404, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CountryResponse?)null);
        var controller = new CountriesController(service.Object);

        var result = await controller.GetCountryAsync(404, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateCountryAsync_ValidRequest_ReturnsLegacyCreatedAtRoute()
    {
        var request = new UpsertCountryRequest("Thailand", "Asia", "764", "TH", "THA");
        var created = new CountryResponse(7, "Thailand", "Asia", "764", "TH", "THA", DateTime.UtcNow, DateTime.UtcNow);
        var service = new Mock<ICountryService>(MockBehavior.Strict);
        service.Setup(x => x.CreateAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        var controller = new CountriesController(service.Object);

        var result = await controller.CreateCountryAsync(request, CancellationToken.None);

        var route = Assert.IsType<CreatedAtRouteResult>(result);
        Assert.Equal("GetCountry", route.RouteName);
        Assert.Equal(7, route.RouteValues!["id"]);
        Assert.Same(created, route.Value);
    }

    [Fact]
    public async Task UpdateCountryAsync_MissingCountry_ReturnsNotFound()
    {
        var request = new UpsertCountryRequest("Missing", null, null, null, null);
        var service = new Mock<ICountryService>(MockBehavior.Strict);
        service.Setup(x => x.UpdateAsync(404, request, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var controller = new CountriesController(service.Object);

        var result = await controller.UpdateCountryAsync(404, request, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteCountryAsync_ExistingCountry_ReturnsNoContent()
    {
        var service = new Mock<ICountryService>(MockBehavior.Strict);
        service.Setup(x => x.DeleteAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var controller = new CountriesController(service.Object);

        var result = await controller.DeleteCountryAsync(7, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
