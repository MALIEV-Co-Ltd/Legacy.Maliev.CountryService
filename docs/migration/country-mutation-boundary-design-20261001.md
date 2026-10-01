# Country36: bounded mutation authority and conflict acceptance

## Current scope and status — 2026-10-01

Root-created child36 of parent32. Base778c1775ef4061d9291af5efc8ad1828499823b4 already contains #35 authoritative reads and consumed OpenAPI. This slice does not repeat those repairs or close the raw80/API quality gate. Root reviewed the actual six-RED TRX and whole new fixture, then authorized the narrow runtime below. No commit/push, deployment, persistent schema/data, grants, providers, source or ledger mutation. Sole writer/output path is `B:/maliev-legacy/.worktrees/country-api-acceptance-20261001`; older worktrees and canonical are untouched.

Final narrow candidate is frozen for root independent acceptance: fresh Release0W0E; focused55/55 and unfiltered113/113, zero skips; whole-solution format verification, five-project transitive vulnerability audit and six-owned-file redacted secrets scan passed. Raw API64.56% remains below80: parent32/whole-owner scope stays open. Pending/scaffold statements in the chronology below are historical, superseded by the final evidence section. No commit or protected-main acceptance is claimed.

Applicable repository instructions, worktree isolation, MALIEV testing, TDD including writing-good-tests, authentication, receiving-code-review and verification-before-completion skills were read fully. Legacy-template AGENTS fields/versioned mutation suggestions do not authorize invented fields/routes. Runtime approval supersedes the initial tests/design-only gate; original test policy changes still require separate narrow review.

## Architecture and deliberate current policy

Normal Production RS256 HTTP → existing permission handler → actual scoped CountryApplicationService → CountryRepository → real PostgreSQL18.1 tracked xmin. Existing AddStandardCache selects real Redis8 through its actual adapter. The only controlled authority transport is actual IamServiceClient's named `IAMService` primary HTTP handler; ephemeral synthetic live-check credential and disposable identity are never logged. This is local adapter/route acceptance, not deployed IAM grant, upstream success, source data or production-derived Aspire proof.

DELETE retains `legacy-country.countries.delete` and global resource, now forced-live/critical. No stale signed claim fallback when remote IAM denies, returns503/malformed data, or client is absent. Anonymous requests remain401. Allow requires exact POST `/iam/v1/auth/check-permission`, matching principal/permission/global resource, bypassCache=true and live header. Public reads, current create/update/read permissions and all routes remain unchanged.

Repository Update/Delete Save boundaries translate only DbUpdateConcurrencyException with nonempty, exclusively Country entries to Application-owned CountryConcurrencyException. Tracked xmin remains unchanged; cancellation is checked before translation. Inner EF cause stays internal; Application references no EF. Controllers catch only that type and return fixed generic409. Add, arbitrary failures, foreign-key/provider errors and global classifier are unchanged; controlled IOException500/InvalidOperation400 stay redacted. No blind retry/expected-version wire/ETag/rowversion/schema policy is added.

Historical source controller uses JWT with no IAM permission attributes and SQLServer persistence with no current xmin policy. Thus forced-live DELETE and explicit409 are reviewed current correctness/security decisions, not claims of exact historical parity. Existing source create/update/delete/not-found semantics and eight-field wire are preserved.

## Current/source action and consumer matrix

| Operation | Current contract | Existing/new evidence |
|---|---|---|
| GET `/Countries` | Anonymous sorted camel-case eight fields; empty404; PostgreSQL authoritative | Existing lifecycle/runtime #35; new realRedis poison cannot override it |
| GET `/country/v1/countries` | Additional current facade; anonymous empty200 array | Existing alias/405 unsupported POST; new realRedis control |
| GET `/Countries/{id:int}` | Exact `legacy-country.countries.read`,200/404/401/403; PG detail | Existing lifecycle; new realRedis poisoned projection does not override detail |
| POST `/Countries` | Exact create;201/GetCountry Location; five input fields; server ID/dates | Existing full validations/lowercase ISO/null/length/timestamp controls unchanged |
| PUT `/Countries/{id:int}` | Exact update;204/404/nonpositive400; preserve CreatedDate | New realPG winner conflict409/caller abort/arbitrary-fault controls |
| DELETE `/Countries/{id:int}` | Same delete grant/global; forced-live critical;204/404 | New controlled actualIAM allow/refusal/no-client/anonymous + realPG conflict/cancel |
| NonProduction docs / Production hiding | `/countries/openapi/v1.json`, Scalar; consumed bearer/length/nullable summaries | Already #35; no documentation runtime changes here |

