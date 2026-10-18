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
conflict there. Either stack the PR on the newest open PR's branch, or leave both shared files untouched until the
rebase right before `gh pr create`.

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
| `images` | Optional, 1–2 `{ src, caption }`; the caption says where in the app the screenshot is. The file must exist. |

Check it before pushing: `pwsh scripts/release/validate-announcements.ps1` (CI runs the same check).

## PR body

Use `.github/pull_request_template.md`. Fill in:
- **What:** what changed and why, in plain language.
- **Screenshots:** before/after images, linked to raw URLs **pinned to a commit SHA**:
  `https://raw.githubusercontent.com/apettey/multi-box/<sha>/changelog/<folder>/after.png`.
- Link to the release-log entry, a test summary (`N/N pass`, what was run against live clients), and
  `Review rounds: <n>, last one clean`.
- **EULA boundary:** "unchanged", or what widened and why (see the settings-sync PR for the shape).
- Anything deliberately out of scope ("Not in this PR").
