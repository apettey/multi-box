---
name: ship-change
description: End-to-end checklist for shipping any change to this repo — branch, tests, before/after screenshots of the real app, release-log entry, fresh-reviewer loop until a round has no findings, PR with announcement entry, then merge, which automatically releases a new SemVer version (never skipped) that installed copies auto-update to. Use when implementing, testing, screenshotting, reviewing, opening a PR for, merging or releasing a feature, fix, refactor, CI, dependency or docs change.
---

# Ship a change

The rules file (`AGENTS.md`) wins over this skill if they disagree; update both in the same PR.

## The six non-negotiables (blocking: a change missing any of them is not done)

1. **Tests are in place.** Every code change adds/updates tests for the behaviour it changes; the full suite passes locally and in CI. Release scripts have Pester tests; non-code changes need CI green.
2. **Review loop.** A fresh reviewer (subagent that did not write it) reviews the diff; fix every finding; re-review; repeat until a round has **zero findings**. Round count goes in the PR body.
3. **Screenshots.** Every visible change has a `before.png` (taken *before the first edit*) and `after.png` (final version) next to its release-log entry and in the PR.
4. **Release log.** Every change, of any kind, gets a `changelog/` entry in the same PR; visible changes also get an announcement entry.
5. **Every merge is a new version.** No skip. Merging to `main` releases automatically; docs/CI/deps changes are a patch.
6. **Release quickly.** Small PRs, merged the same day. The release (and with it the auto-update deploy) follows the merge with no one acting.

## Repo values

Porting to another repo: change this table, the review rules below it, the reviewer brief in
`references/review-loop.md`, the commands in `references/checklist.md` and `.github/pull_request_template.md`, and the
release section of `references/versioning-and-release.md`. Then grep the skill for `MultiBox`, `multi-box`, `EULA`
and `dotnet` to catch anything left over. The release automation (`scripts/release/`, `.github/workflows/ci.yml`,
`release.yml`) is repo-agnostic apart from the csproj path in `Release.psm1` and `finalize.ps1`.

| Key | Value |
| --- | --- |
| Rules file | `AGENTS.md` (`CLAUDE.md` imports it) |
| Main branch | `main`. PRs are merged with a **merge commit**; not protected (the release job pushes a bookkeeping commit to it) |
| Branch prefix | `feature/`, `fix/`, `chore/` (CI, docs, deps, process) |
| Host | **Windows** with .NET 10 SDK and the EVE clients you play on. The WPF app (`net10.0-windows`) only runs there. macOS/Linux can build (`-p:EnableWindowsTargeting=true`) and test Core, but cannot run or screenshot the app. |
| Dev start | `dotnet run --project src/MultiBox.App` |
| Dev data | Real EVE clients with logging on; there is no seed or mock (AGENTS.md section 3). `multibox-replay doctor` checks the setup. The dev build uses your real `%APPDATA%\MultiBox\multibox.json`; don't press **SYNC NOW** unless the change is about settings sync. |
| Screenshot | `.\scripts\capture-window.ps1 -Size 1600x1000 -Out <path>.png` (add `-Title '<text>'` for a secondary window such as SYNC LAYOUT). Same `-Size` for before and after. |
| Test dir / cmd | `tests/MultiBox.Core.Tests/` (xUnit, asserting against real logs in `samples/`) · `dotnet test tests/MultiBox.Core.Tests/MultiBox.Core.Tests.csproj -c Release --nologo` |
| Script tests | `pwsh -c "Invoke-Pester scripts/release/tests"` (needed when `scripts/release/` changes) |
| Build | `dotnet build MultiBox.sln -c Release --nologo` (add `-p:EnableWindowsTargeting=true` off Windows) |
| Artefact build | `.\build.ps1 -SelfContained` (PR CI also publishes and Velopack-packs via `windows-build`) |
| Release log | `changelog/` (index `changelog/README.md`). `CHANGELOG.md` is **generated** at release; don't edit it. |
| Announcement | `whats-new/features.json` · images `whats-new/img/pr-<N>-<name>.png`. No in-app What's New yet; entries become the GitHub Release notes. |
| Version source | Tags `vX.Y.Z`, computed on merge by `scripts/release/next-version.ps1`. The release job writes `<Version>` in `src/MultiBox.App/MultiBox.App.csproj`; don't edit it by hand. |
| Release | **Automatic:** `.github/workflows/release.yml` on every push to `main` → version → build/pack → tag + GitHub Release → notes + `CHANGELOG.md`. |
| Deploy | Installed copies auto-update from GitHub Releases (Velopack, hourly), so publishing the Release **is** the deploy. `deploy:manual` publishes it as a prerelease, which they ignore until promoted. |