Input Name(required50), Continent(nullable50), CountryCode(nullable30), Iso2(nullable2), Iso3(nullable3). Response adds integerId and nullable CreatedDate/ModifiedDate. No new fields, normalization, pagination, uniqueness or currency API.

Read-only current Web HEAD6d0dc9f7095b4128becbf25af5f4de2b22ed7731: `Legacy.Maliev.Web.Infrastructure/CountryClient.cs:18` GET `Countries` via named countries endpoint (`ServiceCollectionExtensions.cs:62`), legacy404 becomes available empty list; transient outages become unavailable; caller cancellation propagates. `Legacy.Maliev.Web.Application/ContactContracts.cs:3` exactly matches eight-field response. Member address/contact/quotation/fulfillment pages use these collection reads, not Country mutations. No consumer wire change.

Read-only Intranet HEAD40275ec6c5b3547ab6d5363fcb8cc03b82d3f473: `Materials/LegacyCatalogClient.cs:11` GET `/Countries` with bearer, eight-field `MaterialContracts.cs:51`; PurchaseOrders/Create.cshtml.cs:74 consumes it. **Actual Program.cs:72 targets Services:Catalog**, so this is a Catalog facade consumer, not evidence of a direct CountryService endpoint/permission/deployment. No Country mutation caller was established; do not infer its new DELETE readiness from Intranet.

## Complete source-owner lineage, not whole-owner retirement

Committed source mirror cutoffbed10c7d15e0698e0b75f1329d0f312937f5d77f. `git log` for CountriesController has exactly the initial commit below; latest source retains same five actions. Country model stays eight fields. Ledger read-only currentdbc33fd3f64f33e18fcff148a294a323109b95de, `migration/source-commit-resolutions.json`:24 Country records,18 pending/6 migrated (22 nonmerge/2merge). Earlier inspected d003919533400df33eb6c7245b885a14876ec760 had unrelated dirty report preserved; it is historical snapshot only. Relocation to MigrationTracking is not completion; no record changes here.

| Individual full SHA | Bounded owned intent/disposition |
|---|---|
| 5fac706a7983a6d359b39acbd670e6800afe020e | Initial CountriesController/CountryContext/Country/CRUD source. Current runtime parity bounded; initial currency/infrastructure co-ownership remains pending. |
| 3a393215d883fa35e1461f69c876bf2ead7ce36e | Deployment/ingress separation pending, excluded. |
| 0822636e5e2d46e4db20a79d27037aab426d85aa | Resource limits/node selector pending, excluded. |
| 3a104503328cc3c0d57ff9ae2deafba06d1e46d5 | Remove deployment node selectors pending, excluded. |
| 72eb9f1949176392141951d35e6e06f7c30af4c2 | Model/context XML docs/Startup/project update; no new action/field; remaining owner pending. |
| 5458b7ddc81a15d72087fa69fb4cfcc27ae75747 | Deployment configuration rewrite pending, excluded. |
| 53f4baf373ef04a3ed5ab5c1ef39bd61404c5258 | Deployment resources pending, excluded. |
| 93f9f99522fbe6c128acb5d049f2b448e07dba95 | Resource optimization pending, excluded. |
| 90f34b389c298d1ce85abe2ae7ac92877dbbf7af | Project graph/frontend deployment split pending; no Country action delta. |
| 00ec830615c15b5e4e227046712247b11df0100f | Deployment script hardening pending, excluded. |
| 2aab25eb07894fc0267b03b85bad96490219d2fa | External design-time connection pending, existing current PG controls not original SQLServer/operational closure. |
| 7d6f46f53cbab853ca9c25e385af067cfff6238a | External scaffold/runtime database config pending, no credential replay. |
| cbac7d7155da2208c77d56103b6a2cb19196fc83 | Migrated via28/29 aadff644213bd53328d3e2a07f699cfb6f36f46e, CI36432571798; unchanged. |
| eb8ed86672bd9afccc6560b547b734d0fcd7363b | Secret remediation merge pending; constituents not extra actions. |
| a649db99a27bda65274fe1b18866ae226d3c69cf | Remote-main merge pending; no invented extra behavior. |
| 03eaff1194c3ae2a54ceefeae31deffaff90436f | Docker context migrated30/31 c1e6222e9b7de6431b0e5115e4e43419e12e1323 CI36682469410/36682800993; unchanged. |
| 72163e9ae11f39f6579423841a2e20529b986fab | Deployment exit-status propagation pending, excluded. |
| f8921b1b1d5846eeaff999af10b640011655d1d4 | Throwaway rendered manifests pending, excluded. |
| 143f53ba0a1c81c78d252864ca131d42ed79dc1b | Deployment runtime secrets pending; no provider/grant acceptance. |
| f0640fe0719b2eb6becda378bff08153d955be07 | Request failure tracing migrated27 df4aa56353949b43bd534fc7daf20ebb41c42106 CI36384608409; unchanged. |
| 9e51e6c5da29de8e617b65b59d46882cde6d3b64 | Native logging migrated30/31 c1e6222e9b7de6431b0e5115e4e43419e12e1323 CI36682469410/36682800993; unchanged. |
| 54ad353a386dc9476f779f9b3fdf152b93b135c9 | Deployment rollout headroom pending, excluded. |
| 03dc9a1271c16e6535934445e9dd6e3f30e8fffe | Generated XML isolation migrated30/31 c1e6222e9b7de6431b0e5115e4e43419e12e1323 CI36682469410/36682800993; unchanged. |
| 5ac7d045c51194edd9e64d8564f1b726b001be34 | Application-local logging migrated23/24 f4577b8d08ef2e58d07a3e56a012876209fa7a1c CI36358590578/36358731086; unchanged. |

