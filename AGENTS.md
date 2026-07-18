# Legacy.Maliev.CountryService — Agent Coding Guide

> This repo (`Legacy.Maliev.CountryService`) is an independent git repo inside the `B:\maliev` workspace. All commands run from this directory.

---

## Build, Test & Lint Commands

```powershell
# Build (treats warnings as errors — all must be fixed)
dotnet build Legacy.Maliev.CountryService.slnx

# Run all tests
dotnet test Legacy.Maliev.CountryService.slnx --verbosity normal

# Run a single test method
dotnet test --filter "FullyQualifiedName~CountriesControllerTests.GetById_ReturnsOk"

# Run all tests in a class
dotnet test --filter "FullyQualifiedName~CountriesControllerTests"

# Run with code coverage
dotnet test Legacy.Maliev.CountryService.slnx --collect:"XPlat Code Coverage"

# Format check
dotnet format Legacy.Maliev.CountryService.slnx

# EF Core migrations (Infrastructure project only)
dotnet ef migrations add <Name> --project Legacy.Maliev.CountryService.Infrastructure --startup-project Legacy.Maliev.CountryService.Infrastructure
```

---

## Code Style & Conventions

### Workspace Structure
```
Legacy.Maliev.CountryService/
├── Legacy.Maliev.CountryService.Api/           # Controllers, Consumers, Middleware
├── Legacy.Maliev.CountryService.Application/   # Use cases, DTOs, Interfaces, Handlers
├── Legacy.Maliev.CountryService.Domain/        # Entities, value objects, domain interfaces
├── Legacy.Maliev.CountryService.Infrastructure/ # EF Core DbContext, repositories, HTTP clients
├── Legacy.Maliev.CountryService.Tests/         # Unit + Integration tests (xUnit)
├── Directory.Build.props                # Central package versioning
└── Legacy.Maliev.CountryService.slnx          # Solution file (.slnx preferred over .sln)
```

### C# Naming & Formatting
- **Namespaces**: File-scoped (`namespace Legacy.Maliev.CountryService.Domain.Entities;`)
- **Classes/Methods/Properties**: `PascalCase`
- **Private fields**: `_camelCase` (underscore prefix)
- **Parameters/locals**: `camelCase`
- **Async methods**: Suffix with `Async` (e.g., `GetByIdAsync`)
- **Interfaces**: Prefix with `I` (e.g., `ICountryService`)
- **Permissions**: GCP-style `{domain}.{plural-resource}.{action}` as `public const string` in a `Permissions` static class
  - Valid: `country.countries.create`, `country.countries.read`
  - Invalid: `country.country.create` (singular), `countries.create` (missing domain)
- **XML docs**: Required on ALL public methods and properties
- **Nullable**: Enabled (`<Nullable>enable</Nullable>`). Use `?` explicitly
- **Imports**: System first, then third-party, then local. Alphabetize within groups. Remove unused `using`
- **Braces**: Allman style (new line) for methods and control structures. Expression-bodied for properties/accessors
- **Indentation**: 4 spaces, LF line endings, UTF-8, trim trailing whitespace

### C# Patterns
- **DI**: Constructor injection with `private readonly` fields
- **Controllers**: `[ApiController]`, `[ApiVersion("1")]`, `[Route("country/v{version:apiVersion}/[controller]")]`
- **Logging**: `ILogger<T>` with structured placeholders (never interpolate): `_logger.LogInformation("Processing {CountryId}", countryId)`
- **Error handling**: Global exception middleware. Return `ProblemDetails` / `ErrorResponse` DTOs. Never expose stack traces
- **JSON**: Check existing conventions in this service for naming policy
- **Manual mapping**: Static extension methods (`ToDto()`, `ToEntity()`). AutoMapper is banned
- **Validation**: `System.ComponentModel.DataAnnotations` on DTOs. FluentValidation is banned
- **Observability**: Uses `Legacy.Maliev.ServiceDefaults` (compatibility namespace retained) for OpenTelemetry, logging, and health checks
- **LoggerMessage**: Prefer `[LoggerMessage]` source generator for high-performance logging

---

## Banned Libraries (Build Will Fail)

| Banned | Use Instead |
|--------|-------------|
| AutoMapper | Manual mapping extensions |
| FluentValidation | DataAnnotations or manual validation |
| FluentAssertions | Standard xUnit `Assert.*` |
| Swashbuckle/Swagger | Scalar (at `/country/scalar`) |
| InMemoryDatabase (EF Core) | Testcontainers with real PostgreSQL |

