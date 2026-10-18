# Release log

One entry per change, newest first. Each entry is a folder `YYYY-MM-DD-<short-name>/` with a `README.md` and, for
visible changes, `before.png` / `after.png`. Old entries are never rewritten; a change to an existing feature gets a
new entry. Format: [`.claude/skills/ship-change/references/formats.md`](../.claude/skills/ship-change/references/formats.md).

Summaries here become the GitHub Release notes and the generated [`CHANGELOG.md`](../CHANGELOG.md), so write them for
players. Internal changes start with `Internal:`.

| Date | Change | Summary |
| --- | --- | --- |
| 2026-10-18 | [Pin the shared release actions](2026-10-18-pin-shared-actions/README.md) | Internal: release and CI use the shared eve-contracts actions pinned to a commit, updated by Dependabot PRs, instead of `@main` |
| 2026-10-18 | [Shared release versioning](2026-10-18-shared-versioning/README.md) | Internal: versioning, release notes and CHANGELOG.md now come from the release code shared with the other apps; same rules, nothing changes for players |
| 2026-10-18 | [Ship-change process](2026-10-18-ship-change-process/README.md) | Internal: every merge to main is now released automatically; one process for tests, screenshots, release log and review |
