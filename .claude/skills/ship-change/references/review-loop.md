# Review loop

The PR stays a **draft** and is not merged until a round comes back with **zero findings**. The loop runs after the
draft PR exists (SKILL.md step 7), so the announcement entry, index row and PR body are reviewed too. CI can't check
this; the round count in the PR body is the evidence.

## Procedure

1. Commit and push everything (code, tests, release-log entry, screenshots, announcement entry and image).
2. Spawn a **fresh** reviewer subagent with the `Agent` tool (`subagent_type: "general-purpose"`: it needs Bash for
   `git diff`). It must not be the agent that wrote the change. Use the brief below and keep the agent ID it returns.
3. For each finding: fix it, or write down why it isn't a problem. Commit.
4. Send the reviewer the **new** diff with `SendMessage` to the same agent ID (load it with `ToolSearch`
   `select:SendMessage` if deferred), listing what you fixed and what you rejected and why, and ask for another round.
   If that agent can't be resumed, spawn a new one with the full brief and the earlier findings.
5. Repeat until a round reports `NO FINDINGS`. Record `Review rounds: <n>, last one clean` in the PR body.

A finding you reject still counts. The round isn't clean until the reviewer accepts the explanation.

## Reviewer brief (paste into the subagent prompt; fill in the angle brackets)

```text
You are reviewing a change to the multi-box repo (the current checkout): MultiBox, a .NET 10 WPF
Windows dashboard for multiboxing EVE Online that reads game and chat logs from disk. You did not
write this change. Be strict and concrete.

Intent of the change: <what it is meant to do, and why>
Visible to users: <yes/no>
Release-log entry: changelog/<folder>/README.md
Draft PR: #<N> (read the body with: gh pr view <N>)

Get the diff with: git diff origin/main...HEAD   (and `git status` for anything uncommitted)
Read AGENTS.md first, then whatever surrounding code you need. Do not edit files.

Check:
- Correctness: does it do what the intent says, including edge cases (no logs, unknown or malformed
  lines, 1 / 10 / more than 10 characters, the same line seen by several clients, clients opening and
  closing, a character with no log yet)? Any regression in nearby behaviour?
- Tests: is the new behaviour covered in tests/MultiBox.Core.Tests, preferably against samples/?
  Would the tests fail without the change? Is a bug fix accompanied by a test that reproduces it?
- Evidence: does the release-log entry exist, match the diff and say what wasn't exercised? (Visible)
  are before.png/after.png present, from the real app, and does after.png show the final UI? Is there
  an entry in whats-new/features.json with "pr": <N>, written for players and saying where to find the
  feature, with its image in whats-new/img/? Is the changelog/README.md index row at the top? Are
  docs/images/ files that the change dated replaced? Does the PR body have before/after (SHA-pinned),
  tests, the release-log link and the EULA line? A visible fix carries the semver:patch label; a
  breaking change semver:major. CHANGELOG.md and the csproj <Version> are untouched (the release job
  writes them).
- EULA boundary: no input synthesis (SendInput, keybd_event, PostMessage, SendMessage, hooks), no
  memory reads, no screen scraping of EVE. Any new native call, file write or outbound host has an
  allow-list entry with a reason in EulaComplianceTests, and docs/EULA-COMPLIANCE.md and README.md
  are updated. No new carve-outs.
- Parsing: every new GamelogParser/chat pattern is backed by a line in samples/, or is marked
  unverified in a comment and in the docs.
- Architecture: MultiBox.Core has no third-party dependencies and stays platform-neutral; Windows-only
  code is in MultiBox.App, which stays thin.
- Docs: README.md, docs/USER-GUIDE.md, docs/ARCHITECTURE.md, docs/CONFIGURATION.md and
  docs/DEVELOPMENT.md still tell the truth (test counts, version numbers, API lists, config keys).
- Security: chat text and character/channel names never interpreted as markup or commands; no
  secrets committed; GitHub workflow inputs from PRs passed through env, not interpolated into run:.
- Scope: one change; no unrelated reformatting or drive-by edits.

Report each finding as:
  <file>:<line> — what's wrong — why it matters
If there is nothing to report, reply with exactly: NO FINDINGS
On later rounds, first say whether each earlier finding is resolved, then list any new ones.
```
