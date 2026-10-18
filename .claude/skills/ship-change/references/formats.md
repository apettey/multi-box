# Formats

## Release-log entry

Folder: `changelog/YYYY-MM-DD-<short-name>/` (lower-case, hyphenated), with `README.md`, `before.png` and `after.png`
(more images if needed, e.g. `after-sync-window.png`). CI fails a PR (other than Dependabot's) that adds no such
folder.

```markdown
# <Change name>

**Date:** YYYY-MM-DD

## Description

One to three sentences: what changed and why.

## Before / After

| Before | After |
| --- | --- |
| ![before](before.png) | ![after](after.png) |

<!-- Nothing visible? Delete the table and describe the change under "Change" instead. -->

## Change

What is different now: files/modules added or changed, new config keys, new native calls / file writes / outbound
hosts ("none"; if any, the EulaComplianceTests allow-list entry), new parser patterns and the sample log behind
them, tests added, and what was not exercised against live clients.
```

Keep it short; it is a log, not documentation (feature docs live in `README.md` and `docs/USER-GUIDE.md`).

### Index row

Add at the **top** of the table in `changelog/README.md`, after rebasing on the latest `main`:

```markdown
| 2026-10-17 | [Settings sync](2026-10-17-settings-sync/README.md) | SYNC LAYOUT copies one character's window layout to your other characters |
```

Summaries of internal changes start with `Internal:`. The summary goes into the GitHub Release notes and
`CHANGELOG.md` as written, so write it for players.

**Parallel PRs:** the index and `whats-new/features.json` both take a row at the top, so PRs branched in parallel
conflict there. Either stack the PR on the newest open PR's branch (merge the stack bottom-up, each PR into `main`
after `gh pr edit <N> --base main`; merging a PR into another PR's branch loses its labels for versioning), or leave both
shared files untouched until the rebase right before `gh pr create`.

## Announcement entry (visible changes only)

`whats-new/features.json`, newest first:

```json
{
  "features": [
    {
      "pr": 42,
      "date": "2026-10-17",
      "title": "Settings sync",
      "description": "The SYNC LAYOUT button in the header copies one character's window layout, chat windows and overview to your other characters.\n\nIt only runs while every EVE client is closed, and backs up the files first.",
      "images": [
        { "src": "whats-new/img/pr-42-settings-sync.png", "caption": "The SYNC LAYOUT window, opened from the header" }
      ]
    }
  ]
}
```

| Field | Rule |
| --- | --- |
| `pr` | Integer, unique: this PR's number (so open the PR first). |
| `date` | `YYYY-MM-DD` |
| `title` | The feature's name. |
| `description` | 1–3 sentences of plain text (no HTML), in the player's words: what it does and **where to find it**. `\n\n` starts a new paragraph. |
| `images` | Omit it, or 1–2 objects with exactly `src` and `caption`. `src` starts with `whats-new/img/` and the file must exist; the caption says where in the app the screenshot is. |

No other fields. CI checks all of this with the shared validator from `apettey/eve-contracts`, and also that every
entry the PR adds uses its number. Check it before pushing, with `apettey/eve-contracts` checked out next to this
repo: `python3 ../eve-contracts/scripts/versioning/validate_features.py --base-ref origin/main --pr <N>`.

## PR body

Use `.github/pull_request_template.md`. Fill in:
- **What:** what changed and why, in plain language.
- **Screenshots:** before/after images, linked to raw URLs **pinned to a commit SHA**:
  `https://raw.githubusercontent.com/apettey/multi-box/<sha>/changelog/<folder>/after.png`.
- Link to the release-log entry, a test summary (`N/N pass`, what was run against live clients), and
  `Review rounds: <n>, last one clean`.
- **EULA boundary:** "unchanged", or what widened and why (see the settings-sync PR for the shape).
- Anything deliberately out of scope ("Not in this PR").
