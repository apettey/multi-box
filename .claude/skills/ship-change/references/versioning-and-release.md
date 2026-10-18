# Versioning and release

## Every merge is a release

SemVer `MAJOR.MINOR.PATCH`, tag `vX.Y.Z`, worked out automatically when a PR merges
by the versioning code shared with eve-contracts and hangarscope (the `next-version` action in `apettey/eve-contracts`,
`scripts/versioning/bump.py` there; this repo has no copy, see "Shared code" below):

1. A label wins: `semver:major`, `semver:minor` or `semver:patch` (with several labels, the biggest wins).
2. Otherwise, a PR that adds an entry (a new `pr` number) to `whats-new/features.json` is **minor**; editing an
   existing entry is not an addition. A visible *fix* also
   gets an announcement entry, so label it `semver:patch`.
3. Everything else is **patch**: fixes, refactors, CI, dependency bumps (Dependabot included), docs, process, and
   direct pushes to `main`.
4. Major is never automatic.
5. Merges that land while a release run is in progress share the next version, with the biggest bump among them
   (the newest pending run covers every merge since the last published tag; an older pending run is superseded and shows as
   cancelled). Merged one at a time, every merge is its own version.

**No skip.** A docs-only PR still gets a patch release; every merge commit on `main` is in exactly one version and
has its release-log entry. The only other commits are the release job's own `Release vX.Y.Z: changelog and
version` bookkeeping commits, which the bump calculation ignores.

Merge PRs with a **merge commit** (`gh pr merge --merge`). A rebase merge puts several commits on `main` and only one
of them carries the PR's labels; consider turning off rebase merging in the repo settings.

Every PR's CI job summary says "Merging this PR would release vX.Y.Z (minor: …)", and PRs from this repo (not
forks or Dependabot, whose tokens are read-only) also get it as a comment. If it's wrong, fix the label; the preview
updates on `labeled`/`unlabeled`. For a stacked PR the preview is against its base branch, so it's approximate.

| Label | Meaning |
| --- | --- |
| `semver:major` | Breaking change (config keys removed or renamed, a behaviour players rely on removed) |
| `semver:minor` | New feature, even without an announcement entry |
| `semver:patch` | Force patch (visible fix, or a typo fix in the announcement file) |
| `deploy:manual` | Release normally, but publish as a **prerelease** so installed copies don't update until promoted |

Create missing labels once:

```bash
gh label create semver:major --color B60205 --description "Breaking change"
gh label create semver:minor --color 0E8A16 --description "New feature"
gh label create semver:patch --color C2E0C6 --description "Force patch bump"
gh label create deploy:manual --color FBCA04 --description "Release as prerelease; promote by hand to deploy"
```

## The release workflow

`.github/workflows/release.yml` runs on every push to `main`, one run at a time in merge order:

1. **version**: the shared `next-version` action walks the first-parent commits since the newest *published*
   `vX.Y.Z` tag in `main`'s history (its Release is not a draft and has the Velopack `-full.nupkg`), looks up each
   one's merged PR (labels, title) and changed files, and outputs `release`, `finalize`, `version`, `tag`, `bump`,
   `reason`, `prerelease` and the release `notes`. Nothing new (only a bookkeeping commit) → no release. If a PR
   lookup fails, the job fails rather than guess the labels (a missed `deploy:manual` would auto-update everyone).
   A commit that is already tagged reuses its tag (built only if its Release isn't published yet); a commit already
   inside a later tag does nothing. Runs only for `main`.
2. **build**: the shared `apettey/eve-contracts/.github/workflows/build-windows-app.yml` tests, publishes and
   Velopack-packs that version, **then** creates the tag on the built commit and publishes the GitHub Release
   (installer, delta packages, zip). A failed build leaves no tag, and if publishing fails before any Release exists
   the tag is removed again, so every tag has a Release. It refuses to publish a version below one already released.
3. **finalize**: the shared `finalize-release` action writes the Release notes (new announcement entries as
   `- **Title.** text (#N)`, the `changelog/` rows added since the previous release with links pinned to the release
   commit, every merged PR, the compare link), then commits `Release vX.Y.Z: changelog and version` to `main`,
   adding the version section to `CHANGELOG.md` and raising `<Version>` in the App csproj. It also runs when the
   build is skipped because the Release is already published, so any re-run completes a missing finalize; it is
   idempotent. It fails if the Release is a draft or lacks the Velopack packages (no bookkeeping then) or the zip
   (after the bookkeeping, since that version has shipped).

