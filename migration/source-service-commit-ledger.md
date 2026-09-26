# Standalone CountryService source commit parity

This ledger classifies original `MALIEV-Co-Ltd/Maliev.CountryService` commits against
`Legacy.Maliev.CountryService`. It is separate from the monorepo source ledger in
`Legacy.Maliev.Workflows`. All source SHAs below are full, published main commits.

| Source SHA | Disposition | Evidence |
| --- | --- | --- |
| `4f389ba5fa256ee0a8ac01256b810a2ab3513955` | Migrated | Update `xunit.runner.visualstudio` to 4.0.0 in `Legacy.Maliev.CountryService.Tests.csproj`. The original also refreshes its test package lockfile; this Legacy repo has no NuGet lockfile. Validate by restoring/building and running the full test suite. |
| `7e5f639600ef0bbe9a3bb76bbe0dd3d6e9c540ba` | Not applicable in this repository | The original updates `docker/setup-buildx-action` and `docker/build-push-action` pins in its local validation workflow. Legacy CountryService has no direct use of either action. Its gated `publish-image.yml` calls the pinned `Legacy.Maliev.Workflows` reusable publication workflow. Any shared action update belongs to that repository's own review and validation. |

Source `2bef2ecd27652fab3f884b53513bfe3c77ddda9f` is outside this
bounded slice. Applicable .NET 10.0.12 packages were independently aligned in
Legacy CountryService PR #19; remaining package differences require their own
compatibility review, not a wholesale source file copy.

Tracking: [Legacy CountryService issue #20](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/issues/20).

Validation on this branch (Windows, .NET 10):

- `dotnet build Legacy.Maliev.CountryService.slnx -c Release -warnaserror`: passed, 0 warnings and 0 errors.
- Focused `WorkflowContractTests`: 15 passed, 0 failed.
- `dotnet test Legacy.Maliev.CountryService.slnx -c Release --no-build`: 38 passed, 0 failed, 0 skipped. The test output identified xUnit VSTest Adapter 4.0.0.
- `dotnet format Legacy.Maliev.CountryService.slnx --verify-no-changes --no-restore`: passed.
- `dotnet list Legacy.Maliev.CountryService.slnx package --vulnerable --include-transitive`: no vulnerable packages reported.
- `git diff --check` and test project XML parse: passed.

The CountryService publication workflow pins the shared reusable publisher at
`6e3bb55f5ff3ee2b69dd6b4aee6333777ba0ed36`. That version owns its own
Docker action pins; changing them requires a separately validated Workflows
revision and is not part of this CountryService test-runner change.
