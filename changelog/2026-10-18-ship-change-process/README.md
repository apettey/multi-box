# Ship-change process

**Date:** 2026-10-18

## Description

One process for every change, and a release on every merge. Before this, releases were cut by hand with
`scripts/release.ps1`, and PR #15 and #16 had merged without a release. Now merging to `main` builds, tags and
publishes the next version, and installed copies auto-update to it.

## Change

- **Skill:** `.claude/skills/ship-change/` (SKILL.md and references for formats, review loop, versioning, checklist),
  ported from eve-contracts' skill with multi-box values. `AGENTS.md` section 1 now holds the six blocking rules;
  `CLAUDE.md` imports `AGENTS.md`.
- **Release log:** `changelog/` folders replace hand-edited `CHANGELOG.md` entries. `CHANGELOG.md` is now generated at
  release; its hand-written `[Unreleased]` section ships with the first automated release, under "Earlier,
  unversioned changes".
- **Announcements:** `whats-new/features.json` (empty), validated in CI, used for Release notes until the app has a
  What's New view.
- **Versioning:** `scripts/release/Release.psm1` (pure bump, notes and changelog logic, 38 Pester tests),
  `next-version.ps1` (gathers PR labels and files via git and `gh`), `finalize.ps1` (notes, `CHANGELOG.md`,
  `<Version>` bookkeeping commit; `-DryRun`), `validate-announcements.ps1`. `scripts/release.ps1` is removed.
- **CI:** new `.github/workflows/ci.yml` on PRs: tests, full build, Pester, PSScriptAnalyzer, actionlint, announcement
  validation, a required `changelog/` entry (Dependabot exempt), "Merging this PR would release vX.Y.Z" as summary
  and comment, and the Windows publish + Velopack pack.
- **Release:** `.github/workflows/release.yml` now runs on push to `main` only: version → shared Windows build that
  tags only after packing → notes and bookkeeping. `deploy:manual` publishes a prerelease. Needs the new optional
  `version` / `release-tag` / `prerelease` inputs in `apettey/eve-contracts`' `build-windows-app.yml`, merged first.
- **Screenshots:** `scripts/capture-window.ps1` captures a MultiBox window at a fixed size (dev tooling; not shipped,
  touches no EVE window). No new native calls, file writes or outbound hosts in the app.
- **Tested:** Pester suite (38/38) and PSScriptAnalyzer on macOS with pwsh 7; `next-version.ps1` against the real
  history (gives v1.0.4 for #15 + #16, and "already released" for v1.0.3); `finalize.ps1 -DryRun`; actionlint on both
  workflows. Not run: the workflows themselves (first run is this PR's CI and its merge), and `capture-window.ps1`,
  which needs Windows.
