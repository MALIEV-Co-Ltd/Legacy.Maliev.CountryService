# Legacy.Maliev.CountryService

Temporary .NET 10 compatibility service extracted from `maliev-web`. It preserves
the legacy integer-key `Country` schema and `/Countries` JSON contract while the
new `Maliev.CountryService` is developed independently.

## Boundaries

- Legacy route: `/Countries`
- Versioned route: `/country/v1/countries`
- Scalar: `/countries/scalar`
- PostgreSQL database: `Country` on `legacy-postgres-main`
- Redis key prefix: `legacy:country:`
- Public country listing remains anonymous; protected operations require granular
  `legacy-country.countries.*` permissions.

This service does not modify the SQL Server source. PostgreSQL promotion requires
the artifact-backed parity and cutover gates tracked in `MALIEV-Co-Ltd/maliev-web`.

## Validate

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format Legacy.Maliev.CountryService.slnx --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
```
