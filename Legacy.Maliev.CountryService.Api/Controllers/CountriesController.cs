using Legacy.Maliev.CountryService.Api.Authorization;
using Legacy.Maliev.CountryService.Application.Interfaces;
using Legacy.Maliev.CountryService.Application.Exceptions;
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
    /// <param name="cancellationToken">Cancels the request's country lookup.</param>
    /// <returns>The country list, or not found when no countries exist.</returns>
    /// <response code="200">Countries ordered by name.</response>
    /// <response code="404">No countries exist.</response>
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
    /// <param name="id">The integer country identifier.</param>
    /// <param name="cancellationToken">Cancels the request's country lookup.</param>
    /// <returns>The matching country, or not found.</returns>
    /// <response code="200">The matching country.</response>
    /// <response code="401">A valid bearer credential is required.</response>
    /// <response code="403">The caller is not authorized to read the country.</response>
    /// <response code="404">The country does not exist.</response>
    [HttpGet("{id:int}", Name = "GetCountry")]
    [RequirePermission(CountryPermissions.CountriesRead)]
    [ProducesResponseType<CountryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CountryResponse>> GetCountryAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var country = await countryService.GetByIdAsync(id, cancellationToken);
        return country is null ? NotFound() : country;
    }

    /// <summary>Creates a country.</summary>
    /// <param name="request">The supported country fields; name is required and other fields may be omitted.</param>
    /// <param name="cancellationToken">Cancels the request's country creation.</param>
    /// <returns>The created country and a Location header identifying its read route.</returns>
    /// <response code="201">The created country with its server-owned integer identifier.</response>
    /// <response code="400">The input failed request validation.</response>
    /// <response code="401">A valid bearer credential is required.</response>
    /// <response code="403">The caller is not authorized to create countries.</response>
    [HttpPost]
    [RequirePermission(CountryPermissions.CountriesCreate)]
    [ProducesResponseType<CountryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> CreateCountryAsync(
        [FromBody] UpsertCountryRequest request,
        CancellationToken cancellationToken)
    {
        var created = await countryService.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetCountry", new { id = created.Id }, created);
    }

    /// <summary>Updates a country.</summary>
    /// <param name="id">The integer country identifier.</param>
    /// <param name="request">The supported replacement country fields.</param>
    /// <param name="cancellationToken">Cancels the request's country update.</param>
    /// <returns>No content on success, or an input, authorization, missing-country or concurrency error.</returns>
    /// <response code="204">The country was updated.</response>
    /// <response code="400">The identifier or input failed validation.</response>
    /// <response code="401">A valid bearer credential is required.</response>
    /// <response code="403">The caller is not authorized to update the country.</response>
    /// <response code="404">The country does not exist.</response>
    /// <response code="409">Plain-text explanation that a concurrent request changed the country.</response>
    [HttpPut("{id:int}")]
    [RequirePermission(CountryPermissions.CountriesUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<string>(StatusCodes.Status409Conflict, "text/plain")]
    public async Task<ActionResult> UpdateCountryAsync(
        int id,
        [FromBody] UpsertCountryRequest request,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        try
        {
            return await countryService.UpdateAsync(id, request, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (CountryConcurrencyException)
        {
            return Conflict("The country changed during this request.");
        }
    }

    /// <summary>Deletes a country.</summary>
    /// <param name="id">The integer country identifier.</param>
    /// <param name="cancellationToken">Cancels the request's country deletion.</param>
    /// <returns>No content on success, or an authorization, missing-country or concurrency error.</returns>
    /// <response code="204">The country was deleted.</response>
    /// <response code="401">A valid bearer credential is required.</response>
    /// <response code="403">Current live permission to delete the country was not granted.</response>
    /// <response code="404">The country does not exist.</response>
    /// <response code="409">Plain-text explanation that a concurrent request changed the country.</response>
    [HttpDelete("{id:int}")]
    [RequirePermission(CountryPermissions.CountriesDelete, RequireLiveCheck = true, IsCritical = true)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<string>(StatusCodes.Status409Conflict, "text/plain")]
    public async Task<ActionResult> DeleteCountryAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await countryService.DeleteAsync(id, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (CountryConcurrencyException)
        {
            return Conflict("The country changed during this request.");
        }
    }
}