## RED chronology and commands

Private detached clean CI deps under `TestResults/.dependencies`: Defaults8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3, Contracts78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7. All commands use `DOTNET_PROCESSOR_COUNT=1`, `-p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/country-api-acceptance-20261001/TestResults/.dependencies`.

- Baseline `dotnet build Legacy.Maliev.CountryService.slnx -c Release -warnaserror`:0W0E6.18s. Focus existing lifecycle/runtime/repository39/39; full90/90 zero skips. Baseline TRX `TestResults/country32-baseline-full/natth_MALIEV-31USFIV_2026-10-01_12_21_53_net10.0.trx`; rawcoverage API64.13/Application98.63/Data92.17/Domain100, API below80.
- Initial new test compiler throw-precedence error corrected; not productRED/no test on failed build. Fresh0W0E then15tests6 genuine assertionRED9GREEN0skip (`country32-mutation-red/...12_25_41_net10.0.trx`). Three refused-IAM DELETE204; positive wire bypassfalse; actual two-context PUT/DELETE500 instead409.
- Strengthened winner-state check before response assertion and HTTP canceled-xmin controls: fresh0W0E then17tests6RED11GREEN0skip (`country32-mutation-expanded-red/...12_27_17_net10.0.trx`). Controlled actual Redis stores poison; both collection aliases/detail ignore it and realPG is authoritative. Arbitrary IOException500/InvalidOperation400, missing404 and save-boundary abort controls pass.
- Root-required direct registered-service cancellation at genuine EF ThrowingConcurrencyExceptionAsync first observes canceled passed token; two original DbUpdateConcurrencyException insteadOCE fail genuinely. MissingIAM signed DELETE204 instead403 also RED; anonymous401 controlGREEN. Additional4:3RED1GREEN0skip (`country36-additional-red/...12_28_36_net10.0.trx`). This direct-service proof is separate from normal HTTP route authorization, not positive HTTP acceptance.
- After minimal approved runtime freshRelease0W0E,21/21 newfocusGREEN0skip (`TestResults/country36-focus-green/natth_MALIEV-31USFIV_2026-10-01_12_29_39_net10.0.trx`). Full original-policy compatibility run is pending; do not label candidate final until old policy deltas explicitly reviewed.

Focused/full command: `dotnet test Legacy.Maliev.CountryService.Tests/Legacy.Maliev.CountryService.Tests.csproj -c Release --no-build --no-restore --logger trx --results-directory TestResults/<owned-lane>` plus dependency properties; focus `--filter FullyQualifiedName~CountryMutationBoundaryAcceptanceTests`; full unfiltered with `--collect:"XPlat Code Coverage"`. No exclusions/threshold edits/skips. Final static and rawcoverage counters will be appended after execution.

## Explicit existing-policy compatibility review

First full111 =109PASS/2FAIL/0skip (`TestResults/country36-policy-transition-full/natth_MALIEV-31USFIV_2026-10-01_12_31_42_net10.0.trx`). Only the reviewed transitions failed: old raw DbUpdateConcurrencyException expectation and positive DELETE with no IAM client. Root read those complete tests/fixture and approved ONLY:

