# Country32: measured runtime boundaries and source-owner matrix

## Current root-reviewed child correctness slice

Issue #34 separately tracks the authoritative collection-read and consumed OpenAPI fixes; parent #32 remains open for its 80 percent API coverage requirement. The earlier test/design-only sections below are retained chronology, not current runtime scope. Root independently inspected the complete runtime/test diff, then executed Release build with warnings as errors (zero warnings/errors), focused42/42 and full90/90, zero skips. Root whole-solution format verification and five-project transitive vulnerability audit passed. Exact current unexcluded coverage is API64.13%, Application98.63%, Data92.17%, Domain100%; no threshold, generated-code exclusion or checker waiver was introduced. Root reports are `TestResults/root-country-focus/root-country-focus.trx` and `TestResults/root-country-full/root-country-full.trx`, with coverage `TestResults/root-country-full/14f2fdb2-bedd-4361-aee2-6a52af008113/coverage.cobertura.xml`. Protected-main PR, exact-head CI and post-merge main are still required. No schema, persistent data, external activation or deployment changes occur.

## Scope and gate

Initially TEST/DESIGN only; root subsequently approved the bounded runtime repair below. Isolated branch `codex/country-runtime-acceptance-20261001`, worktree `B:/maliev-legacy/.worktrees/country-runtime-acceptance-20261001`, clean canonical and live-origin base `dd44db99c149272faecd79093ac2c137d70ee30f`. No shared dependency, original-source, ledger, infrastructure, persistent-data/schema, GitHub, commit, push or deployment edits. Disposable PostgreSQL18 fixtures use the existing actual Program, normal RS256 authentication, controllers, application service, repository and migrations. No mocked service/auth/controller acceptance.

Read repository AGENTS and testing/debugging/auth/TDD/worktree skills completely. Historical AGENTS modern-template fields/ISO regex/versioned-only routes are not this compatibility contract; direct owner main-PR direction supersedes historical develop preference. Native worktree tool is tied to the original Web task, so the explicitly requested Country repository/path was created with repository-scoped Git instead.

## Exact source/current contract

Read-only committed mirror checkpoint `bed10c7d15e0698e0b75f1329d0f312937f5d77f`. Original Country controller is unchanged from `5fac706a7983a6d359b39acbd670e6800afe020e` to checkpoint; Country model/context changes are documentation and configuration rather than new business fields. The source controller reads directly from the database on every collection request. Source `ServiceExtensions.ConfigureSwagger` explicitly calls `IncludeXmlComments` and registers an Authorization/Bearer security scheme and security requirement. Source Startup publishes `/countries/swagger/v1/swagger.json` and `/countries/swagger`; current replacement publishes `/countries/openapi/v1.json` and `/countries/scalar` only outside Production.

| Source/current operation | Actual contract and acceptance |
|---|---|
| GET `/Countries` | Anonymous, ordered names; empty404; current camel-case JSON; real PG lifecycle baseline covers populated/empty paths. |
| GET `/Countries/{id}` | Normal JWT plus current exact `legacy-country.countries.read`;200/404,401/403; positive ID from actual creation/seed, never assumed current rows. |
| POST `/Countries` | Exact create grant;201 with GetCountry Location; database-generated integer ID; server timestamps; caller ID/timestamps ignored. |
| PUT `/Countries/{id}` | Exact update grant;204/404, nonpositive ID400; creation time preserved, modification time updated; optional fields can be cleared. |
| DELETE `/Countries/{id}` | Exact delete grant;204/404; normal JWT denies unauthenticated/wrong grant before mutation. |
| GET `/country/v1/countries` | Additional current anonymous facade; empty200 array; no fabricated versioned mutation/detail aliases. |
| Consumed OpenAPI | Actual routes remain present, but summaries/auth scheme/input length metadata are missing; new HTTP tests expose these gaps. Production hiding remains a baseline control. |

