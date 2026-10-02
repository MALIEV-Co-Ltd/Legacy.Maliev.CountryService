# Country43 published-contract consumer repair — local review candidate

CURRENT: seven-file metadata-only candidate: controller/output-ID schema,
accurate request/response XML comments and dynamic Application XML build input,
NEW consumer test and this doc. Fresh Release and Debug builds0W/E, six focused
PASS and full184PASS/0FAIL/0skip; raw API385/473=81.40% without exclusions.
Protected-head/main CI, broader32 and full source-owner acceptance remain pending.
The original TEST/DESIGN scope and RED/diagnostic checkpoints below are historical.

Accepted base `0f753edb9a37bcf1e4f349a78819d220c9561241`, unique branch
`codex/country-openapi-consumer-proof-20261002`. Only this NEW document and NEW
`Tests/Integration/CountryOpenApiConsumerAcceptanceTests.cs` are owned. No existing
runtime, comments, tests, metadata, project or coverage checker edits.

Frozen source checkpoint `bed10c7d15e0698e0b75f1329d0f312937f5d77f` was read through
the immutable committed-object mirror. Country controller/model originated in
root commit `5fac706a7983a6d359b39acbd670e6800afe020e` (no parent); broader update
`72eb9f1949176392141951d35e6e06f7c30af4c2`, parent
`023aa2f143fa5a8f557d72c1302b9add64792d6a`, remains separately pending.
Source `03dc9a1271c16e6535934445e9dd6e3f30e8fffe`, parent
`b1dc51ef5241ceb40a3c8a4e5c3f3a823d16b07e`, removes the checked-in generated
`Maliev.CountryService.Api/Maliev.CountryService.Api.xml`, NOT controller/model
comments. Original `Controllers/CountriesController.cs` documents identifier/item
parameters and returns; `Data/Database/CountryContext/Country.cs` documents each
integer/scalar/timestamp property. The generated companion before deletion confirms
those comments. Current migrated controller retains summaries only. Source lengths
50/50/30/2/3 and integer identity agree with the current independent DTO oracle.
No legacy connection configuration was printed/copied.

Five cases pair normal HTTP with genuine disposable PostgreSQL effects before
checking emitted OpenAPI: create201/Location/nullable persistence; invalid51-character
name400 validation problem/no write; protected read401/403/no disclosure; genuine
delete xmin409/concurrent winner preserved; integer-ID200/noninteger404 and identifier
parameter semantics. Expectations are literals/source/current DTO contracts, never
derived from generated output. Existing public lifecycle and normal-IAM fixtures
are reused unchanged; no IIam fixture registration, principal substitution or SDK
reflection. The delete case reaches normal scoped IAM composition and only its named
external Auth/IAM transport is controlled. This is synthetic boundary proof, not
deployed grants or production Aspire acceptance.

Normal Development docs requests already execute the generated XML callbacks;
missing response/parameter/property tags cannot be covered simply by requesting
the same document repeatedly. Any missing published contracts require separately
reviewed accurate XML/response metadata and potentially referenced Application XML
input; none is authorized here. No examples are invented just to exercise upstream
branches. Existing raw root178 evidence: API302/423=71.39%, Application73/74=98.65%.
Focused coverage is not a full-suite threshold proof. No exclusions, new endpoints,
runtime composition change or 80% guarantee. Broader32 and all16 operational owners
remain pending. Actual build/focus/coverage evidence follows after execution.

## Reached evidence and review gate

Exact private pins: Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, Contracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Baseline and final
`dotnet build Legacy.Maliev.CountryService.slnx -c Release --no-restore
-p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/country-normal-iam-composition-20261002/.dependencies`
both passed with zero warnings/errors.

