# Country lifecycle acceptance — 2026-09-30

Issue: [Country #32](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/issues/32).
Base: `c1e6222e9b7de6431b0e5115e4e43419e12e1323`, required main CI `36682800993` passed before writes.

## Real boundaries and repairs

The new acceptance fixture runs the actual Program in Production with real RS256 authentication,
permission middleware, DI, application service, repository, migrations and disposable PostgreSQL
18.1. Only the external distributed-cache backend is independently controlled for faults. It does
not substitute the controller, authentication handler, service, repository or context. One
container is shared by the class; each case resets only its own disposable Country table/cache.
The external-configuration case restores its environment variable in finally and the collection
does not execute concurrently with other test collections.

Actual authenticated POST first reproduced MVC's rejection of property-target validation metadata
on the positional request record. Removing only the attribute targets exposes the unchanged
Required/StringLength rules to MVC's constructor-parameter validation. No field, wire name,
nullability or ISO policy is added. Length-limit values and lowercase ISO values remain accepted.

After that repair, the same authenticated POST reproduced Npgsql's rejection of UTC DateTime
values for timestamp-without-time-zone columns. Conversions on CreatedDate and ModifiedDate
strip only Kind when writing; they preserve wall-clock ticks (subject to PostgreSQL microsecond
precision), nullable values and imported Unspecified reads. The service's UTC clock, legacy
columns/defaults, xmin and schema remain unchanged. No migration or global Npgsql switch is used.

## Acceptance matrix

- Anonymous legacy collection: empty 404; populated sorted camel-case legacy eight-field response.
  Versioned collection: empty 200 array; same populated values. Versioned POST remains 405.
- Actual RS256 protected create: valid 201/Location; absent/malformed/expired/wrong issuer,
  audience, signature or algorithm 401 without database changes. Missing, unrelated and employee
  wildcard permission 403. Detail/update/delete enforce their exact existing legacy-country grants.
- Five-field HTTP validation: required/blank name and every existing length limit fail 400 before
  persistence or cache invalidation. Boundary lengths succeed; posted ID/timestamps are ignored.
- Authenticated create/update/delete: real persistence, UTC server timestamps, retained created
  date, nullable optional fields, actual collection cache invalidation, missing 404 and invalid ID 400.
- Real provider reads/writes: imported timestamp ticks, Unspecified Kind, SQL NULL reads and EF
  NULL writes. No timezone conversion or synthetic production-parity claim.
- Two independent tracked PostgreSQL repositories: xmin rejects stale update/delete with
  DbUpdateConcurrencyException and preserves the winning state. No HTTP ETag, 412 or uniqueness
  contract is invented; the current HTTP DTO exposes no expected-version token.
- Real cache adapter/service: failed reads/writes fall back to PostgreSQL; failed removal does
  not roll back committed mutations; controlled recovery restores fresh collection reads.
  Removal failure alone is best effort and may retain a stale cached projection, not a guarantee
  of cross-process coherence. Cancellation propagates rather than becoming fallback success.
- Design-time context: missing/blank external configuration fails closed; supplied disposable
  PostgreSQL configuration connects and queries the actual schema. No historical secrets copied.
- Actual generated OpenAPI document in non-production includes the versioned route; Production
  hides that documentation endpoint. Generated-transformer internals are not mirrored in tests.

## Bounded source-owner evidence

Read-only committed objects were inspected in the original source repository:

- `5fac706a7983a6d359b39acbd670e6800afe020e`: original Country controller/entity, basic legacy
  fields, anonymous sorted collection/empty convention and server-generated create/update dates.
  The tests prove those bounded paths against the current producer, not deployment parity.
- `2aab25eb07894fc0267b03b85bad96490219d2fa`: Country-specific external context configuration.
  The current provider is PostgreSQL, not an assertion that historical SQL Server infrastructure
  or resource configuration has been migrated wholesale.
- `7d6f46f53cbab853ca9c25e385af067cfff6238a`: Country appsettings/scaffold external-configuration
  ownership. Missing/current supplied configuration behavior is proved without emitting earlier
  credential values or claiming historical operational changes resolved.

Existing PR #24/#27 and full `5ac7`/`f064` source-owner provenance in the earlier Country evidence
remain unchanged. Other pending Country deployment/resource/secret owners are outside this slice.

## Validation and exclusions

Local dependency pins match CI: ServiceDefaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`,
CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Set DOTNET_PROCESSOR_COUNT=1,
UseLocalMalievDependencies=true and MalievWorkspaceRoot to the isolated pinned dependency root.

Commands: `dotnet build Legacy.Maliev.CountryService.slnx -c Release -warnaserror`;
focused `dotnet test Legacy.Maliev.CountryService.Tests -c Release --no-build --no-restore
--filter FullyQualifiedName~CountryLifecycleAcceptanceTests`; full same test project with
`--collect:"XPlat Code Coverage"`; `dotnet format ... --no-restore --verify-no-changes`;
`git diff --check`, changed-file gitleaks and direct package vulnerability audit.
TRX/coverage artifacts stay outside the repository.

Measured unexcluded full-suite coverage, including the final NULL-write assertion: 82/82 passed,
zero skips; API 19.04% lines/4.62% branches, Application 98.7%/100%, Data 93.53%/70%,
Domain 100%/100%. Actual Program and both controllers are 100% lines/branches. API includes
unexecuted generated OpenAPI XML infrastructure. Combined own-assembly line entries are
411/652 (63.04%); no generated code or migrations are excluded to manufacture 80%.
The 80% repository requirement remains unresolved and #32 must stay open. This is a substantive
bounded acceptance/defect repair, not completion of the whole coverage gate.

No deployment, persistent data/schema write, credential rotation, source edit, shared dependency
edit or ledger write occurred. Cross-service auth, real Redis outage/recovery, full Aspire and
production-derived data/TLS acceptance are deliberately not claimed here.
