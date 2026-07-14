using Asp.Versioning;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CountryService.Api.Controllers;

/// <summary>Versioned facade for clients migrating away from the legacy route.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("country/v{version:apiVersion}/countries")]
public sealed class CountryV1Controller(ICountryService countryService) : ControllerBase
{
    /// <summary>Returns all countries ordered by name.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<CountryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CountryResponse>>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var countries = await countryService.GetAllAsync(cancellationToken);
        return Ok(countries);
    }
}