1. Two RepositoryXmin assertions now require exact CountryConcurrencyException with actual DbUpdateConcurrencyException InnerException and nonempty exclusively Country entries. Winner/deletion/no-overwrite assertions remain intact.
2. Existing positive lifecycle's two DELETE requests alone use an opt-in derived WebApplicationFactory with actual IamServiceClient and strict named HTTP transport. Original fixture/factories/negative tests have no IAM registration. Transport allows only existing employee:country-acceptance principal/exact delete permission/global/bypassCache=true/fresh runtime synthetic header; token comes from original fixture. All prior create/update/read/validation/body/status/database assertions remain unchanged. This is controlled remote fixture adaptation, not deployed permission proof or source historical requirement.

Two additional HTTP controls reject entryless DbUpdateConcurrencyException as ordinary redacted500, not409. HTTP canceled-xmin waits for actual EF callback and observed canceled token before asserting actual OCE; timeout exceptions cannot satisfy it. Registered-service cancellation proof independently reaches real EF conflict and checks exact passed token before asserting OCE. No generated/reflection coverage injection or threshold change.

## Final frozen evidence and handoff

- Scoped formatter touched only the authorized runtime/new tests and explicitly reviewed old fixture/assertions. Fresh Release build0warnings0errors4.50s; no source changes after that build except this documentation.
- Focused existing lifecycle plus all23 new boundary cases:55PASS/0FAIL/0SKIP6s. `TestResults/country36-final-focus/natth_MALIEV-31USFIV_2026-10-01_12_35_45_net10.0.trx`.
- Unfiltered relevant suite:113PASS/0FAIL/0SKIP24s. `TestResults/country36-final-full/natth_MALIEV-31USFIV_2026-10-01_12_36_16_net10.0.trx`; coverage `TestResults/country36-final-full/80b16785-7c2e-4cc0-84ff-20f0a19ab1e9/coverage.cobertura.xml`.
- Unexcluded line/branch percentages: API64.56/49.59, Application98.64/100, Data92.43/50, Domain100/100. Coverlet API line entries215/333: handwritten106/106; generated OpenApiXmlCommentSupport109/227. These separate diagnostics do not remove the generated denominator or satisfy raw80. No coverage checker or waiver was introduced; no duplicate/reflection-only helper tests were added to inflate it.
- `dotnet format Legacy.Maliev.CountryService.slnx --no-restore --verify-no-changes` exit0, using exact private dependency properties via environment. `dotnet list Legacy.Maliev.CountryService.slnx package --vulnerable --include-transitive` exit0/no vulnerable packages for all five service/test projects. `git diff --check` exit0.
- `gitleaks dir <each-owned-file> --redact --no-banner --report-format json --report-path TestResults/country36-secrets-<1..6>.json`: sixexit0/no leaks. Runtime-generated RSA/token/header values were not emitted. Final documentation readback/re-scan preserves all historical RED and policy-transition TRXs.
- Exactly six changed files: API Controllers/CountriesController.cs; Data/CountryRepository.cs; new Application/Exceptions/CountryConcurrencyException.cs; reviewed existing Tests/Integration/CountryLifecycleAcceptanceTests.cs; new Tests/Integration/CountryMutationBoundaryAcceptanceTests.cs; this document. No project/package/shared-default/cache/OpenAPI/schema changes.
- Private clones remain clean/detached at exact8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3 and78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7. No active owned dotnet/testhost/vstest process observed at final release. All process handles were terminal before output release. Root owns next independent build/focus/full and any commit/PR.

Residual gates: parent32 rawAPI80; deployed IAM/live credential/grant and actual production identity; original deployment/SQLServer/data parity/cluster/secret and pending source owners; cross-service production-derived Aspire/browser acceptance. No persistent writes/provider access/activation took place. Disposable synthetic PG/Redis controls are not data or upstream authority certification.

## Independent root acceptance

The orchestrator independently reviewed the runtime boundary, new HTTP/registered-service tests, complete existing lifecycle fixture adaptations, source matrix and consumer scope. A fresh exact-private-dependency Release test-project graph built with zero warnings and errors. Combined lifecycle/mutation focus passed 55/55 with zero skips (`TestResults/root-country36-focus/natth_MALIEV-31USFIV_2026-10-01_12_40_05_net10.0.trx`); the unfiltered suite passed 113/113 with zero skips (`TestResults/root-country36-full/natth_MALIEV-31USFIV_2026-10-01_12_40_13_net10.0.trx`). Whole-solution format verification, all five transitive package audits and diff whitespace verification passed. The raw API coverage gate remains open on parent #32; this bounded #36 mutation repair does not waive that gate or close the pending operational source owners. No deployment, persistent schema/data writes or production-derived Aspire acceptance occurred.
