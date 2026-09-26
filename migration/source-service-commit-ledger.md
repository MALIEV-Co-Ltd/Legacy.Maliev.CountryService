# Standalone CountryService source commit parity

This ledger classifies original `MALIEV-Co-Ltd/Maliev.CountryService` commits against
`Legacy.Maliev.CountryService`. It is separate from the monorepo source ledger in
`Legacy.Maliev.Workflows`. All source SHAs below are full, published main commits.

| Source SHA | Disposition | Evidence |
| --- | --- | --- |
| `4f389ba5fa256ee0a8ac01256b810a2ab3513955` | Migrated | Update `xunit.runner.visualstudio` to 4.0.0 in `Legacy.Maliev.CountryService.Tests.csproj`. The original also refreshes its test package lockfile; this Legacy repo has no NuGet lockfile. Validate by restoring/building and running the full test suite. |
| `7e5f639600ef0bbe9a3bb76bbe0dd3d6e9c540ba` | Migrated through shared publisher | CountryService's gated `publish-image.yml` now pins `Legacy.Maliev.Workflows` at `dfea9b89226b6fc0c2885c4a7c4212497d846a89`. That published revision uses the source's exact `docker/setup-buildx-action@f87e5991a6d7451dcb8d9637bfbc97413f497069` and `docker/build-push-action@c3c9e263c25d99ce0380d002d59b67737d91b0dc` pins. The previous reusable publisher revision differed only in those two action pins; inputs, outputs, permissions, and deployment gate were unchanged. |

Source `2bef2ecd27652fab3f884b53513bfe3c77ddda9f` is outside this
bounded slice. Applicable .NET 10.0.12 packages were independently aligned in
Legacy CountryService PR #19; remaining package differences require their own
compatibility review, not a wholesale source file copy.

Tracking: [Legacy CountryService issue #20](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/issues/20).

Test-runner parity validation (PR #21; Windows, .NET 10):

- `dotnet build Legacy.Maliev.CountryService.slnx -c Release -warnaserror`: passed, 0 warnings and 0 errors.
- Focused `WorkflowContractTests`: 15 passed, 0 failed.
- `dotnet test Legacy.Maliev.CountryService.slnx -c Release --no-build`: 38 passed, 0 failed, 0 skipped. The test output identified xUnit VSTest Adapter 4.0.0.
- `dotnet format Legacy.Maliev.CountryService.slnx --verify-no-changes --no-restore`: passed.
- `dotnet list Legacy.Maliev.CountryService.slnx package --vulnerable --include-transitive`: no vulnerable packages reported.
- `git diff --check` and test project XML parse: passed.

The CountryService publication workflow now pins the validated shared reusable
publisher at `dfea9b89226b6fc0c2885c4a7c4212497d846a89`. Its deployment
gate remains closed unless `LEGACY_DEPLOY_ENABLED` is explicitly set to `true`.

Publisher-pin validation (Windows, .NET 10):

- The old-to-new shared `publish-image.yml` diff contains only the two Docker action pin updates; the reusable input, output, permission, and gate contract is unchanged.
- Workflows PR #51 merged as the pinned SHA above; its hosted validation and GitGuardian checks passed.
- CountryService Release build passed with 0 warnings and 0 errors; focused publication tests passed 2/2 and the full suite passed 38/38.
- `dotnet format --verify-no-changes`, NuGet vulnerable-package audit, and `gitleaks dir . --no-banner --redact` passed.