Input is exactly Name(required,max50), Continent(nullable,max50), CountryCode(nullable,max30), Iso2(nullable,max2), Iso3(nullable,max3). No uppercase ISO restriction; lower-case legacy values remain accepted. Response adds Id, CreatedDate and ModifiedDate; database storage preserves existing timestamp-without-time-zone semantics and current xmin concurrency mapping. No timezones, borders, callingCodes, currencies, languages, translations, flags or public rowversion are invented. Imported/current data is not assumed. Historical Currency paths share ledger ownership but are not evidence that Country exposes Currency endpoints; no currency feature is added in this slice.

## Full eighteen pending owner records

Workflows read-only checkpoint `e06959be255581c8cba145899996bc434a75dfcc`, source-resolution JSON with Country owner `pending`. There are16 nonmerge records and2 merge records, not eighteen independent CRUD implementations. All remain pending here; matching runtime tests do not resolve deployment or other owners.

| Full source SHA | Country-owned behavior/files | Current evidence or remaining gate |
|---|---|---|
| `5fac706a7983a6d359b39acbd670e6800afe020e` | Initial CountriesController, Country/Context, CRUD tests, Startup/ServiceExtensions, images/deployment | Actual route/model/normal-JWT/PG82 baseline; consumed docs and cache correctness RED; initial whole-owner scope also includes currency/infrastructure, so not resolved. |
| `3a393215d883fa35e1461f69c876bf2ead7ce36e` | deploy-service, deployment and service ingress separation | Infrastructure/deployment outside this slice; API test is not ingress acceptance. |
| `0822636e5e2d46e4db20a79d27037aab426d85aa` | deployment resource limits/node selector changes | Production placement/resource evidence outside scope. |
| `3a104503328cc3c0d57ff9ae2deafba06d1e46d5` | deployment node selector removal | No runtime/controller change; deployment gate remains. |
| `72eb9f1949176392141951d35e6e06f7c30af4c2` | Country model/context XML docs, Startup/project update | Eight legacy fields unchanged; generated/published XML already tested, consumed summaries RED. |
| `5458b7ddc81a15d72087fa69fb4cfcc27ae75747` | deployment configuration rewrite | No Country CRUD delta; production manifests not applied. |
| `53f4baf373ef04a3ed5ab5c1ef39bd61404c5258` | deployment resources/requests | Resource acceptance remains independent. |
| `93f9f99522fbe6c128acb5d049f2b448e07dba95` | deployment resource optimization | Not resolved by disposable PG or coverage. |
| `90f34b389c298d1ce85abe2ae7ac92877dbbf7af` | Country API/Data/Test project changes | Current .NET10 project graph built0W0E; original multi-project/deployment intent requires whole-owner review. |
| `00ec830615c15b5e4e227046712247b11df0100f` | deploy.ps1 hardening | Portable deployment-script behavior is a separate slice; no deployment execution. |
| `2aab25eb07894fc0267b03b85bad96490219d2fa` | design-time Context/resource credential externalization | Current CountryDbContextFactory requires external connection and actual disposable PG baseline; no original SQLServer connection or credential replay. |
| `7d6f46f53cbab853ca9c25e385af067cfff6238a` | appsettings/scaffold credential externalization | Current external config/startup baseline; original scaffold/source credentials not accessed or replayed. |
| `eb8ed86672bd9afccc6560b547b734d0fcd7363b` | Secret-remediation merge including docs/auth/config | Merge, not independent behavior; constituent owner evidence must reconcile before closure. |
| `a649db99a27bda65274fe1b18866ae226d3c69cf` | Remote-main merge of same configuration cohort | Merge reconciliation only; no invented extra test feature. |
| `72163e9ae11f39f6579423841a2e20529b986fab` | deploy/deploy-service exit-status propagation | Portable script/error-status gate separate from API writes. |
| `f8921b1b1d5846eeaff999af10b640011655d1d4` | throwaway rendered deployment manifests | No original/local deployment execution; structural/runtime script acceptance separate. |
| `143f53ba0a1c81c78d252864ca131d42ed79dc1b` | runtime secrets in deployment manifest | Real secret grants and deployment excluded; actual external JWT/config tests are not production grants. |
| `54ad353a386dc9476f779f9b3fdf152b93b135c9` | rollout headroom deployment resources | Production cluster/rollout readiness remains unproven. |

