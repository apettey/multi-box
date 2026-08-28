# Architecture

## Design constraints

Three rules shaped every decision. They are not negotiable, and any future change should be
checked against them.

1. **Logs and ESI only.** No reading EVE client memory, no injected input, no automation.
   This is an EULA constraint, not a preference. The only Windows APIs used are read-only
   window queries.
2. **Windows is the deployment target**, because that is where the game runs. But the
   parsing and state logic must be testable off-Windows, or development is miserable.
3. **The same event arrives many times.** With four clients running, one game event lands in
   up to four log files. Deduplication is not a feature bolted on the side; it is the
   central abstraction.

Rule 2 produced the project split: `MultiBox.Core` targets `net8.0` and is fully portable;
only `MultiBox.App` targets `net8.0-windows`. Consequently the full test suite runs on
macOS or Linux against real captured logs, and the Windows build can even be *published*
from a Mac via `-p:EnableWindowsTargeting=true`.

---

## Projects

| Project | Target | Role |
|---|---|---|
| `src/MultiBox.Core` | `net8.0` | discovery, tailing, parsing, dedup, stats, config, ESI |
| `src/MultiBox.App` | `net8.0-windows` | WPF dashboard, audio alerts, window interop |
| `src/MultiBox.Replay` | `net8.0` | console diagnostics (`doctor`, `replay`, `chat`) |
| `tests/MultiBox.Core.Tests` | `net8.0` | 39 xunit tests, mostly against real logs |

`MultiBox.Core` has **no third-party dependencies** — `System.Text.Json` and
`System.Net.Http` only.

---

## Data flow

```
                    ┌──────────────── MultiBoxSession (orchestrator) ─────────────────┐
                    │                                                                 │
Gamelogs/*.txt ──► LogDirectory ──► LogTailer ──► GamelogParser ──► dedup ──► CharacterMonitor ──► CharacterViewModel
                    │  (discover)     (follow)      (interpret)      (once)     (accumulate)          (render)
                    │                                                  │                │
                    │                                                  │                └─► EwarStateTracker ──► AlertPlayer
Chatlogs/*.txt ──► LogDirectory ──► LogTailer ──► ChatlogParser ──► ChatAggregator ─────────────────► unified chat pane
                    │                                                (dedup + witnesses)
                    └─────────────────────────────────────────────────────────────────┘
```

### Discovery — `LogDirectory`

Probes candidate roots (including the OneDrive-redirected `Documents`), then groups files by
`(characterId, channel)` and keeps the newest of each. EVE opens a new file on every session
change, so "the current log" is always "the most recent per character".

### Tailing — `LogTailer`

Opens with `FileShare.ReadWrite | FileShare.Delete` — mandatory, because EVE holds a write
lock and any stricter share mode throws immediately.

Returns only **complete lines**; a trailing partial line is buffered until its newline
arrives, so a half-flushed line is never parsed. Encoding is auto-detected from the BOM,
which is how one class handles UTF-8 gamelogs and UTF-16LE chatlogs transparently.

New tailers start at **end of file**. Replaying history on startup would fire alerts for
tackle that landed before the app was running.

### Parsing — `GamelogParser`, `ChatlogParser`

`EveMarkup.Strip()` removes the pseudo-HTML first; all patterns then run against plain text.
See [LOG-FORMATS.md](LOG-FORMATS.md) for every shape.

Pattern order in `ParseCombatBody` is deliberate and load-bearing: the broad
`<entity> jammed` pattern is tested **last**, after every specific shape has had first
refusal, because it would otherwise swallow them.

Each parser instance is bound to one `listener` — the character who owns the file — which is
what lets `"you"` resolve to a real name.

### Deduplication

Two independent dedup layers, both keyed on the identity of the underlying event rather than
the text of the line.

```csharp
GameLogEvent.DedupKey() => $"{Timestamp:yyyyMMddHHmmss}|{Kind}|{Ewar}|{Victim}|{Counterparty?.Name}|{Amount}|{Module}"
ChatMessage.DedupKey()  => $"{Timestamp:yyyyMMddHHmmss}|{Channel}|{Sender}|{Text}"
```

Combat dedup lives in `MultiBoxSession` behind a bounded `HashSet` (20,000 keys, FIFO
eviction) so a long session cannot grow it without limit. Chat dedup lives in
`ChatAggregator`, which additionally records **which characters saw each message** — that is
the "seen by N" column, and it distinguishes "everyone got this" from "only one client was
in the channel".

Measured on the captured session: 969 of 14,383 parsed combat events (6.7%) were duplicates;
50 warp-scramble lines represented 19 real scrambles.

### Attribution — `CharacterMonitor`

The session broadcasts every deduplicated event to every monitor, so each monitor decides
whether an event is its own:

```csharp
var isMyAction = e.Listener == Name;   // outgoing: only the actor's own log records it
var aboutMe    = e.Victim   == Name;   // incoming: the victim is named explicitly
if (!isMyAction && !aboutMe) return;
```

Outgoing stats are gated on `isMyAction`, incoming on `aboutMe`. This asymmetry is a direct
consequence of how EVE writes logs (see LOG-FORMATS §7) and it is easy to get wrong: an
early version credited outgoing damage to every monitor, which silently showed the fleet's
combined DPS on all four tiles. `AttributionTests` exists to prevent a regression.

