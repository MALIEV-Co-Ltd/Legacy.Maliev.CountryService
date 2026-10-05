# Disabled Country deployment draft

`disabled/deployment.yaml.template` is an **UNRENDERED disabled template**, not a
published-image or deployment receipt. No workflow, GitOps registration, overlay,
publication flag or cluster resource is changed by these files.

“Disabled” describes workflow exclusion and the absence of active registration;
the annotation does not enforce a Kubernetes activation barrier. Applying the
rendered replicas-1 Deployment would create a workload. Such application remains
forbidden without separate release and deployment approval.

The actual Deployment is copied from GitOps committed main
`9598923957dad5e962559e38cf1167bed8cd3fbc`, path
`3-apps/_legacy-country-service/base/deployment.yaml`, with the existing overlay's
`maliev-legacy` namespace made explicit. It retains one replica, one rolling-update
surge and zero unavailable replicas, existing resource bounds, probes, managed
secret reference and security settings. Only image/release placeholders and the
disabled-state annotations are added. No replica counts or resource budgets are
summed across former services.

This corresponds to the **Country** deployment change in source
`54ad353a386dc9476f779f9b3fdf152b93b135c9`, parent
`a7d0a4517ef1cfef638763cb1092088a5932fa2f`, at fixed source checkpoint
`135e526d0dab85c415b3afdcefd7b70fe2c82e2f`. The historical Currency deployment and
Material deployment map to the actual Catalog behavior owner; their separate
source desired-state/disposition obligations remain open. This draft does not
implement Currency behavior or close that mixed source commit.

## Release input is still required

No accepted immutable Country publication was established while preparing this
draft. The observed Country main `01113637c751c7bea1354517be7f8b56a1637bb9`
publication run `37288529027` passed its deployment gate while the publication job
was **skipped**. The old GitOps overlay commit tag is not an accepted image digest.
The template therefore contains no `latest`, fabricated digest or guessed release.

After the owner has accepted a real publication, run the offline renderer with:

- `-AcceptedImage`: the complete approved Country image reference from the existing
  production registry, ending in its exact `@sha256:` digest;
- `-AcceptedReleaseCommit`: the exact source commit accepted for that image;
- `-AcceptedPublishRunUrl`: the corresponding accepted Country publication run.

`deploy/Render-CountryDeployment.ps1` requires all three inputs and rejects floating,
foreign, malformed and zero identities before emitting a manifest to stdout.
It performs no registry lookup or acceptance verification: the caller remains
responsible for the actual image/commit/publication evidence join. A rendered
result remains a **RENDERED disabled draft**, not deployment authorization or proof
that the image exists. No provider tool or publication command is invoked.

The template's `legacy-maliev-country-service` secret must be supplied by the
existing managed-secret desired state. No secret value, credential, database,
volume, workload-identity binding or resource is provisioned by this draft.
Activation, image verification, existing-cluster capacity, readiness, cutover and
rollback remain separate owner gates.

The copied Country baseline has no `emptyDir` or `/tmp` mount. Its non-root and
read-only-root settings are retained; this draft makes no writable-filesystem or
image runtime guarantee. A future writable volume needs separately reviewed
ownership settings and real image/runtime evidence before such a guarantee.

## Tests

The existing Country test project already uses YamlDotNet 16.3.0. New focused tests
parse the real template, guard rollout/secret/probe/security correspondence, invoke
the real offline renderer and verify unchanged non-release YAML fields. Refusal
controls cover missing, floating, foreign, zero, malformed and newline-bearing
release inputs. Positive test digests/URLs are explicit synthetic structural
controls and are never publication evidence.

Build, focused tests, the full Country suite and formatting are performed through
the existing hosted validation lane after review. No SDK, package, dependency pin,
publisher workflow or activation change is part of this slice.