Focused command: `dotnet test Legacy.Maliev.CountryService.Tests/Legacy.Maliev.CountryService.Tests.csproj
-c Release --no-build --no-restore` with the same property,
`--filter FullyQualifiedName~CountryOpenApiConsumerAcceptanceTests --collect "XPlat Code Coverage"`,
unique results directories and TRX logger. First `country32-openapi-consumer-red`:
4 reached published-contract assertion failures and 1 NEW fixture assumption
(attempting to deserialize the actual plain-text409 as JSON), not five product
failures; original TRX retained, SHA256
`CCE36D2657EF0FB55C35A8182BB4F11856580D97F265659E810BC6CABEEE75D9`.
The subsequent `country32-openapi-consumer-reached` preserved all five metadata
failures but its first denied-read transport incorrectly expected a forced-live
header on a nonforced read; that earlier403 is not explicit IAM-denial proof.
Final NEW recorder observations/assertions are outside the client catch boundary;
they require normal service-login reach, bearer, exact employee/read/global wire,
bypassCache=false and no forced-live credential. The response is explicit
`allowed:false`; no IAM implementation or caller is replaced.

Final `TestResults/country32-openapi-consumer-final-red/consumer-final-red.trx`:
**5 genuine published-contract assertion REDs, zero passes/skips**, after all
preceding normal HTTP/PostgreSQL/control assertions passed. Missing emitted
POST201, POST400, protected GET401, DELETE409 and identifier-description metadata.
GET403, persisted nonmutation, source nullability/length/int identity, Location
readback and real xmin-winner preservation were independently reached. The later
GET403 documentation assertion follows the first missing401 and is not claimed
executed. SHA256:
`0268DEE9CF31F29737B1467587DC50DD16C71DFDD5020955F2D0EA7937FFBBA2`.

Fresh focused-only raw coverage: API272/423=64.30%, Application50/74=67.57%, artifact
`country32-openapi-consumer-final-red/cbf7998c-aafa-47ad-9e52-b72271772da4/coverage.cobertura.xml`.
These are five-case subset measurements, NOT replacement full178 coverage and NOT
an80% claim. Whole relevant/full suite is deliberately not run before root reviews
the genuine RED/design. Scoped formatting verification passed; exact only two
NEW paths remain untracked. Original fixtures/runtime/tests/checkers untouched.

Smallest candidate repair for root review: accurate response annotations/XML
parameters/returns on the existing controller, then genuine schema/property
documentation consumed by the generator if a separately approved DTO XML-input
slice is needed. Document the actual409 **plain-text** shape, not an invented
ProblemDetails contract. Source returns wording is imprecise and should be
adapted to actual behavior, not copied blindly. No new endpoint, permission,
grant, entity, schema or response behavior is proposed. Published description
repair and raw80 acceptance remain separate gates; no runtime edits authorized.

## Root-approved controller metadata checkpoint — issue43

[Country43](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CountryService/issues/43)
is the bounded child of32. Root independently reproduced all five original REDs
before authorizing accurate CountriesController XML parameters/returns/responses
and explicit response declarations ONLY. Controller additions preserve every
method body/route/permission: created CountryResponse201, create validation400,
update ProblemDetails400, body-unspecified401/403/404, successful204 and plain
string/text/plain concurrency409. No DTO/Application XML-input/helper changes.

Both fresh metadata builds passed Release zero warnings/errors. First focus
`country32-openapi-consumer-green` reached all original five metadata oracles,
4PASS/1NEW supplementary representation diagnostic (OpenAPI3.1 type array read
as scalar). Root required strict nonnullable int32: scalar integer or union
exactly integer; analogous strict scalar/one-element string409. Corrected NEW
test retains that rule, not a permissive integer-membership oracle.

Fresh `country32-openapi-consumer-strict/consumer-strict.trx` is **4PASS/1genuine
supplementary schema RED/0skip**: actual201 body/database ID is int32 but emitted
CountryResponse.id allows exactly `["integer","string"]`. No null appears;
the string alternative still violates the root-reviewed strict output contract.
All original status/parameter failures are repaired; strict string409 passes.
This remaining failure is NOT classified as a fixture defect. No assertion was
weakened and no JSON number-handling/runtime serialization was changed.

Proposed next reviewed scope, not implemented: metadata-only CountryOpenApi
schema transformer limited to CountryResponse.id, integer/int32/nonnullable;
do not change request DTO schemas, JSON options, serialization or API behavior.
The existing helper is outside currently authorized ownership. Full183 has not
run while this genuine focused failure awaits root review. Raw80 and broader32,
production/Aspire/grants and source owners remain pending.

