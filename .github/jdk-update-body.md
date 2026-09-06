## Summary
Updated pinned JVM releases, generated declarations, and the compatibility inventory.

## Rationale
Keeps bindings synchronized with upstream headers and tests current available runtimes.

## Changes
See the generated API and vendor archive diffs; no handwritten bindings were edited.

## Verification
Deterministic generation, ABI checks, unit tests, and the full NativeAOT JVM matrix are required before merge.

## Performance
No performance improvement is claimed; runtime reports include process execution results.

## Risks & Rollback
Upstream ABI or runtime changes may affect compatibility. Revert this update to restore the previous pinned inputs.

## Breaking/Migration
Generated public API changes require review; compatible archive updates may auto-merge after checks pass.

## Links
See the source URLs and SHA256 values in the header provenance and JDK lock files.
