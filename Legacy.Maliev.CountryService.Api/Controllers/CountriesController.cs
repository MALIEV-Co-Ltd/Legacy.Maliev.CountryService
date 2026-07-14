using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CountryService.Api.Controllers;

/// <summary>Preserves the legacy country HTTP contract during migration.</summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public sealed class CountriesController(ICountryService countryService) : ControllerBase
{
    /// <summary>Returns countries ordered by name.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<CountryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CountryResponse>>> GetAllCountriesAsync(
        CancellationToken cancellationToken)
    {
        var countries = await countryService.GetAllAsync(cancellationToken);
        if (countries.Count == 0)
        {
            return NotFound();
        }

        return countries.OrderBy(country => country.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Returns one country by legacy identifier.</summary>
    [HttpGet("{id:int}", Name = "GetCountry")]
    [RequirePermission(CountryPermissions.CountriesRead)]
    public async Task<ActionResult<CountryResponse>> GetCountryAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var country = await countryService.GetByIdAsync(id, cancellationToken);
        return country is null ? NotFound() : country;
    }

    /// <summary>Creates a country.</summary>
    [HttpPost]
    [RequirePermission(CountryPermissions.CountriesCreate)]
    public async Task<ActionResult> CreateCountryAsync(
        [FromBody] UpsertCountryRequest request,
        CancellationToken cancellationToken)
    {
        var created = await countryService.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetCountry", new { id = created.Id }, created);
    }

    /// <summary>Updates a country.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(CountryPermissions.CountriesUpdate)]
    public async Task<ActionResult> UpdateCountryAsync(
        int id,
        [FromBody] UpsertCountryRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        return await countryService.UpdateAsync(id, request, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    /// <summary>Deletes a country.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(CountryPermissions.CountriesDelete)]
    public async Task<ActionResult> DeleteCountryAsync(int id, CancellationToken cancellationToken)
    {
        return await countryService.DeleteAsync(id, cancellationToken)
            ? NoContent()
            : NotFound();
    }
}
