# Restore reproducibility

DotBoxD currently uses Central Package Management without NuGet lock files. Restore therefore resolves
the graph allowed by centrally pinned direct dependencies plus transitive constraints; a newly published
transitive version can change the closure without a repository diff.

The compensating controls are explicit `NuGetAudit=true` and `NuGetAuditMode=all`, SHA-pinned GitHub
Actions, package-consumer smoke tests, provenance attestations, and review of the resolved graph during
release readiness. `NU1900` remains non-fatal only when the advisory source itself is unavailable; known
vulnerability diagnostics retain their configured severity.

Revisit lock files when both conditions hold:

1. two consecutive stable releases require no central dependency or target-framework graph change; and
2. a clean Windows and Linux restore produces an identical package closure for every shipping project.

At that point, generate and review all lock files in one dedicated change, enable locked restore in CI
and release workflows, and document the regeneration command. Until the trigger is met, do not add
project-local lock files piecemeal.
