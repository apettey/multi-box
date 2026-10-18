# Checklist

```markdown
### Start
- [ ] Branched from latest main with --no-track (or stacked on the newest open PR): feature/|fix/|chore/<name>
- [ ] Visible change: "before" screenshot taken before the first edit (real app, live clients, capture-window.ps1 -Size 1600x1000)

### Build
- [ ] Tests added/updated, against samples/ where possible (a failing test first for bug fixes)
- [ ] Code: `dotnet test tests/MultiBox.Core.Tests/MultiBox.Core.Tests.csproj -c Release` passes locally
- [ ] Code: `dotnet build MultiBox.sln -c Release` and `.\build.ps1 -SelfContained` succeed
- [ ] Code: tried in the running app against live clients, edge cases included (what wasn't exercised noted). Non-code: output checked, CI green
- [ ] EULA boundary unchanged, or allow-list + EULA-COMPLIANCE.md + README updated with reasons
- [ ] Docs updated where the change made them untrue
- [ ] Visible change: "after" screenshot of the final version; stale docs/images/ replaced

### Document
- [ ] changelog/YYYY-MM-DD-<name>/README.md (+ before/after), saying what was and wasn't run

### Draft PR
- [ ] Rebased on origin/main; index row at the top of changelog/README.md
- [ ] Draft PR opened; visible change: whats-new/features.json entry with pr = <N> + whats-new/img/pr-<N>-*.png
- [ ] PR body: what/why, before/after (SHA-pinned), release-log link, tests, EULA line
- [ ] semver:* label if the default is wrong (visible fix = semver:patch); deploy:manual if a manual step is needed
- [ ] CI's "would release vX.Y.Z" is the version you expect

### Review
- [ ] Fresh reviewer subagent given the diff, intent, PR number and checklist
- [ ] Every finding fixed or answered; re-reviewed on the new diff
- [ ] Repeated until a round had zero findings; round count in the PR body

### Release
- [ ] PR marked ready, CI green, merged with a merge commit (user approved)
- [ ] Release run green: vX.Y.Z tagged, GitHub Release has installer + packages + zip, notes written
- [ ] Deploy: stable release (auto-update) or prerelease held on purpose (deploy:manual)
```