## Final four-file freeze

Root approved only exact CountryResponse DTO plus exact id output-schema property
in existing CountryOpenApi: nonnullable integer/int32. No request-schema coercion,
JSON options, serializer, DTO or other numeric fields changed. The genuine strict
`["integer","string"]` RED remains retained, SHA256
`EFB57FF33A33B4F7DBA7141C17EBB392D265D68F7D15071D5D9C7892349DF9B0`.

Fresh serial Release build using the command above: zero warnings/errors. Same
five-case focus with `country43-output-schema-green/output-schema-green.trx`:
5PASS/0FAIL/0skip, SHA256
`E9A70953F002AF7AADC22FB8BC5FA1343296C7CD9D7DDED6C399A9EEA446FFBB`.
Both201 ID and409 body assertions require scalar or one-element-only type,
never null/string alternatives for ID. Original five independent RED oracles
are unchanged, not waived.

Same direct-tests command **without filter**, collect XPlat coverage,
`--results-directory TestResults/country43-full --logger "trx;LogFileName=full.trx"`:
183PASS/0FAIL/0skip,52 seconds. Full TRX SHA256
`94B1A247DB5778A7C63CA45FA1CB74726FF219637F56049C8AFE840505E44DC7`.
Raw coverage `country43-full/002e293f-3bf4-470e-ad52-035cc1d6527a/coverage.cobertura.xml`:
API330/430=76.74%, Application73/74=98.65%, Data281/304=92.43%, Domain8/8=100%.
No exclusions or altered checker. At least14 additional current API lines must
execute for80%; current generated schema callback4/22 and doc-ID helper61/91
are genuine remaining coverage, not a claimed pass. Accurate source-backed DTO
property/XML-input consumer work requires separate approval; no fake examples or
reflection coverage padding is proposed.

Whole `dotnet format Legacy.Maliev.CountryService.slnx --verify-no-changes --no-restore`
with the explicit private workspace environment passed. Sequential
`dotnet list <project> package --vulnerable --include-transitive` passed for all
five owned solution projects and both exact pinned private helper projects, with
no vulnerable packages reported. Four-file scoped gitleaks stdin redacted scan
and tracked/new-file whitespace checks complete before root handoff. No old
assertions, method bodies, routes, permission/grants, schemas, workflow/packages,
AdditionalFiles or operational files changed. No browser, persistent database,
deployment, CI rerun, commit/push or GitHub mutation. Root owns integration.

## Property-description TEST/DESIGN extension for #43

The preceding 183-pass/full-coverage checkpoint is the **pre-extension** candidate,
not acceptance of this deliberately RED addition. Only this new test file and this
doc are edited in this phase. Controllers/CountryOpenApi, DTOs, project/XML inputs,
old tests, schema, serializer and generated files remain unchanged.

One additional case uses normal HTTP create/read plus fresh disposable PostgreSQL
readback before inspecting the public `/countries/openapi/v1.json` document.
It independently checks all five supported input fields, exact max lengths
50/50/30/2/3 and required name only; the prior integer/null/length oracles remain.
The created row has a server-owned integer identity and persisted dates. A second
owned synthetic row is explicitly updated to null dates, then read through HTTP
and PostgreSQL: INSERT defaults otherwise populate those nullable columns.
This is imported-null representation, not execution of a migration/import tool.

The immutable `bed10c7d15e0698e0b75f1329d0f312937f5d77f` model
`Maliev.CountryService.Data/Database/CountryContext/Country.cs` was read completely.
Its property summary/value anchors are identifier, name, continent, country code,
iso2, iso3, created date and modified date. The new assertion requires those
literal anchors for eight output and five input property descriptions, not
observed/generated metadata as its oracle. It adds no examples or inferred
geographical/business promises. The source provenance and pending full owner
SHAs above remain unchanged; this does not close any source owner or broader #32.

