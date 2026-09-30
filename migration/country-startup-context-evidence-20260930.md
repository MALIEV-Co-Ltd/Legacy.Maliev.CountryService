# Country startup, Docker context and generated documentation acceptance

Tracking: [Country issue #30](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/issues/30).
Baseline: `aadff644213bd53328d3e2a07f699cfb6f36f46e`.
This is Country-owned evidence from committed `maliev-web` objects, not the
standalone Country source ledger and not a claim that other owners are resolved.

## Source ownership and retained behavior

| Full source SHA | Country paths and disposition |
| --- | --- |
| `03eaff1194c3ae2a54ceefeae31deffaff90436f` | Country API `.dockerignore`: preserve private/generated-context exclusion at the actual standalone **root** context. Docker's old policy retained 12/14 nested excluded fixtures; the targeted fix rejects all 14 while retaining required project/source/props/NuGet inputs and a legitimate XML asset. |
| `9e51e6c5da29de8e617b65b59d46882cde6d3b64` | Country Program/Startup/ServiceExtensions/project/Dockerfiles/generated XML: obsolete LoggerService/NLog removal is already present. Actual Production startup now has Country-owned acceptance for the pinned native console and correlated safe incident boundary. Do not reintroduce the subsequently removed NativeLogging project. |
| `03dc9a1271c16e6535934445e9dd6e3f30e8fffe` | Country API/Data/Tests project documentation settings and generated XML deletion: SDK documentation output already stays in build outputs. Parse actual generated API/Data/Application/Domain XML; verify published XML is readable by the non-root image user. No broad XML ignore rule or project suppression is added. |
| `5ac7d045c51194edd9e64d8564f1b726b001be34` | Country Program/Startup/project/Dockerfiles genuinely occur in this object. **Existing resolution retained**: [PR #24](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/pull/24), merge `f4577b8d08ef2e58d07a3e56a012876209fa7a1c`, [CI 36358590578](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/actions/runs/36358590578), [CI 36358731086](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/actions/runs/36358731086). This slice supplements acceptance; it does not replace that provenance. |
| `f0640fe0719b2eb6becda378bff08153d955be07` | Country Startup genuinely occurs in this object. **Existing resolution retained**: [PR #27](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/pull/27), merge `df4aa56353949b43bd534fc7daf20ebb41c42106`, [CI 36384608409](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/actions/runs/36384608409). |

The three pending cohort owners above receive only bounded behavior evidence.
All other Country source-owner records, historical deployment/resource/secret
changes, and every other service owner remain outside this slice. The shared
Workflows ledger is not edited here. Root owns ledger integration and merge.

## Executed validation

Use the exact CI dependency revisions, not floating sibling checkouts:

- ServiceDefaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`.
- CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.
- Local dependency root: `B:/maliev-legacy/.validation-customer-parity-20260930`.
- Commands use `DOTNET_PROCESSOR_COUNT=1`, `UseLocalMalievDependencies=true`,
  and `MalievWorkspaceRoot` set to that pinned dependency root.

```text
dotnet build Legacy.Maliev.CountryService.slnx -c Release -warnaserror --nologo -v quiet
dotnet test Legacy.Maliev.CountryService.Tests -c Release --no-build --no-restore --filter "FullyQualifiedName~AcceptanceTests|FullyQualifiedName~CountryDockerContext"
dotnet test Legacy.Maliev.CountryService.slnx -c Release --no-build --no-restore --collect:"XPlat Code Coverage"
dotnet format Legacy.Maliev.CountryService.slnx --verify-no-changes --no-restore --verbosity quiet
dotnet list Legacy.Maliev.CountryService.slnx package --vulnerable --include-transitive --no-restore
gitleaks dir . --no-banner --redact
git diff --check
docker build --tag country-startup-acceptance:20260930 --file Legacy.Maliev.CountryService.Api/Dockerfile .
```

- Baseline Release build: 0 warnings / 0 errors; baseline suite 44/44.
- Updated Release build: 0 warnings / 0 errors; focused 6/6; full 50/50,
  0 failed / 0 skipped (14 seconds). No tests were removed or excluded.
- Actual Production subprocess uses Kestrel and real application/repository/
  configured EF context against disposable PostgreSQL 18.1. Both collection
  routes return the legacy camel-case country shape; anonymous POST returns 401.
- A fresh process after removal of the disposable Country table returns a
  generic 500 body with an incident ID, null details, no provider detail, and a
  CRITICAL native JSON event with matching incident ID, service/method/route,
  exception type, UTC timestamp and trace correlation. Completion logging
  retains the 500 status. Private unmatched-path/query/header and invalid
  correlation-header markers are absent from captured native output.
- No app service, repository, logger, auth middleware or DbContext is replaced.
  The process receives a fresh **public-only** RSA validation key, no minted
  identity/token, a supported explicit memory-cache configuration, and only
  an allowlist of OS environment variables. No inherited deployment secret,
  OTLP, Redis, IAM or service destination is used.
- Docker context regression builds a scratch image from marker fixtures using
  the real ignore policy, exports its filesystem and checks exclusion/retention.
  It cleans its image, container and temporary context; fixtures are not data
  parity evidence.
- The real image built as
  `sha256:938e9946227ee49f8bad940f72772e42f4b1cfb9c43dd08a87c83046a9a1f3fb`.
  Runtime UID is **1654**, and the API DLL plus all four published XML documents
  are readable. Published XML assembly/member counts: API 22, Data 27,
  Application 28, Domain 9. An additional Linux image check used a disposable
  PostgreSQL container on an **internal** isolated Docker network; HTTP requests
  from inside the API container returned 200/Thailand/TH on both routes as UID
  1654. Both containers and the network were removed.
- Whole-solution formatting, five-project transitive vulnerability audit,
  secret scan and whitespace validation passed.

Local TRX, coverage and image/XML proof artifacts are outside the checkout at
`B:/maliev-legacy/.artifacts/country-startup-context-acceptance-20260930`.

## Deliberately excluded acceptance

This is code/schema/runtime behavior proof on disposable fixtures, not
production-derived data parity, TLS/cookie browser acceptance, Redis acceptance,
IAM employee permission/token integration, OpenTelemetry backend delivery,
production image publication/deployment, or persistent migration application.
The normal suite retains its existing JWT and PostgreSQL model/concurrency tests.
No API wire contract, runtime code, EF configuration, authentication policy,
shared dependency pin or workflow changes are needed by this slice.

In-process coverage is API 18.68%, Application 61.03%, Data 86.98%, Domain 100%.
The separately executed actual HTTP processes are not instrumented by that
collector. These measurements **do not satisfy the whole-service 80% target**;
this slice does not inflate coverage, add exclusions or claim program-wide
coverage completion. Unrelated coverage expansion needs its own bounded lane.