**Repo-specific review rules** (already in the reviewer brief):
- **EULA boundary** (AGENTS.md section 4): logs on disk and public ESI only. No input synthesis (`SendInput`, `keybd_event`, `PostMessage`, `SendMessage`, hooks), no memory reads, no screen scraping of EVE. A new native call, file write or outbound host needs its allow-list entry in `EulaComplianceTests` **with a reason**, plus `docs/EULA-COMPLIANCE.md` and `README.md` updated. Never add a carve-out to get green.
- **Parsing claims** (section 5): every `GamelogParser` pattern comes from a captured log in `samples/`; an unverified one says so in a comment and in the docs.
- `MultiBox.Core` has **no third-party dependencies** and a platform-neutral TFM; Windows-only code lives in `MultiBox.App`.
- **Docs** (section 2): `README.md`, `docs/USER-GUIDE.md`, `docs/ARCHITECTURE.md`, `docs/CONFIGURATION.md`, `docs/DEVELOPMENT.md` match the change; stale counts and API lists are bugs. User-guide images in `docs/images/` are replaced in the same PR that dates them.
- Player-supplied text (chat lines, character and channel names) is never interpreted as markup or commands.

## Lifecycle

**Kinds of change:**
- **Visible:** users can see it (UI, behaviour, alerts, settings).
- **Code:** anything under `src/`, `tests/` or `scripts/`, or a dependency bump.
- **Non-code:** docs, process, CI workflows.

Steps marked *(visible)* are for visible changes only. Steps 4, 6, 7, 8 and 9 are **never** skipped.

1. **Branch.** `git fetch origin && git switch --no-track -c <prefix><short-name> origin/main`. One change per PR, small enough to merge the same day. Another PR open? Stack on its branch, or leave `changelog/README.md` and `whats-new/features.json` alone until step 7 (see `references/formats.md`).
2. **Before screenshot** *(visible)*. Before any edit: start the dev app from the untouched branch, open the affected view with live clients, and run the capture script into the scratchpad (the release-log folder doesn't exist yet). Not on Windows? **Stop and ask** the user to capture it. Never substitute a mockup, the design handoff or an old `docs/images/` file.
3. **Build with tests.** Put testable logic in `MultiBox.Core`; keep `MultiBox.App` glue thin. Write or update tests, preferring real `samples/` logs, and cover empty, missing or unknown data, many characters (1, 10, more than 10), duplicate lines across clients, and clients opening and closing. A bug fix starts with a test that fails without the fix. *(code)* Run the full test cmd, the build and the artefact build; all must pass. *(non-code)* No new tests are needed, but CI must be green on the PR. Don't commit `bin/`, `obj/` or `dist/`.
4. **Try it for real.**
   - *Code:* run the dev app against live clients through the main path, the edge cases and any setting that toggles it. Say in the release log what you couldn't exercise (e.g. "no web landed during the test"). Off Windows, this blocks: say so, don't claim it.
   - *Non-code:* check the actual output: rendered Markdown, valid JSON, a script dry run (`finalize.ps1 -DryRun`), or the workflow run on the PR.
5. **After screenshot** *(visible)*. Same view, same `-Size`, the **final** UI; retake it if review changes the UI. Replace any `docs/images/` file the change makes stale.
6. **Release-log entry.** Create `changelog/YYYY-MM-DD-<short-name>/README.md` from `references/formats.md`, with `before.png`/`after.png` beside it. Record what was and wasn't run (tests, live clients, anything not exercised). Changing an old feature gets a **new** entry; never rewrite old ones. Commit.
7. **Draft PR, announcement, index.**
   - `git fetch origin && git rebase origin/main`.
   - Add the index row at the top of `changelog/README.md`, then `git push -u origin HEAD`.
   - `gh pr create --draft` with the body from `.github/pull_request_template.md`; this gives you `<N>`.
   - *(visible)* Add the entry at the top of `whats-new/features.json` with `"pr": <N>`, and copy or crop `after.png` to `whats-new/img/pr-<N>-<name>.png`.
   - Visible **fix**, not a feature: add the `semver:patch` label, because an announcement entry defaults to minor. Breaking change (config keys removed, behaviour users rely on gone): `semver:major`. Needs a step before installed copies should update: `deploy:manual`.
   - Commit, push, and pin the PR-body image URLs to that commit's SHA (`https://raw.githubusercontent.com/apettey/multi-box/<sha>/changelog/...`).
   - CI comments "Merging this PR would release vX.Y.Z". Check that it's the version you expect.
8. **Review loop.** Follow `references/review-loop.md`. Spawn a fresh reviewer subagent with the diff, the intent and the checklist. Fix every finding, then re-review the *new* diff with the same reviewer (`SendMessage`). Repeat until a round has zero findings. Every later commit (rebase conflicts, retaken screenshots) gets reviewed too. Write `Review rounds: <n>, last one clean` into the PR body.
9. **Merge and check the release.**
   - `gh pr ready <N>`; CI must be green.
   - Merge only when the user approves: `gh pr merge <N> --merge`.
   - Release happens by itself. Watch it and report the version: see "Checking a release" in `references/versioning-and-release.md`.

## References

- `references/formats.md`: release-log template and index row, announcement JSON, PR body.
- `references/review-loop.md`: reviewer brief ready to paste into a subagent.
- `references/versioning-and-release.md`: bump rules, the release workflow, failed releases, deploy and rollback.
- `references/checklist.md`: copy-paste checklist for the PR or your own tracking.