---

## Testing Rules

- **Framework**: xUnit with standard `Assert` (`Assert.Equal`, `Assert.NotNull`, etc.)
- **Naming**: `MethodName_StateUnderTest_ExpectedBehavior` or `HTTP_METHOD_Path_Scenario_ExpectedStatus`
- **Coverage**: Minimum 80% per service
- **Integration tests**: `BaseIntegrationTestFactory<TProgram, TDbContext>` with Testcontainers (PostgreSQL, Redis, RabbitMQ). Never InMemoryDatabase
- **System tests** (Tier 3): `AspireTestFixture` with `[Collection("AspireDomainTests")]` — shared AppHost, never one per class
- **Eventual consistency**: Use `TestHelpers.WaitForAsync`. Never `Task.Delay`
- **MassTransit consumers**: Must have consumer tests using `AddMassTransitTestHarness()`

### CountryService Test Infrastructure

- **Integration Tests**: Inherit from `IntegrationTestBase`
- **Collections**:
  - Use `[Collection("TestDatabase")]` for tests sharing the DB container
  - Use `[Collection("ResilienceTests")]` for tests needing DB restart/manipulation
- **Database Cleanup**: `CleanDatabaseAsync()` is called automatically in `IntegrationTestBase.InitializeAsync()`. Ensure your test class inherits correctly
- **Factory**: Use `TestWebApplicationFactory` to create the test client

### Testing Strategy (4-Tier Pyramid Context)

This service's tests cover **Tier 1 (Unit)** and **Tier 2 (Service Integration)** of the Maliev testing pyramid:

| Tier | What to Test | Infrastructure |
|------|-------------|---------------|
| **Unit** | Business logic, domain models, service methods with mocked dependencies | None (mocks only) |
| **Service Integration** | API endpoints, database persistence, permission enforcement, input validation | `BaseIntegrationTestFactory` + Testcontainers (Postgres/Redis/RabbitMQ) |

**Tier 3 (System Integration)** — cross-service workflows and event chains — is tested in `Maliev.Aspire.Tests/`.

> Full ecosystem test strategy: `Maliev.Aspire.Tests/TEST_PLAN.md`

### Critical Test Rules (Country-Specific)
1. **Required Fields**: `CreateCountryRequest` MUST include:
   - `Timezones`, `Borders`, `CallingCodes`, `TopLevelDomains`
   - `Currencies`, `Languages`, `Translations`, `Flags`
2. **ISO Validation**:
   - Iso2: `^[A-Z]{2}$` (2 uppercase letters)
   - Iso3: `^[A-Z]{3}$` (3 uppercase letters)
3. **Resilience**: Query by ISO code instead of ID after DB restarts in resilience tests (IDs may change or be unreliable across resets/seeds, though UUIDs typically persist)

---

## Mandatory Rules

- **`TreatWarningsAsErrors = true`**: Zero warnings allowed. No suppression
- **`[RequirePermission("country.countries.action")]`**: On all endpoints, not plain `[Authorize]`
- **API versioning**: All routes versioned (`v1/`)
- **Service prefix**: Routes prefixed with `/country`
- **Scalar docs**: Configured at `/country/scalar`
- **Secrets**: Never hardcoded. Use GCP Secret Manager or environment variables
- **Async/await**: All the way down. Pass `CancellationToken`
- **EF Core Design package**: Only in Infrastructure project, never in Api
- **PostgreSQL xmin**: Shadow property only — `entity.Property<uint>("xmin").HasColumnType("xid").IsRowVersion()`. Never add entity property
  - Never use `UseXminAsConcurrencyToken()` (removed in Npgsql EF v7)
  - Never add entity property `public uint Xmin { get; set; }` or `public uint xmin { get; set; }`
  - Never use `.Ignore(e => e.Xmin)` — remove the entity property instead
- **Temporary files**: Generate in `/temp` folder, clean up afterwards

---

## Git Rules

- Each `Maliev.*` folder is an independent git repo. `cd` into it before git commands
- **Commit early and often** after every meaningful unit of work. Do not accumulate changes
- **Never use `git checkout` to restore files** — commit first, then `git revert` or `git reset --soft`
- Feature branches merged to `develop` via PR. Do not push without being asked
