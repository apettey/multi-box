# Changelog

All notable changes to MultiBox are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

See [AGENTS.md](AGENTS.md) for the rules on keeping this file, the documentation and the
screenshots current.

## [Unreleased]

### Added

- **Fleet Command dashboard.** The tile dashboard is replaced by the full Fleet Command
  layout: a header carrying fleet-wide incoming damage, reps, neut pressure and an EWAR alert
  count; a character grid that reflows from 1 to 10 cards; and a 440px unified comms panel.
  Cards can be dragged to reorder, and the order is remembered.
- **Live client thumbnails in every card.** Each card shows its client, drawn by the Windows
  compositor through the DWM thumbnail API — the same mechanism eve-o-preview uses. Clicking
  a thumbnail brings that client to the foreground.
- **Fast Screen Switcher (cycle groups).** Up to five groups, each with forward and backward
  global hotkeys and an ordered member list. A hotkey raises the next member's client,
  wrapping at the end and skipping clients that are not running. Configured from the
  ⇄ CYCLE GROUPS window, and importable from an existing `EVE-O Preview.json`, including its
  tie-breaking rules for members sharing a position. Membership shows on each card as
  `G1·3` tags.
- **Per-character combat log** on every card, colour-coded by what happened: heavy hits,
  chip damage, reps, cap, neuts and EWAR.
- **Dual sparklines** behind the incoming-damage figure — incoming damage in red against reps
  received in green, on a shared vertical scale so the two can be compared — plus
  `▲ EXCEEDS REPS` and `● REPS HOLDING` state tags.
- **EWAR badges naming the aggressor**, as `SCRAM Ares "Vint-1"`. Webs flash rather than
  merely colouring, being the effect you can least afford to miss.
- **Spoken web warnings.** A new web is announced as "Web detected. *character*", naming the
  pilot, which a tone cannot do across ten clients. Toggleable.
- **Cap transfer tracking** (`CAP XFER`). See *Known limitations* below.
- **Squad tabs** for fleets larger than ten characters.
- **Channel filter chips** in the comms panel: `ALL · CORP · ALLIANCE · LOCAL` pinned first,
  every other channel alphabetically after them.
- Fleet roles per character (`DPS`, `LOGI`, `CMD`, `EWAR`) shown as a chip on each card, set
  through `CharacterRoles` in the config.
- `AGENTS.md` and this changelog.

### Changed

- **Upgraded from .NET 8 to .NET 10** (`net10.0`, `net10.0-windows`). The self-contained
  build no longer requires a runtime install; the framework-dependent build now needs the
  .NET 10 Desktop Runtime.
- The dashboard opens centred on a single monitor rather than at a fixed position, and
  remembers where it was left.
- The comms panel is populated from the session's existing history at startup instead of
  filling only as new lines arrive.

### Fixed

- **The self-contained build crashed a few seconds after launch, without ever showing a
  window.** WPF's native libraries cannot be loaded from inside a single-file bundle, and
  without `IncludeNativeLibrariesForSelfExtract` they were neither bundled for extraction nor
  written next to the executable, so the app died with `DllNotFoundException` in
  `SetWindowLongPtrWndProc`.
- **The Cycle Groups window failed to open**, because its XAML referenced a converter defined
  later in the same resource dictionary and `StaticResource` cannot resolve forward
  references.

### Security

- The native API surface grew from six calls to twelve, each a deliberate, documented
  decision recorded in [docs/EULA-COMPLIANCE.md](docs/EULA-COMPLIANCE.md):
  - `DwmRegisterThumbnail`, `DwmUpdateThumbnailProperties`, `DwmUnregisterThumbnail`,
    `DwmQueryThumbnailSourceSize` — the compositor draws the previews and returns no pixel
    data to this process, so displaying a client is still not reading one.
  - `SetForegroundWindow`, `ShowWindow`, `IsIconic` — raise the single client whose thumbnail
    or hotkey was used. This is the first capability in the project that acts on a game
    window rather than observing one, and it matches eve-o-preview's long-standing
    behaviour.
  - `RegisterHotKey`, `UnregisterHotKey` — reserve specific key combinations for the
    switcher. Unlike a keyboard hook these observe nothing else, which is why
    `SetWindowsHookEx` remains banned.
- Every input-synthesis and memory-access API remains prohibited and enforced by
  `EulaComplianceTests`. Input broadcasting is still impossible.

### Known limitations

- `CAP XFER` is **unverified against a captured log**. No file in `samples/` contains a
  capacitor transfer, because nobody in the recorded session flew a transmitter. The pattern
  is written from EVE's log format documentation and accepts both of the client's spellings;
  every other pattern in the parser is derived from a real log.
- Webs, target painters, sensor dampeners and tracking disruptors are still undetectable:
  EVE writes no game-log line when they are applied. The web *alert* fires only for a web the
  log actually reports.

## [0.1.0] - 2026-08-28

### Added

- Initial release: log discovery and tailing, combat and chat parsing, cross-client
  deduplication, per-character DPS/reps/neut statistics, EWAR state tracking with audio
  alerts, a unified read-only chat view, the `multibox-replay` console diagnostics tool
  (`doctor`, `replay`, `chat`), and an EULA compliance audit enforced by tests.