A useful side effect: when another client witnesses an effect on you, the line names your
**ship**, so `VictimShip` populates the tile's ship label without any ESI call.

### Statistics — `RollingWindow`

Trailing-window sum ÷ window length, the same reading PyEveLiveDPS gives. `now` is a
**parameter**, never `DateTime.UtcNow` read internally — which is precisely what allows a
saved log to be replayed and produce numbers identical to a live session, and lets the tests
assert on real data deterministically.

### EWAR state — `EwarStateTracker`

The important subtlety: **EVE logs application, never release.** There is no "you are no
longer scrambled" line. So the only honest model is a decaying one:

- Each incoming EWAR line **refreshes** a per-type timer.
- An effect **lapses** if nothing refreshes it within `EwarHoldSeconds` (default 12).
- `EwarApplied` fires only on the clear→applied transition, so a scrambler cycling every few
  seconds produces **one** alert, not a stream.

This is an approximation, and it is documented as one. A 12s hold sits just above a typical
activation cycle: long enough to stay lit while genuinely tackled, short enough to clear
soon after release.

---

## The Windows layer

### Client identification — `EveClientLocator`

EVE sets each window's title to `EVE - <Character Name>` once a character is logged in. That
string is the bridge between the log world and the window world:

```
log banner "Listener: Commander Tyrael"  ⟷  window title "EVE - Commander Tyrael"
```

This is the same mechanism eve-o-preview uses, which is what makes the two tools
interoperable and why the config keys match. The character-select screen shows a bare `EVE`
title and is skipped.

Only `EnumWindows`, `GetWindowText`, `GetWindowRect`, `GetForegroundWindow` and
`SetWindowPos` are imported. Nothing writes to the game.

### ESI — `EsiClient`

Deliberately a **verification step, not a dependency**. Character ids come free in log
filenames and names come free in log banners, so ESI is used only to confirm the two agree
(`POST /universe/ids/`) and to fetch public details for display. Both endpoints are public:
no OAuth, no scopes, no token that could touch a running client.

### Alerts — `ToneGenerator`, `AlertPlayer`

Tones are **synthesised into PCM WAV in memory** at runtime, so there are no audio assets to
ship, lose or path-configure. Each EWAR type gets a deliberately different shape rather than
a different pitch of the same beep, because pitch alone is hard to distinguish while reading
something else:

| Effect | Shape | Rationale |
|---|---|---|
| Jam | warble (18 Hz vibrato) | "my sensors are wrong" |
| Warp scramble | falling pitch | "held down" |
| Warp disruption | two pips | distinct rhythm, not just pitch |
| Energy neutraliser | low throb | bottom of the range, draining |

Every alert is rate-limited per `(character, effect)` by `CooldownSeconds` (default 8).

### UI

MVVM without a framework: a hand-rolled `ObservableObject`, `MainViewModel` owning the
session, `CharacterViewModel` per pilot, `ChatRowViewModel` per unified message.

Two timers:

| Timer | Interval | Work |
|---|---|---|
| poll | 250 ms | read appended lines, update stats, expire EWAR |
| rescan | 10 s | look for new session files, re-enumerate client windows |

Reading appended text is cheap and needs to be responsive for alerts; directory scanning and
window enumeration are comparatively expensive and only need to notice a new session file or
a client starting.

Effects that **cannot** be detected (web, painter, dampener, tracking disruptor) render as
greyed lamps with an explanatory tooltip rather than being hidden — a dark lamp must never
be read as "this is not happening to me".

---

## Configuration

`MultiBoxConfig` intentionally mirrors eve-o-preview's `EVE-O Preview.json`: clients keyed
by window title, a `FlatLayout` of positions, an optional `PerClientLayout` keyed by the
focused client, and a `ClientLayout` map of client rectangles. `EveOPreviewImport` reads a
real eve-o-preview config and adopts its layout, tolerating both `{"X":1,"Y":2}` and
`"1,2"` point encodings (Newtonsoft emits either depending on version).

See [CONFIGURATION.md](CONFIGURATION.md).

---

## Known gaps

Recorded honestly so they are not mistaken for finished work.

- **The window has never been rendered.** Everything was built and tested on macOS via
  `EnableWindowsTargeting`. Parsing, dedup, attribution and stats are verified against real
  logs; the WPF layout itself is unproven until it runs on Windows.
- **The layout does not yet reflect the real screen setup.** The current design is one
  consolidated window. The screenshot in Drive shows four clients arranged roughly 2×2 with
  ~6 PyEveLiveDPS windows pushed to the screen edges, which suggests small per-client panels
  positioned relative to each client's rectangle (the `ClientLayout` data is already
  captured for exactly this) rather than a single window. **Deferred by agreement until the
  app runs.**
- **Web detection is impossible from logs** and the captured test session did not actually
  land a web — see [EWAR-DETECTION.md](EWAR-DETECTION.md) for the exact status and the
  re-test that would settle it.
- **No fleet-wide roll-up** (total fleet DPS, who is being primaried) — the per-character
  data supports it, nothing renders it yet.
- **`Sz`/`PanelSize` is only partially used**, since the layout question above is open.
