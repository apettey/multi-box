# MultiBox

A single dashboard for multiboxing EVE Online: per-character DPS, remote reps and incoming
electronic warfare with distinct audio alerts, plus one deduplicated read-only chat window
instead of the same message repeated once per client.

Built to replace a screen full of separate windows — one PyEveLiveDPS instance per
character, plus four copies of every chat channel — with one place to look.

> **Status:** core is built and verified against real logs (39 tests, 99.97% line coverage,
> Windows binaries publish clean). The WPF window itself has not yet been rendered on
> Windows, and the on-screen layout is expected to change once it runs. See
> [known gaps](docs/ARCHITECTURE.md#known-gaps).

---

## The problem it solves

Running four clients means four log files recording the same fight from four angles:

- A warp scramble on one character is written to **every** client's log — once as
  `to you!`, and once as `to <Ship> // <Name>` in each observer's file.
- Every fleet message is written **four times**, once per client in the channel.

So the raw data is four times larger than the truth. MultiBox collapses it: **6.7% of parsed
combat events in the captured session were cross-client duplicates**, and 50 warp-scramble
lines represented **19 actual scrambles**.

---

## Rules it plays by

**Only two data sources: log files on disk, and the public ESI API.** No reading EVE client
memory, no screen capture, no injected input, no automation of the game. This is audited in
[EULA-COMPLIANCE.md](docs/EULA-COMPLIANCE.md) and enforced by tests that fail the build if a
prohibited API is ever introduced. The Windows API calls it makes are
read-only window queries (`EnumWindows`, `GetWindowText`, `GetWindowRect`) used to match a
character to a window and to position panels.

ESI is a **verification step, not a dependency**: character ids come free in log filenames
and names come free in log banners, so ESI only confirms the two agree.

---

## What it can and cannot detect

EVE writes a game-log line for *some* EWAR and stays silent for the rest. This is a property
of the client — no log-based tool can do better.

| Effect | Detected | Source line |
|---|---|---|
| ECM jam | yes | `You're jammed by <attacker> - <module>` |
| Warp scramble | yes | `Warp scramble attempt from <attacker> to you!` |
| Warp disruption | yes | `Warp disruption attempt from <attacker> to you!` |
| Energy neutraliser | yes | `<N> GJ energy neutralized` |
| **Stasis web** | **no** | nothing is written when a web lands |
| Target painter | no | nothing is written |
| Sensor dampener | no | nothing is written |
| Tracking disruptor | no | nothing is written |

Undetectable effects still appear in the UI, **greyed out with an explanatory tooltip**, so
a dark indicator is never mistaken for "this is not happening to me".

⚠️ **On webs specifically:** the captured test session contains 8 webifier lines, all of them
`(notify) ... is too far away to use your Fleeting Compact Stasis Webifier on`. The web never
actually landed, so that session does not by itself prove a landed web is unlogged — the
conclusion rests on corroborating evidence. [The full reasoning, and the one-minute re-test
that would settle it, is in EWAR-DETECTION.md](docs/EWAR-DETECTION.md).

---

## Requirements

- Windows 10 or 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — or use the
  self-contained build, which bundles it
- EVE logging enabled (on by default)

## Build

```powershell
.\build.ps1 -Test                # test, then build
.\build.ps1 -SelfContained       # standalone, no runtime install needed
```

Output lands in `dist\win-x64\`.

## First run

Check the setup before launching the UI:

```powershell
.\dist\win-x64\multibox-replay.exe doctor --esi
```

This prints the log folders it found, every character it can see with its id and expected
window title, verifies those ids against ESI, and lists what is and is not detectable. If it
cannot find your logs it prints every path it tried.

Then run `MultiBox.exe`.

---

## Diagnostics

```powershell
multibox-replay doctor [logsRoot] [--esi]   # setup check, character list, detectability
multibox-replay replay <folder>             # parse gamelogs; event totals, dupes, EWAR per pilot
multibox-replay chat   <folder>             # parse chat logs; show the deduplicated stream
```

Sample output from the captured session:

```
parsed events   : 14383
cross-client dupes dropped: 969 (6.7%)
distinct events : 13414

Incoming EWAR by character:
  Commander Tyrael       Jammed       x28
  Commander Tyrael       Scrambled    x11
  Lieutent Tyrael        Scrambled    x4
  Lieutent Tyrael        Disrupted    x1
  Major Tyrael           Scrambled    x3
```

---

## How it works

```
Gamelogs/*.txt ─┐
                ├─► LogTailer ─► parser ─► dedup ─► CharacterMonitor ─► tiles + alerts
Chatlogs/*.txt ─┘                              └─► ChatAggregator ───► unified chat
```

Three details carry most of the design:

**Every client logs the same event**, from its own perspective. Events are keyed on
`(timestamp, kind, ewar, victim, attacker, amount, module)` and duplicates dropped.
Resolving `"you"` to the log's owner is what makes four different-looking lines agree on one
victim.

**Chat is deduplicated the same way**, on `(timestamp, channel, sender, text)`. Each row
shows how many of your clients saw it — useful for noticing a character was not actually in
the channel.

**Attribution is per pilot.** Outgoing events only ever appear in the acting pilot's own
log, so they are credited by log owner; incoming events name their victim, so they are
credited by victim regardless of which client recorded them. Getting this backwards sums the
whole fleet's DPS onto every tile — there is a regression test for exactly that.

**EWAR state decays.** EVE logs when an effect is applied and never when it is released, so
each line refreshes a timer and the indicator lapses after `EwarHoldSeconds` (default 12).
Alerts fire on the clear→applied transition only, with a per-effect cooldown, so a cycling
scrambler produces one alert rather than a stream.

Alert sounds are synthesised at runtime — no audio files to install. A warble for jams, a
falling tone for scrambles, two pips for disruption, a low throb for neuts.

---

## Configuration

`%APPDATA%\MultiBox\multibox.json`, written on exit, created with sensible defaults.

It deliberately mirrors eve-o-preview's config shape — clients keyed by window title
`EVE - <Character Name>`, a `FlatLayout` for positions, an optional `PerClientLayout` keyed
by focused client — so both tools describe layouts the same way. On first run MultiBox
imports `ClientLayout` and `FlatLayout` from `%LOCALAPPDATA%\EVE-O Preview\EVE-O Preview.json`
if present. The import is read-only; the two tools run side by side.

Full key reference: [CONFIGURATION.md](docs/CONFIGURATION.md).

---

## Tests

```bash
dotnet test
```

39 tests, ~300 ms. Most run against the **real logs in `samples/`** — including the
deliberate ECM/scramble/web session — rather than invented fixtures, because a parser that
only works on made-up data cannot pass. The corpus test asserts that over 99% of real combat
lines parse; it currently sits at **99.97%** (14,383 of 14,388) and prints whatever it could
not parse.

---

## Documentation

| Document | Contents |
|---|---|
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | design constraints, data flow, every component, known gaps |
| [LOG-FORMATS.md](docs/LOG-FORMATS.md) | complete EVE log format reference — encodings, naming, every line shape |
| [EWAR-DETECTION.md](docs/EWAR-DETECTION.md) | what is detectable, the webifier investigation, alternatives considered |
| [EULA-COMPLIANCE.md](docs/EULA-COMPLIANCE.md) | audit against the EVE EULA: no memory reads, no screen capture, no input |
| [CONFIGURATION.md](docs/CONFIGURATION.md) | every configuration key |
| [DEVELOPMENT.md](docs/DEVELOPMENT.md) | building, testing, adding event types, gotchas |

---

## Layout

```
src/MultiBox.Core/     parsing, tailing, dedup, stats, config, ESI   (net8.0, portable)
src/MultiBox.App/      WPF dashboard, alerts, window interop         (net8.0-windows)
src/MultiBox.Replay/   console diagnostics                           (net8.0, portable)
tests/                 xunit suite
samples/               real logs captured from the clients
docs/                  reference documentation
```

The core is deliberately portable so parsers can be developed and tested on any machine;
only the UI layer is Windows-bound. The Windows binary can even be published from macOS with
`-p:EnableWindowsTargeting=true`.

---

## Credits

Inspired by, and designed to sit alongside:

- [PyEveLiveDPS](https://github.com/ArtificialQualia/PyEveLiveDPS) — the live DPS graph this
  consolidates
- [eve-o-preview](https://github.com/Proopai/eve-o-preview) — client thumbnails and the
  window-title convention and config shape this deliberately matches
