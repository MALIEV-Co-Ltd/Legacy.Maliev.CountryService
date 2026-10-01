namespace Legacy.Maliev.CountryService.Application.Exceptions;

/// <summary>Signals a stale Country mutation without exposing persistence details.</summary>
public sealed class CountryConcurrencyException(Exception innerException)
    : Exception("The country changed during this request.", innerException);
