# Legacy.Maliev.CountryService

[![PR validation](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/actions/workflows/pr-validation.yml/badge.svg)](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/actions/workflows/pr-validation.yml)
[![Main deployment](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/actions/workflows/ci-main.yml/badge.svg)](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/actions/workflows/ci-main.yml)

Temporary .NET 10 compatibility service extracted from `maliev-web`. It preserves
the legacy integer-key `Country` schema and `/Countries` JSON contract while the
new `Maliev.CountryService` is developed independently.

## Architecture

The service uses clean dependency direction: `Api` calls `Application`, domain rules live in
`Domain`, and PostgreSQL/Redis adapters live in `Data`. It depends only on the public
`Legacy.Maliev.ServiceDefaults` and `Legacy.Maliev.CompatibilityContracts` source repositories
during CI and image builds. Compatibility namespaces and all country route/DTO behavior remain
unchanged.

## API endpoints

| Purpose | Method | Route | Access |
| --- | --- | --- | --- |
| Legacy country list | `GET` | `/Countries` | Anonymous |
| Legacy country lookup | `GET` | `/Countries/{id}` | `legacy-country.countries.read` |
| Legacy country create | `POST` | `/Countries` | `legacy-country.countries.create` |
| Legacy country update | `PUT` | `/Countries/{id}` | `legacy-country.countries.update` |
| Legacy country delete | `DELETE` | `/Countries/{id}` | `legacy-country.countries.delete` |
| Versioned list | `GET` | `/country/v1/countries` | Anonymous |
| Scalar UI | `GET` | `/countries/scalar` | Anonymous |

## Runtime boundaries

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
