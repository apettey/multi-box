# Working agreements for agents

Rules for any AI agent (Claude Code or otherwise) making changes in this repository. They
exist because this project's value is in being *auditable* — the EULA boundary, the parsing
claims, and the screenshots are all promises to a reader who cannot run the code.

---

## 1. Every change is recorded in the changelog

`CHANGELOG.md` at the repo root is the record. It follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/).

**Before committing any change to behaviour, features, dependencies or build,** add an entry
under `## [Unreleased]` in the right group:

| Group | Use for |
|---|---|
| `Added` | new features |
| `Changed` | changes to existing behaviour |
| `Deprecated` | features about to be removed |
| `Removed` | features taken out |
| `Fixed` | bug fixes |
| `Security` | anything touching the EULA boundary or the native API surface |

Write entries for the person running the app, not for the diff. "Fixed a crash on startup in
the self-contained build" beats "added `IncludeNativeLibrariesForSelfExtract`" — put the
mechanism in the second half of the sentence if it helps.

Cutting a release means renaming `[Unreleased]` to `[x.y.z] - YYYY-MM-DD` and opening a fresh
`[Unreleased]`. Git tags are `vX.Y.Z`.

Changes that need **no** entry: typo fixes in comments, whitespace, reformatting, and edits
to this file.

## 2. Every change updates the documentation

Documentation is part of the change, not follow-up work. Before a change is done, check each
of these and update whatever the change made untrue:

- `README.md` — what it is, what it detects, requirements, build, first run
- `docs/USER-GUIDE.md` — how a player actually uses the feature
- `docs/ARCHITECTURE.md` — project layout, target frameworks, data flow
- `docs/EULA-COMPLIANCE.md` — **mandatory** whenever the native API surface changes
- `docs/DEVELOPMENT.md` — prerequisites, build and test commands

A claim in the docs that the code no longer honours is worse than no claim. Search the docs
for counts, version numbers and API lists that your change invalidates — `62 tests`,
`net8.0`, `six read-only window queries` have all gone stale here before.

## 3. UI changes come with screenshots

Any change to what the app looks like means regenerating the affected images in
`docs/images/` and referencing them from the user guide.

Screenshots are captured from the **real app running against real clients**, never mocked.
Capture at the default window size on a single monitor. Current set:

| File | View |
|---|---|
| `docs/images/dashboard.png` | the Fleet Command dashboard |
| `docs/images/cycle-groups.png` | the Fast Screen Switcher config window |

A screenshot showing a stale layout is a bug in the documentation. Replace it in the same
commit as the change that dated it.

## 4. The EULA boundary is not casually widened

`docs/EULA-COMPLIANCE.md` and `tests/MultiBox.Core.Tests/EulaComplianceTests.cs` encode
promises about what this program can and cannot do. The test holds an **allow-list** of
native calls; anything outside it fails the build.

If a change needs a new native call:

1. Decide deliberately whether it belongs, and say so in the commit message.
2. Add it to the allow-list **with a comment explaining why it is safe**.
3. Update the API table and the surrounding prose in `docs/EULA-COMPLIANCE.md`.
4. Update the corresponding paragraph in `README.md`.

Never add a file to the prohibited-name carve-out list to get a build green. If a comment
trips the check, reword the comment.

Input synthesis (`SendInput`, `keybd_event`, `PostMessage`, `SendMessage`, hooks) and memory
access stay banned. Input broadcasting is the thing EVE's EULA actually prohibits.

## 5. Claims about parsing must be backed by a real log

Every pattern in `GamelogParser` is derived from a captured log in `samples/`. If you add one
that is **not** — because no sample contains that line — say so in a comment on the pattern
and in the docs, as `CapTransfer` does. Do not present an unverified pattern as verified.

## 6. Tests

`dotnet test` must pass before committing. New behaviour in `MultiBox.Core` needs tests;
prefer asserting against `samples/` over hand-written fixtures.

`MultiBox.Core` has **no third-party dependencies** and targets a platform-neutral TFM so the
suite runs anywhere. Keep it that way — Windows-only code belongs in `MultiBox.App`.

## 7. Commits

- Explain *why*, not just what. The mechanism is visible in the diff; the reason is not.
- Never commit directly to `main` — branch, then open a PR.
- Do not commit `dist/`, `bin/`, `obj/` (already ignored) or anything from `%APPDATA%`.
- Files are **CRLF**. Note that `sed -i` under Git Bash on Windows strips carriage returns and
  turns a one-line edit into a whole-file diff; use an editor that preserves them.
