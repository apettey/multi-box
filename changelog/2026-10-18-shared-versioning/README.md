# Shared release versioning

**Date:** 2026-10-18

## Description

Versioning, release notes and the generated `CHANGELOG.md` now run on the release code shared with eve-contracts and
hangarscope, which lives in `apettey/eve-contracts`. This repo's PowerShell copy is removed. The rules don't change:
a `semver:*` label wins, a new announcement entry is minor, everything else is patch, and `deploy:manual` makes a
prerelease.

**Depends on apettey/eve-contracts#20** (the shared actions); until it is merged, the actions this repo calls
`@main` don't exist.

## Change

- `.github/workflows/release.yml`:
  - `version` is the shared `next-version` action, with inputs `bookkeeping-paths:
    CHANGELOG.md,src/MultiBox.App/MultiBox.App.csproj`, `app-name: MultiBox` and `announcement-notes: list`.
  - `finalize` is the shared `finalize-release` action with `changelog-file`, `version-file` and `branch: main`.
  - The build is unchanged.
- `.github/workflows/ci.yml`: the announcement check and the "would release vX.Y.Z" preview use the shared actions.
  The Pester step is removed. PSScriptAnalyzer still lints `scripts/` (`capture-window.ps1`).
- Kept:
  - Re-runs: a tagged commit reuses its tag, and only re-finalizes once the Release is published.
  - The bookkeeping commit is ignored.
  - The generated `CHANGELOG.md` section, with an `[Unreleased]` section folded in, and the `<Version>` commit with
    retries.
  - Notes from the release log and announcements, plus every PR.
  - A failed PR lookup fails the run.
  - The preview counts unreleased merges on `main`.
- New, from the other two repos:
  - Only published tags in `main`'s history are a base.
  - A run on a commit already inside a later release does nothing. The old script would have built an older commit
    again as a new version.
  - Versions are numbered above any tag left by a failed run.
  - `finalize` fails if the Release is a draft or lacks the zip or the Velopack packages.
  - Release-log links are pinned to the release commit instead of `main`.
  - Release notes list PRs in merge order, under `What's new` / `Changes` / `Pull requests` headings, with a compare
    link.
- Removed: `scripts/release/` (Release.psm1, next-version.ps1, finalize.ps1, validate-announcements.ps1 and the
  Pester tests; their cases are merged into the shared suite).
- Docs updated: the ship-change skill (repo values, release references, checklist, reviewer brief, formats),
  AGENTS.md, README.md layout and docs/DEVELOPMENT.md "Releasing". The csproj `<Version>` comment no longer names
  the retired `release.ps1`.
- Tested:
  - Shared suite in eve-contracts: 150 tests. `actionlint` is clean.
  - Dry runs against this repo's real history agree with the old `next-version.ps1` at `main` (the bookkeeping
    commit: nothing to release) and at the `v1.0.4` merge (published: notes only).
  - With the `v1.0.4` tag removed in a scratch clone, both compute `v1.0.4` (patch, #15, #16, #17). The finalize
    dry run produces the same `CHANGELOG.md` section shape as the shipped one.
  - Not run: a release on GitHub with the new workflow. No live clients are needed; no app code changed.
- EULA boundary: unchanged (no app code).