Preserved resolved provenance: source `5ac7d045c51194edd9e64d8564f1b726b001be34` -> PR24, target `f4577b8d08ef2e58d07a3e56a012876209fa7a1c`, CI36358590578/36358731086; source `f0640fe0719b2eb6becda378bff08153d955be07` -> PR27, target `df4aa56353949b43bd534fc7daf20ebb41c42106`, CI36384608409. Existing JWT PR29 and context/XML PR31 provenance stays untouched. No owner ledger writes or completion claims.

## Baseline and honest coverage

Unique clean CI-pinned clones: Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`; Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. All commands use `-p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/country-runtime-acceptance-20261001/.dependencies`.

- Baseline solution Release0W0E6.28s.
- Focus40pass0fail0skip11s: lifecycle/application/controller compatibility classes; TRX `TestResults/country-baseline-focus/natth_MALIEV-31USFIV_2026-10-01_04_11_55_net10.0.trx`.
- Unexcluded full82pass0fail0skip13s: `TestResults/country-baseline-full-coverage/natth_MALIEV-31USFIV_2026-10-01_04_13_06_net10.0.trx`; Cobertura `72f2d622-2f13-4237-b486-6c7a720162f7/coverage.cobertura.xml`.
- API19.04% lines/4.62% branches; Application98.7%/100%; Data93.53%/70%; Domain100%/100%. The issue's18.68/61.03 was earlier evidence, not this main baseline. Actual Program and both controllers have100% line/branch coverage. API has52 covered runtime line entries and221 unexecuted generated OpenAPI XML helper entries (273total). The sole uncovered Application entry is the default repository interface method, overridden by the actual repository. Do not add duplicate CRUD tests or reflection-call generic framework helpers to manufacture80%.
- No existing Country/shared Workflows coverage checker was found; root confirms none known. No claim of an unchanged checker pass. Measured unexcluded coverage remains below required80; portable CI gate is a proposal, not implemented or weakened threshold. Generated-helper denominator is a policy/registration question, not a missing CRUD diagnosis.

## Genuine RED and root runtime gate

Only new file `Legacy.Maliev.CountryService.Tests/Integration/CountryRuntimeContractAcceptanceTests.cs` plus this document are changed. Four cases use the actual existing disposable fixture, not current production data or substitute authentication. Schema assertions resolve the consumed POST request-body inline/reference schema rather than assuming its CLR component name.

New buildRelease0W0E2.08s; focused4fail0pass0skip, `TestResults/country-runtime-contract-red/natth_MALIEV-31USFIV_2026-10-01_04_22_36_net10.0.trx`:

1. Actual nonproduction operations have no human-readable XML summary, despite source IncludeXmlComments and current generated/published XML.
2. Actual document components have no usable bearer/Authorization scheme, despite real401/403 enforcement and historical Swagger scheme.
3. Actual POST input schema has exactly the five legacy fields, but no maxLength; it reports every nullable optional field required. HTTP validation remains enforced by positional-record constructor attributes. Documentation must not change the wire validator to fit a schema.
4. Prime Thailand cache; fault only cache removal; POST Japan returns201 and genuine PG row exists; restore cache health; next GET returns only Thailand. Swallowed removal failure leaves acknowledged writes invisible for the six-hour stale cache TTL. Existing recovery test simultaneously faults reads then manually invalidates, so does not cover this ordering.

Full86:82pass4fail0skip14s, `TestResults/country-design-full-red/natth_MALIEV-31USFIV_2026-10-01_04_25_20_net10.0.trx`; every unchanged original control passes. Initial guessed schema-component lookup error and one nullable compile error were corrected before confirmed behavior RED; neither is acceptance proof.

## Proposed minimal repair boundaries, not implemented

Documentation: only Country API Program and a narrow new Country OpenAPI transformer/registration file. Register application-local literal v1 OpenAPI XML support so source-generator interception can attach actual application comments, retaining shared title/description, existing paths, Scalar and Production hiding. Add a Country-only schema transformer reflecting already-enforced positional constructor constraints and correct nullable optional-field requirements; never add attributes that change MVC record validation/wire fields. Add usable bearer scheme and per-operation protection reflecting actual AllowAnonymous/RequirePermission metadata; do not introduce new IAM grants or alter authorization. Re-run consumed document, real protected routes and Production-hidden controls, then unexcluded measurement. No promise this repairs the entire generated-helper coverage denominator.

Cache: approval requires an explicit authority/consistency choice. A process-local invalidation-failure flag/retry can pass the single-host fault ordering but does not make multiple replicas or concurrent stale fills coherent; it must not be presented as full source parity. Returning503 after a committed write is also not rollback and can provoke duplicate retries. The conservative distributed-correct option is authoritative PostgreSQL collection reads (Country is a small reference list), explicitly changing the cache-hit policy and its existing unit expectation; this needs root authorization and tests, not silent old-test weakening. Alternatively durable cross-instance generation/fencing needs a separately scoped protocol and is outside the current no-schema/infrastructure authority. Do not implement an incomplete flag-only repair just to turn RED green.

Next gates before cache runtime: add a controlled actual cache-fill/committed-mutation interleaving test and optional-field omission/schema control, then root chooses the cache contract and exact file ownership. No distributed linearizability/production Redis claim from the in-process fixture. Full actual Auth/Redis/cross-service production-derived Aspire acceptance remains distinct and unproven.

Those two further controls are now implemented and observedRED: solutionRelease0W0E1.72s;2fail0pass0skip, `TestResults/country-ordering-omission-red/natth_MALIEV-31USFIV_2026-10-01_04_31_51_net10.0.trx`. Actual name-only POST201 persists nullable fields NULL, while document requires all five fields. Actual pending raw-cache Set barrier captures old Thailand list; Japan201 commits and invalidates successfully; releasing the old fill writes stale cache; subsequent GET hides Japan. No sleep, fabricated authority or fake persistence. Only IDistributedCache transport is controlled; real Program/auth/controller/service/repository/PG remain. Whole solution format verify passes. These reinforce that a removal-fault flag alone cannot solve the proven ordering.

Final pre-repair full88:82pass6fail0skip15s, `TestResults/country-final-design-full-red/natth_MALIEV-31USFIV_2026-10-01_04_33_18_net10.0.trx`; Release0W0E1.20s. All original controls passed before policy change. Format, exact-graph vulnerability audit and scoped secrets scan passed; no known coverage checker exists.

## Approved runtime policy and registration

Root authorized authoritative PostgreSQL collection reads, matching the original source controller and bounding this small reference catalog without new schema, distributed generation, CAS or infrastructure. Application GetAllAsync no longer reads or writes cache. Mutation invalidation and the cache transport remain compatible, but neither can determine collection authority. This intentionally supersedes the old cache-hit unit contract; its replacement asserts stale cache cannot override repository data and verifies no cache read/set, exact cancellation propagation and repository-error propagation without fallback. It does not claim a generic distributed cache fix. Both collection routes share the service and retain ordering/empty semantics.

The late-fill fixture now creates a direct independent raw cache write using a genuine old HTTP snapshot, pauses it, commits through actual JWT/controller/service/PG, completes the stale fill, and checks two independent app factories sharing the stale cache return the committed database list. It does not await a removed production cache Set. No admission, fake rows or fake authority are introduced.

Application-local literal AddOpenApi("v1", ...) registers generated XML comment transformers. First build exposed CS9137 requiring the API project's InterceptorsNamespaces to append Microsoft.AspNetCore.OpenApi.Generated; root explicitly authorized that single property, preserving existing namespaces, packages and versions. Local transformers describe normal bearer authentication only on protected operations and the existing five-field input's lengths/nullable omission; runtime annotations, routes, grants and Production hiding remain unchanged. The shared registration still provides its existing title/description.

First post-repair Release0W0E2.32s; focused48:40pass8fail0skip, `TestResults/country-runtime-focus-green/natth_MALIEV-31USFIV_2026-10-01_04_39_20_net10.0.trx`. All six new regressions pass. Eight old invalid-field theory cases pass400 and unchanged-database checks, but deliberately change PostgreSQL then expect stale cached Thailand on the final read. That remaining assertion is incompatible with the approved authority policy, not a validation regression; a narrow existing-test adjustment was requested before editing it. The failed run is retained as policy-transition evidence, not final acceptance.

Root then approved only the theory name/final variable/final current-read expectation, keeping every400/error-payload/database assertion and setup. The OpenAPI schema transformer now derives Required/StringLength from the existing positional constructor parameter attributes rather than duplicating constraints; it changes documentation only. The generated namespace property is the only project-file change. A misplaced using during the refactor caused a compile diagnostic, corrected before the fresh accepted build; no tests run on that failed build.

## Final producer-candidate evidence (not issue32 closure)

All commands ran serially in this worktree with the exact private graph above; no sibling output use.

- `dotnet build Legacy.Maliev.CountryService.slnx -c Release` plus both dependency properties:0warnings0errors2.52s.
- `dotnet test Legacy.Maliev.CountryService.Tests/Legacy.Maliev.CountryService.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CountryRuntimeContractAcceptanceTests|FullyQualifiedName~CountryApplicationServiceTests|FullyQualifiedName~CountryLifecycleAcceptanceTests|FullyQualifiedName~CountriesControllerCompatibilityTests" --logger trx --results-directory TestResults/country-runtime-focus-final` plus both properties:48pass0fail0skip9s; TRX `natth_MALIEV-31USFIV_2026-10-01_04_41_12_net10.0.trx`.
- Same test command without filter and with `--collect:"XPlat Code Coverage" --results-directory TestResults/country-runtime-full-final`:90pass0fail0skip30s; TRX `natth_MALIEV-31USFIV_2026-10-01_04_41_34_net10.0.trx`; Cobertura `646bd931-7271-49c5-baed-0d93775771bb/coverage.cobertura.xml`.
- Whole `dotnet format Legacy.Maliev.CountryService.slnx --no-restore --verify-no-changes` with the exact two properties provided via environment:exit0. `dotnet list Legacy.Maliev.CountryService.slnx package --vulnerable --include-transitive` on that same graph:exit0/no vulnerable packages. `git diff --check`:exit0; private dependency clones unchanged/clean.
- Final unexcluded API64.13%lines/49.59%branches; Application98.63%/100%; Data92.17%/50%; Domain100%/100%. Actual API/controller paths and consumed generated documentation now execute, but remaining generated helper/transformer branches keep API below80. No threshold waiver, exclusion, fabricated invocation, repetitive coverage-only test, CI checker claim or issue closure. Coverage-gate policy remains a distinct unresolved requirement.
- Full suite retains actual normal-JWT401/403, validation/lifecycle/alias/ownership of data and Production documentation-hidden controls. Two independent app instances share controlled stale transport and real PG; this is local disposable integration evidence, not actual Redis/multireplica deployment or production-derived Aspire acceptance.

Exact changed files: API Program, API csproj, new API Documentation/CountryOpenApi.cs, Application/Services/CountryApplicationService.cs, existing Tests/Application/CountryApplicationServiceTests.cs (explicit superseded cache contract plus propagation controls), existing Tests/Integration/CountryLifecycleAcceptanceTests.cs (authorized theory-only final authority expectation), new Tests/Integration/CountryRuntimeContractAcceptanceTests.cs, and this document. No other production/test files changed; no commit/push/deploy/ledger edit.