Two retained NEW-fixture diagnostics precede the reached metadata RED: exact
seven-digit immediate-response equality against PostgreSQL microseconds, and
assuming INSERT with unspecified dates bypasses current database defaults.
`country43-property-description-red` and `country43-property-description-reached-red`
are not missing-description proof. Fresh HTTP GET now agrees with persisted dates;
explicit synthetic null UPDATE supplies the nullable-date control. The retained
`country43-all-property-descriptions-red` run reached 13/13 missing descriptions,
with the existing five cases passing. Final assertions report only source anchors,
not whole emitted schema bodies.

### Smallest proposed metadata/XML-input repair — NOT implemented

1. Accurate XML constructor-parameter documentation on the two positional records:
   eight CountryResponse fields and five UpsertCountryRequest fields, using source
   anchors and truthful current optional/server-output distinctions only. No type,
   attribute, constructor, serializer or request schema change.
2. Supply the Application assembly's already-generated XML documentation as an API
   analyzer AdditionalFiles input resolved from the referenced project's actual
   build output. Do not hard-code Release/bin or copy generated XML/source; verify
   reference-build ordering for clean Debug/Release and hosted/private graph.
3. Rebuild and re-run the six public-document/real-producer cases before any full
   suite/raw coverage. No callback/reflection invocation or coverage exclusions.

