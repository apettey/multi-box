# Development

## Prerequisites

- .NET 8 SDK
- Windows only for *running* the GUI; everything else works on any platform

### Installing the SDK

On Windows: download the .NET 8 SDK installer.

On macOS, note that `brew install --cask dotnet-sdk` **fails** in a non-interactive shell
because the pkg installer requires `sudo` with a TTY. Use Microsoft's script instead, which
installs to your home directory with no elevation:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

---

## Build and test

```bash
dotnet build MultiBox.sln -p:EnableWindowsTargeting=true
dotnet test
```

`EnableWindowsTargeting=true` is only needed off-Windows; it lets the `net8.0-windows` WPF
project restore and compile on a machine that is not Windows. The resulting binary still
only *runs* on Windows.

### Publishing

```powershell
.\build.ps1 -Test                # framework-dependent (needs .NET 8 Desktop Runtime)
.\build.ps1 -SelfContained       # standalone, ~145 MB, no runtime install needed
```

Or directly, which also works from macOS:

```bash
dotnet publish src/MultiBox.App/MultiBox.App.csproj -c Release -r win-x64 \
  --self-contained false -p:EnableWindowsTargeting=true -p:PublishSingleFile=true \
  -o dist/win-x64
```

Verify the output is a real Windows binary:

```bash
file dist/win-x64/MultiBox.exe
# PE32+ executable (GUI) x86-64, for MS Windows
```

---

## Testing philosophy

**Tests run against real captured logs, not invented fixtures.** `samples/` holds actual
files from four clients, including the deliberate ECM/scramble/web session. A parser that
only works on made-up data cannot pass.

| File | Covers |
|---|---|
| `GamelogParserTests` | each combat line shape, hand-picked, with real markup intact |
| `RealLogCorpusTests` | whole-corpus coverage, EWAR discovery, dedup, DPS plausibility |
| `AttributionTests` | which pilot an event belongs to (regression guard) |
| `ChatDedupTests` | UTF-16 handling, per-line BOMs, cross-client dedup, tailer semantics |
| `ConfigAndAlertTests` | config round-trip, eve-o-preview import, EWAR hold behaviour |

39 tests total, ~300 ms.

### The coverage test

`ParsesEveryCombatLineInTheCapturedLogs` asserts **>99%** of real combat lines parse, and
prints the first ten it could not. Currently 14,383 of 14,388 — 99.97%.

This is the most valuable test in the suite. A regex that silently stops matching one line
shape would not be caught by any hand-written case, but shows up here immediately as a drop
in coverage plus a printed sample of what broke.

When adding a pattern, run with detailed output to see the unparsed samples:

```bash
dotnet test --logger "console;verbosity=detailed" 2>&1 | grep UNPARSED
```

### Determinism

`RollingWindow` and `EwarStateTracker` take `now` as a parameter and never read the clock
internally. That is what makes replaying a saved log produce numbers identical to a live
session, and lets tests assert exact values on real data. Keep it that way — reading
`DateTime.UtcNow` inside these classes would make the suite time-dependent and flaky.

---

## The diagnostic tool

`MultiBox.Replay` is portable, so it is the fastest way to check parser behaviour during
development without a Windows box:

```bash
dotnet run --project src/MultiBox.Replay -- doctor samples --esi
dotnet run --project src/MultiBox.Replay -- replay samples/Gamelogs
dotnet run --project src/MultiBox.Replay -- chat   samples/Chatlogs
```

`replay` prints event totals, the duplicate percentage and incoming EWAR per character —
the quickest sanity check after touching a parser or the attribution logic.

---

## Adding a new event type

1. Capture a real log line containing it and add it to `samples/`.
2. Add a `Regex` to `GamelogParser`. **Order matters**: broad patterns such as
   `<entity> jammed` must stay last, after every specific shape.
3. Handle it in `ParseCombatBody`, returning a `GameLogEvent` with the right `Direction`,
   `Victim` and — if the line names a ship — `VictimShip`.
4. Accumulate it in `CharacterMonitor.Apply`, respecting the `isMyAction` / `aboutMe` gate.
5. If it is EWAR: add the enum value, set `EwarTypeInfo.IsLogged`, add an `AlertSetting`
   default with a **distinct** tone, and add the lamp in `CharacterViewModel`.
6. Add a unit test with the raw line, and confirm corpus coverage did not drop.

---

## Gotchas

Collected because each one cost real time:

- **Module names contain hyphens** (`Coreli A-Type ...`). Never use `[^-]+` for a module
  group; the separator is `" - "` with spaces.
- **Chat logs are UTF-16LE with a BOM on every line**, not just at the start.
- **EVE holds a write lock** on open logs — `FileShare.ReadWrite | FileShare.Delete` is
  mandatory or every read throws.
- **OneDrive** may lock a file mid-sync; reads must tolerate `IOException` and retry next
  poll.
- **A new log file appears on every session change**, so discovery must re-scan, not open
  once at startup.
- **Outgoing events appear only in the actor's own log**; incoming events name their victim.
  Mixing these up sums the fleet's stats onto every tile.
- **The WPF layout has never been rendered** — see the known gaps in
  [ARCHITECTURE.md](ARCHITECTURE.md#known-gaps).

---

## Repository layout

```
MultiBox.sln
build.ps1                      Windows build/publish script
README.md
docs/
  ARCHITECTURE.md              system design, data flow, known gaps
  LOG-FORMATS.md               EVE log format reference
  EWAR-DETECTION.md            what is detectable, and the webifier finding
  CONFIGURATION.md             every config key
  DEVELOPMENT.md               this file
src/
  MultiBox.Core/               portable: parsing, dedup, stats, config, ESI
    Model/                     EveEntity, GameLogEvent, ChatMessage, enums
    Parsing/                   markup stripping, gamelog + chatlog parsers, filenames
    Chat/                      ChatAggregator, UnifiedMessage
    Stats/                     RollingWindow, EwarStateTracker, CharacterMonitor
    Tailing/                   LogTailer, LogDirectory
    Config/                    MultiBoxConfig, EveOPreviewImport
    Esi/                       EsiClient
    MultiBoxSession.cs         orchestrator
  MultiBox.App/                WPF dashboard (Windows only)
    Alerts/                    ToneGenerator, AlertPlayer
    Interop/                   user32 window queries
    ViewModels/                MVVM layer
  MultiBox.Replay/             console diagnostics
tests/MultiBox.Core.Tests/
samples/                       real captured logs used by the tests
```
