[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AcceptedImage,
    [Parameter(Mandatory)][string]$AcceptedReleaseCommit,
    [Parameter(Mandatory)][string]$AcceptedPublishRunUrl
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The caller must supply owner-accepted publication evidence. These checks establish
# structure only; this offline renderer cannot verify registry or release acceptance.
$imagePattern = '\Aasia-southeast1-docker\.pkg\.dev/maliev-website/maliev-website-artifact-prod/legacy-maliev-country-service@sha256:[0-9a-f]{64}\z'
$commitPattern = '\A[0-9a-f]{40}\z'
$runPattern = '\Ahttps://github\.com/MALIEV-Co-Ltd/Legacy\.Maliev\.CountryService/actions/runs/[1-9][0-9]*\z'
if ($AcceptedImage -cnotmatch $imagePattern -or $AcceptedImage.EndsWith(('0' * 64), [StringComparison]::Ordinal)) {
    throw 'An owner-accepted Country image with an exact nonzero SHA-256 digest is required.'
}
if ($AcceptedReleaseCommit -cnotmatch $commitPattern -or $AcceptedReleaseCommit -ceq ('0' * 40)) {
    throw 'The exact nonzero accepted release commit is required.'
}
if ($AcceptedPublishRunUrl -cnotmatch $runPattern) {
    throw 'The accepted Country publication run URL is required.'
}

$templatePath = Join-Path $PSScriptRoot 'disabled/deployment.yaml.template'
$template = [IO.File]::ReadAllText($templatePath, [Text.UTF8Encoding]::new($false, $true))
$bindings = [ordered]@{
    UNRENDERED_COUNTRY_IMAGE_DIGEST_REQUIRED = $AcceptedImage
    __ACCEPTED_RELEASE_COMMIT__ = $AcceptedReleaseCommit
    __ACCEPTED_PUBLISH_RUN_URL__ = $AcceptedPublishRunUrl
    UNRENDERED_DISABLED_TEMPLATE = 'RENDERED_DISABLED_DRAFT'
}
foreach ($binding in $bindings.GetEnumerator()) {
    if ([regex]::Matches($template, [regex]::Escape($binding.Key)).Count -ne 1) {
        throw "The disabled Country template must contain exactly one $($binding.Key) binding."
    }
}
foreach ($binding in $bindings.GetEnumerator()) {
    $template = $template.Replace($binding.Key, $binding.Value, [StringComparison]::Ordinal)
}
if ($template -cmatch 'UNRENDERED_|__ACCEPTED_') {
    throw 'The disabled Country draft still contains an unresolved release binding.'
}

# Emit one actual manifest to stdout. No registration, activation or external call occurs.
Write-Output $template