Primary generator mechanism reviewed read-only at ASP.NET Core v10.0.3:
[XmlCommentGenerator](https://github.com/dotnet/aspnetcore/blob/v10.0.3/src/OpenApi/gen/XmlCommentGenerator.cs)
collects referenced `.xml` AdditionalFiles separately from target compilation
comments. [SchemaTests](https://github.com/dotnet/aspnetcore/blob/v10.0.3/src/OpenApi/test/Microsoft.AspNetCore.OpenApi.SourceGenerators.Tests/SchemaTests.cs)
and [AdditionalTextsTests.Schemas](https://github.com/dotnet/aspnetcore/blob/v10.0.3/src/OpenApi/test/Microsoft.AspNetCore.OpenApi.SourceGenerators.Tests/AdditionalTextsTests.Schemas.cs)
explicitly assert positional-record XML `<param>` text becomes property descriptions,
including external XML inputs. Both current projects already generate XML; the
Application records currently have type summaries only, and the API has no XML
AdditionalFiles input. Root must review the precise MSBuild input mechanism before
implementation authority. No 80% promise or deployed Auth/IAM/Aspire acceptance.

Fresh serial Release after the extension: 0 warnings/0 errors. Same focus command
above (six cases), final `TestResults/country43-description-freeze-red/description-freeze-red.trx`:
5PASS/1 genuine metadata FAIL/0skip; 13/13 input/output source descriptions missing.
TRX SHA256 `97FEA071406CD203C1F125F7F961B01403C8906197710C625EDB2A3202308C42`.
Raw **focused-subset** coverage: API300/430=69.77%, Application50/74=67.57%; this
is not a replacement for pre-extension full183 coverage and not broader80 evidence.
No further unfiltered run is appropriate while this reviewed metadata RED remains.

## Reviewed XML comments, automatic-reference attempt

Root approved only thirteen source-anchored XML `<param>` lines on CountryResponse
and UpsertCountryRequest. They now emit thirteen property `<summary>` members in
the Application Release XML, without changing record types or attributes.
Fresh Release 0W/E; six focus still 5PASS/1 genuine RED/0skip, all thirteen
descriptions absent, `country43-property-docs-auto/property-docs-auto.trx`, SHA256
`8D1C290C67782A1A55DF45CA32FCB39EC754BEB059304FBCE2EA3511E89F794E`.
No csproj target was added; no full run while the metadata RED remains.

Actual evaluated dependency is Microsoft.AspNetCore.OpenApi **10.0.12**, supplied
transitively by the pinned Defaults project. Its installed build target uses
AfterTargets ResolveReferences and maps ProjectReference ReferencePath DLLs to
adjacent XML AdditionalFiles. It is in `build`, not `buildTransitive`. The API's
generated NuGet imports do not import that target: requesting its target gives
MSB4057, and evaluated API AdditionalFiles is empty. Application ReferencePath
has ProjectReference provenance and resolves to the actual configuration-specific
DLL beside the correctly emitted XML. These are evaluated build facts, not an
assumption that project references cannot automatically integrate XML generally.

Pending root review, the minimal API-local target proposal is:

```xml
<Target Name="IncludeCountryApplicationXmlComments"
        AfterTargets="ResolveReferences" BeforeTargets="CoreCompile">
  <ItemGroup>
    <AdditionalFiles Include="@(ReferencePath->'%(RootDir)%(Directory)%(Filename).xml')"
      Condition="'%(ReferencePath.Filename)' == 'Legacy.Maliev.CountryService.Application' AND
                 '%(ReferencePath.Extension)' == '.dll' AND
                 '%(ReferencePath.ReferenceSourceTarget)' == 'ProjectReference' AND
                 Exists('%(ReferencePath.RootDir)%(ReferencePath.Directory)%(ReferencePath.Filename).xml')"
      KeepMetadata="Identity" />
  </ItemGroup>
</Target>
```

It intentionally binds only the actual Application reference, not every private
dependency XML, with no hardcoded Debug/Release path, XML copy, generated source,
package change or metadata transformer fallback. Clean configuration/build ordering
and public-document six-case proof still must validate the approved implementation.

## Final seven-file candidate: public XML consumer GREEN

Root reviewed and approved the exact Application-only dynamic target above. It is
now implemented in the API csproj; no package/reference changes, hardcoded paths,
copied/generated XML or schema-transformer description fallback. Seven owned files:
controller XML/response metadata, exact Id output-schema helper, two model XML
comments, API target, new consumer test and this doc. Method bodies, wire types,
attributes, routes, permissions, serializer, database and all old tests are unchanged.

Clean-owned graph validation ran serially for Release and Debug:
`dotnet clean Legacy.Maliev.CountryService.slnx -c <configuration> -p:MalievWorkspaceRoot=<private-root> --verbosity quiet`,
then the earlier `dotnet build ... --no-restore` with each configuration: both0W/E.
An initial clean invocation with unsupported `--no-restore` was a CLI diagnostic,
not a product failure; the corrected clean command above succeeded. No shared
outputs were deleted. Evaluating the API with
`dotnet msbuild <API.csproj> -p:Configuration=<configuration> -p:MalievWorkspaceRoot=<private-root> -target:ResolveReferences -getItem:AdditionalFiles`
returns **exactly one** Application XML in the matching configuration's output.
A fresh Release build again passed0W/E before tests.

The unchanged six public-document/normal-HTTP/PostgreSQL cases now pass6/6, no skips:
`country43-property-descriptions-green/property-descriptions-green.trx`, SHA256
`AB5369AE0518AA4B04B659B61C6A4B4DBF164A1F25CAC64B04477907C1F2C05B`.
Same test command without filter, with coverage/results directory
`TestResults/country43-final184`, passes184/184, no failures/skips,26seconds:
`final184.trx` SHA256
`CB034D4E7947971FFAB102EBB058994FDEFD445515A9E57B6C2D43B815C3DA81`.
Raw coverage `country43-final184/26afae02-f9c0-4575-a509-359c29efc4e1/coverage.cobertura.xml`:
API385/473=81.40%, Application73/74=98.65%, Data281/304=92.43%, Domain8/8=100%.
The increased denominator reflects generated XML consumer code; no exclusion,
checker alteration or claimed waiver. Local API/Application numeric80 is now met,
but broader #32 Aspire/deployed boundary and all pending source-owner dispositions
are **not** completed by this published-contract slice. All earlier RED and fixture
diagnostic artifacts and pre-extension183 evidence remain retained.

Whole-format verification passes after the final code changes. Seven sequential
`dotnet list <project> package --vulnerable --include-transitive` audits completed,
with no vulnerable packages for all five solution projects and both private pinned
helper projects. Seven owned files passed `gitleaks stdin --redact --no-banner`
with no leaks, `git diff --check` and explicit new-file trailing whitespace/extra
EOF blank-line inspection. Final seven-file candidate and all outputs are frozen
for root independent review. No commit/push, GitHub mutation, production provider
call or persistent write.
