## What

<!-- What changed and why, in plain language. -->

## Screenshots

<!-- Visible changes only. Real app, live clients. Link raw URLs pinned to a commit SHA so they survive branch deletion. -->

| Before | After |
| --- | --- |
|  |  |

Release log: `changelog/YYYY-MM-DD-<name>/`

## EULA boundary

<!-- "Unchanged", or what widened (native call, file write, outbound host) and why. -->

## Testing

<!-- N/N tests pass; what you ran against live clients; anything not exercised. -->

## Checklist

- [ ] Code change: tests added/updated; `dotnet test tests/MultiBox.Core.Tests/MultiBox.Core.Tests.csproj -c Release` passes locally. Non-code: CI green
- [ ] Release-log entry: `changelog/YYYY-MM-DD-<name>/` (with before/after if visible) + index row
- [ ] Visible to users? `whats-new/features.json` entry with this PR's number
- [ ] Docs updated where the change made them untrue
- [ ] Review loop done: <N> rounds, last one with no findings
- [ ] Version bump: CI's "would release vX.Y.Z" is right (announcement = minor, everything else = patch), or a `semver:*` label (visible fix = `semver:patch`); `deploy:manual` if a manual step must happen before installed copies update
- [ ] Code change: release artefact builds locally (`.\build.ps1 -SelfContained`)
