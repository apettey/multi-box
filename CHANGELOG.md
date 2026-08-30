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
- **Cards only for characters whose client is open.** A card per log file meant every alt that
  undocked today kept a slot for the rest of the session, because log files outlive the
  client. The header reports how many known characters are currently closed. Toggle it with
  **Open only** if you want the old behaviour.
- **A cards-per-squad slider** in the header, 1 to 20. Squads are consecutive slices of the
  character order, so lowering it moves the overflow onto the next squad tab.
- **A character ordering window** (**≡ ORDER**) for fleets too large to arrange by dragging
  cards: move characters up and down, see which squad each will land in, and sort every open
  client to the top in one action.
- **A CLEAR button on the unified comms panel.** Empties the view only — the session keeps its
  history and nothing on disk is touched.
- **FOLLOW on the comms panel**, keeping the newest message in view as it arrives. Scrolling
  away turns it off so you can read back without the panel dragging you forward; scrolling
  back to the top turns it on again.
- **Chat older than thirty minutes is dropped** from the panel and from the dedup history
  behind it. Configurable with `ChatRetentionMinutes`; zero keeps everything up to the
  scrollback limit.
- **TOP THREAT on every card: what is hitting that character hardest.** Incoming damage is now
  split by who is dealing it, over the same rolling window as the headline figure, and the
  worst offender is named on the card with its share of the damage. Players are identified by
  hull and pilot (`Ares "Vint-1"`), NPCs by their own name. Hovering lists the top three, so a
  single battleship is never mistaken for a swarm of frigates — the total reads the same for
  both, and they call for opposite decisions.
- `AGENTS.md` and this changelog.

### Changed

- **Upgraded from .NET 8 to .NET 10** (`net10.0`, `net10.0-windows`). The self-contained
  build no longer requires a runtime install; the framework-dependent build now needs the
  .NET 10 Desktop Runtime.
- The dashboard opens centred on a single monitor rather than at a fixed position, and
  remembers where it was left.
- The comms panel is populated from the session's existing history at startup instead of
  filling only as new lines arrive.
- **The card grid now fits the space it is given.** It picks the column count whose cells come
  out nearest square for the area available, rather than following a fixed formula, so two
  cards sit side by side on a wide monitor and stack on a tall one. The old rule always
  stacked them, leaving a 1440p monitor almost entirely empty. It also stopped assuming two
  rows, which was simply wrong past ten cards; twenty now lay out 5x4.
- **Client previews grow with the card.** The preview was capped at 440px wide, so a two-card
  squad showed a small thumbnail marooned in an otherwise empty card. It now takes a share of
  whatever the fixed rows leave, staying 16:9, bounded by width or by height depending on
  which runs out first.
- **The comms panel is virtualised and scrolls by pixel.** It previously measured every row in
  the scrollback at once, which is what made scrolling stutter as the backlog grew. Rows are
  now realised only as they come into view, and scrolling moves by pixel rather than by row so
  wrapped messages of different heights do not jump.
- **The per-character combat log flows into columns.** A card is several times wider than a
  log line, so a single column left most of the card empty and showed about a dozen rows where
  the same area holds four times as many. Rows now run top to bottom and then into the next
  column, so a wide card fills with history instead of whitespace.

### Fixed

- **The self-contained build crashed a few seconds after launch, without ever showing a
  window.** WPF's native libraries cannot be loaded from inside a single-file bundle, and
  without `IncludeNativeLibrariesForSelfExtract` they were neither bundled for extraction nor
  written next to the executable, so the app died with `DllNotFoundException` in
  `SetWindowLongPtrWndProc`.
- **The Cycle Groups window failed to open**, because its XAML referenced a converter defined
  later in the same resource dictionary and `StaticResource` cannot resolve forward
  references.
- **A cycle group resumed mid-ring after you had used a different group.** Cycling group 1 to
  its second member, switching to group 2, then pressing group 1 again continued from the
  third member instead of starting over. Position is now remembered only for the group used
  last, so returning to a group restarts it — the same keypress no longer lands on a
  different client depending on history you cannot see.
- **Pressing a group's backward hotkey first landed one short of the end** of the ring rather
  than on its last member.
- **The DPS, reps, cap and neut counters read zero during a fight** while the per-character
  combat log filled normally. EVE buffers its gamelog and flushes it in bursts, so a line can
  arrive well after the moment it describes. The rolling windows were anchored to
  `DateTime.UtcNow`, so whenever that flush lag exceeded the ten-second stat window every
  sample was discarded the instant it arrived. Windows and EWAR hold times are now read on
  the log's own clock, carried forward by however long we have been waiting, which keeps them
  aligned with the data and still lets the readings decay once the shooting stops.

  Measured against a live fight: 148 events arrived with a median lag of **27.9 seconds**
  (27.5 to 28.2), and **all 148** were already older than both the ten-second stat window and
  the twelve-second EWAR hold by the time they could be read.

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
- **TOP THREAT only counts damage the log attributes.** Every damage line names its source, so
  in practice this is all of it; damage that arrived unattributed would still be in the
  headline figure but is deliberately not blamed on an invented "unknown" attacker, which
  would otherwise top the list.
- Webs, target painters, sensor dampeners and tracking disruptors are still undetectable:
  EVE writes no game-log line when they are applied. The web *alert* fires only for a web the
  log actually reports.

## [0.1.0] - 2026-08-28

### Added

- Initial release: log discovery and tailing, combat and chat parsing, cross-client
  deduplication, per-character DPS/reps/neut statistics, EWAR state tracking with audio
  alerts, a unified read-only chat view, the `multibox-replay` console diagnostics tool
  (`doctor`, `replay`, `chat`), and an EULA compliance audit enforced by tests.
