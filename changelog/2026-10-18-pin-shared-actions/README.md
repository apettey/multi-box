# Pin the shared release actions

**Date:** 2026-10-18

## Description

The release and CI workflows called the shared `apettey/eve-contracts` actions and Windows build at `@main`, so any
push there changed this repo's releases immediately, with a token that can write to this repo. They are now pinned to
a full commit SHA (eve-contracts `v1.1.5`), and updates come in as reviewed PRs.

## Change

- `.github/workflows/release.yml` and `ci.yml`: every `uses: apettey/eve-contracts/...@main` now uses
  `@3bdd873c355fe42dfe926c52dab0a4c6713dec0e # v1.1.5`. That is the same code that was running, so behaviour is
  unchanged.
- Dependabot's existing weekly `github-actions` update bumps the pin when eve-contracts tags a new release.
- `.claude/skills/ship-change/references/versioning-and-release.md`: says how the pin is kept up to date.
- Tested: actionlint. CI on this PR runs the pinned actions.