Preview what a release would write, without changing anything: the dry-run commands in `docs/DEVELOPMENT.md`
("Releasing").

### Shared code

The rules, the notes and the `CHANGELOG.md` bookkeeping are one implementation in `apettey/eve-contracts`
(`scripts/versioning/`, Python, with the merged test suite of all three repos), exposed as composite actions called
`@main`: `next-version` (release runs and the PR preview), `finalize-release` and `validate-features`. This repo's
differences are inputs in `release.yml` and `ci.yml`: `bookkeeping-paths: CHANGELOG.md,src/MultiBox.App/MultiBox.App.csproj`,
`app-name: MultiBox`, `announcement-notes: list`, and `changelog-file`, `version-file` and `branch: main` on
finalize. Changing a rule is a PR in `apettey/eve-contracts`, and it changes all three apps' releases.

## Checking a release

After the merge:

```bash
id=""
for i in $(seq 1 12); do
  id=$(gh run list --workflow release.yml --branch main --limit 1 --json databaseId,headSha \
       -q ".[] | select(.headSha==\"$(git rev-parse origin/main)\") | .databaseId")
  [ -n "$id" ] && break; sleep 5; git fetch -q origin
done
[ -n "$id" ] && gh run watch "$id" --exit-status
gh release view --json tagName,isPrerelease,assets -q '{tag: .tagName, pre: .isPrerelease, assets: [.assets[].name]}'
```

The Release must list `MultiBox-win-Setup.exe`, the `.nupkg` and the zip. Report the version to the user.

### If the release run fails

- **version failed** (usually a `gh` lookup error): `gh run rerun <id>`. Nothing was built or tagged.
- **build failed before publishing** (tests, publish, pack, or the Velopack upload itself): no tag is left behind.
  Re-running an old failed build after a newer version has shipped is safe: the shared build refuses the stale
  version and tells you to start a fresh run (`gh workflow run release.yml --ref main`).
  Flaky (runner, network, quota): `gh run rerun <id>`. Code problem: fix it in a `fix/` PR; that merge's run picks
  up both changes, so they share one version. If the build log warns it could not remove the tag, delete it
  (`gh api -X DELETE repos/apettey/multi-box/git/refs/tags/vX.Y.Z`) before the next run.
- **build failed after the Velopack Release was published** (e.g. the zip upload): the version has shipped and the tag
  stays. Don't re-run the failed build job (`vpk pack` would find the same version already released). Attach the
  missing zip by hand (`gh release upload vX.Y.Z <zip>`) from the run's artefacts, which are kept 7 days; after that,
  rebuild it from the tag (`git checkout vX.Y.Z`, `dotnet publish src/MultiBox.App/MultiBox.App.csproj -c Release -r
  win-x64 --self-contained true -p:Version=X.Y.Z -o publish`, zip the folder's contents as
  `MultiBox-X.Y.Z-win-x64.zip`). Then `gh run rerun <id>` (**all** jobs, not
  `--failed`): the version job sees the published Release, skips the build and runs finalize. Without the zip,
  finalize still writes the notes and `CHANGELOG.md`, then fails to flag the missing zip (and keeps failing on
  every re-run until the zip is attached). Skipping this leaves the shipped
  version with no `CHANGELOG.md` section and only GitHub's generated notes; the next merge's run won't add it.
- **finalize failed**: the Release is out; only the notes and bookkeeping are missing. Re-run the run (failed jobs
  or all jobs both work: with the Release already there, the build is skipped and finalize runs).

Manual fallback (workflow broken, needs a release now): `gh workflow run release.yml --ref main`.

## Deploy

Installed copies check GitHub Releases 30 s after launch and then hourly (Velopack, stable releases only), so
**publishing the Release is the deploy**. With `deploy:manual` the Release is a prerelease: tagged and downloadable,
but no installed copy updates. To deploy it, do the manual step (say in the PR what it is), then
`gh release edit vX.Y.Z --prerelease=false`.

Rollback means releasing the previous code as a new, higher version (`git revert` the merge in a PR); installed
copies only pick up versions newer than their own.

## Notes

- `build-windows-app.yml` lives in `apettey/eve-contracts` and is shared with eve-contracts and hangarscope (`@main`).
  The `version` / `release-tag` / `prerelease` inputs are optional; without them it behaves as before (tag-ref
  builds publish).
- Tags created with `GITHUB_TOKEN` don't trigger workflows, which is why the build is called from `release.yml`
  rather than triggered by the tag.
- If `main` gets branch protection, the finalize push needs a bypass for `github-actions[bot]`.
