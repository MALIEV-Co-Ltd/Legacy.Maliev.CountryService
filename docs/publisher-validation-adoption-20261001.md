# Country publisher validation adoption - child 38

Frozen candidate for root independent acceptance. No cloud publication,
deployment or persistent-data action occurred. Root owns GitHub integration.

## Boundary and design

Base Country commit: `7782ad44d9b3128916e979e318607f280717fc9c`.
Accepted reusable Workflows publisher:
`503e8846390a597c267d2889b33a9c26863389b3`; root confirmed exact-main CI
`36823157482` successful.

The publisher caller receives exactly two changes: replace its old immutable
`dfea9b89226b6fc0c2885c4a7c4212497d846a89` pin and grant job-local
`actions: read`, required by the reusable exact-SHA main-validation preflight.
Top-level `contents: read`, job-local `contents: read` / `id-token: write`, both
complementary `LEGACY_DEPLOY_ENABLED` gates, triggers, concurrency, image,
Dockerfile, context, environment, immutable dependency refs and WIF / service
account inputs remain unchanged. The reusable digest output is unchanged.
No variable, environment protection, identity or publication permission was
activated. The reusable workflow remains responsible for its exact protected-main
validation checks; the caller does not duplicate that implementation.

The existing executable YAML contract retains every old assertion and adds parsed
permission-map cardinality/value checks, complementary planned-only gate,
top-level read-only permissions, and exact eight-entry input values. The separate
existing OIDC-scoping test is untouched. No low-value duplicate tests were added;
the total remains 113.

## Source traceability (partial delivery-safety disposition)

Read-only committed source mirror:
`B:/maliev-legacy/.artifacts/source-commit-mirror-20260930.git`, checkpoint
`bed10c7d15e0698e0b75f1329d0f312937f5d77f`.

| Full source SHA | Country-owned source paths / intent | This slice's disposition |
| --- | --- | --- |
| `00ec830615c15b5e4e227046712247b11df0100f` | `Maliev.CountryService.Api/deploy.ps1`: harden per-step failures and cleanup | Fail-closed validation prerequisite before the replacement publisher may write; no literal source deploy script copied or executed. |
| `72163e9ae11f39f6579423841a2e20529b986fab` | `Maliev.CountryService.Api/deploy.ps1`, `deploy-service.ps1`: propagate script status reliably | Reusable validation job success is mandatory; caller adopts the accepted immutable implementation and its necessary read ceiling. |
| `f8921b1b1d5846eeaff999af10b640011655d1d4` | `Maliev.CountryService.Api/deploy.ps1`: render manifests to throwaway copies | Historical safety intent retained in traceability; this publisher does not render/apply Kubernetes manifests, so no deployment-manifest parity claim. |

These are not blanket completion of the 49 source owners. Child 38 covers only
publisher adoption. Existing child 36's absent exact-main hosted validation and
broader issue 32 remain separate/open until root records the appropriate gates.
A future successful new-main run does not retroactively prove the absent run for
`7782ad44d9b3128916e979e318607f280717fc9c`.

## Owned dependency graph and executed evidence

Fresh, clean private clones under absolute
`B:/maliev-legacy/.worktrees/country-publisher-validation-20261001/TestResults/.private`:
Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, Contracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Both match committed CI pins.
No old detached Country or sibling outputs were consumed.

```powershell
dotnet build Legacy.Maliev.CountryService.Tests/Legacy.Maliev.CountryService.Tests.csproj -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/country-publisher-validation-20261001/TestResults/.private --no-restore --nologo
dotnet test Legacy.Maliev.CountryService.Tests/Legacy.Maliev.CountryService.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~PublicationDependencyTests|FullyQualifiedName~PublishWorkflowPermissionContractTests'
dotnet test Legacy.Maliev.CountryService.Tests/Legacy.Maliev.CountryService.Tests.csproj -c Release --no-build --no-restore
```

Initial private graph build, test-first build and final Release build: zero
warnings / zero errors; dependencies also emitted into private Release outputs.

- `TestResults/publisher-red/publisher-red.trx`: one genuine old-pin assertion
  failure, one unchanged OIDC contract pass; zero errors, timeouts or skips.
- `TestResults/publisher-focus/publisher-focus.trx`: both tests passed.
- `TestResults/publisher-full/publisher-full.trx`: all 113 passed; zero failures,
  errors, timeouts or skips. Existing service assertions were not changed.
- Whole `dotnet format Legacy.Maliev.CountryService.slnx --verify-no-changes
  --no-restore`, with the private graph environment: exit 0, no source normalization.
- `actionlint .github/workflows/publish-image.yml`: exit 0.
- `git diff --check`: exit 0.
- `dotnet list Legacy.Maliev.CountryService.slnx package --vulnerable
  --include-transitive`: no vulnerable packages across all five projects under the
  current package feed.
- Gitleaks scoped workflow / workflow-tests scans: no leaks. History scan:
  45 commits, no leaks. Signing-resource scanner: exit 0.
- Whole current-tree credential scanner: exit 1, five unchanged synthetic
  connection-string-password fixture findings, values redacted. Paths/lines:
  `Data/CountryModelCompatibilityTests.cs:14`,
  `Data/CountryPostgresConcurrencyMigrationTests.cs:14`,
  `Integration/ApiCompatibilityTests.cs:21,45,65` within the Tests project.
  All have zero candidate diff. No fixture edits, exclusions or clean-scan claim.

## Five scoped audits and exclusions

1. Actions: parsed caller interface and minimal required read ceiling, unchanged
   opt-in/OIDC boundaries; actionlint passes. Required CI files are preserved.
2. API: no route, DTO, controller or runtime permission edits.
3. Messaging: no schema, producer or consumer edits.
4. Migration: no model, migration or database edits.
5. Performance: no query, application or repository edits.

The last four are unaffected-boundary diff reviews, not new runtime integration
proof. The Actions and testing skills guided immutable pin/least-permission
checks and genuine RED-to-GREEN validation. No API coverage exemption, source
owner closure, Aspire or production acceptance is implied. No commit/push was
created; no external state was changed.

## Independent root acceptance

Root inspected the complete parsed caller contract, unchanged inputs and private
dependency graph. Fresh Release build: zero warnings/errors. Focused contracts:
2 passed; unfiltered suite: 113 passed, zero failures/skips. Root full TRX is
`TestResults/root-publisher-full/2026-10-01_13_32_12_net10.0.trx`
(machine prefix omitted). Whole formatting, all five transitive audits,
actionlint and diff checks passed. Scoped and staged scans must pass before
commit; the unchanged current-tree fixture findings remain disclosed.
Generated/private outputs remain ignored and excluded from staging.
Hosted exact-head and subsequent exact-main validation remain separate gates;
local success is not a replacement for the missing prior main event.
