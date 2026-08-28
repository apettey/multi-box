# Configuration

MultiBox reads and writes `%APPDATA%\MultiBox\multibox.json`. The file is written on exit
and does not need to exist beforehand — every value has a default.

The shape deliberately mirrors eve-o-preview's `EVE-O Preview.json` so both tools describe
layouts the same way, and so an existing arrangement can be imported rather than rebuilt.

---

## Full example

```json
{
  "ConfigVersion": 1,
  "GamelogPath": null,
  "ChatlogPath": null,
  "StatWindowSeconds": 10,
  "EwarHoldSeconds": 12,
  "AlwaysOnTop": true,
  "Opacity": 0.95,
  "ChatChannels": ["Fleet", "Local", "Corp", "ViTA Intel"],
  "ChatScrollbackLines": 2000,
  "Alerts": {
    "Jam":               { "Enabled": true,  "Tone": "warble",  "Frequency": 880, "DurationMs": 500, "WavPath": null, "CooldownSeconds": 8 },
    "WarpScramble":      { "Enabled": true,  "Tone": "descend", "Frequency": 440, "DurationMs": 450, "WavPath": null, "CooldownSeconds": 8 },
    "WarpDisruption":    { "Enabled": true,  "Tone": "double",  "Frequency": 620, "DurationMs": 400, "WavPath": null, "CooldownSeconds": 8 },
    "EnergyNeutralizer": { "Enabled": true,  "Tone": "low",     "Frequency": 200, "DurationMs": 400, "WavPath": null, "CooldownSeconds": 8 },
    "Web":               { "Enabled": false, "Tone": "buzz",    "Frequency": 320, "DurationMs": 400, "WavPath": null, "CooldownSeconds": 8 }
  },
  "FlatLayout": {
    "MainWindow": { "X": 40, "Y": 40 }
  },
  "PerClientLayout": {},
  "EnablePerClientLayouts": false,
  "ClientLayout": {
    "EVE - Commander Tyrael": { "X": 0, "Y": 0, "Width": 1920, "Height": 1080, "IsMaximized": false }
  },
  "CharacterIds": {
    "Commander Tyrael": 1899648001
  },
  "PanelSize": { "Width": 360, "Height": 190 }
}
```

---

## Settings

### Log locations

| Key | Default | Meaning |
|---|---|---|
| `GamelogPath` | `null` | Explicit path to `Gamelogs`. `null` means auto-detect. |
| `ChatlogPath` | `null` | Explicit path to `Chatlogs`. `null` means auto-detect. |

Auto-detection probes, in order: `Documents\EVE\logs`,
`%USERPROFILE%\OneDrive\Documents\EVE\logs`, `%USERPROFILE%\Documents\EVE\logs`,
`%OneDrive%\Documents\EVE\logs`. Set these explicitly only if `multibox-replay doctor`
cannot find your logs — it prints every path it tried.

### Statistics

| Key | Default | Meaning |
|---|---|---|
| `StatWindowSeconds` | `10` | Trailing window behind DPS, reps and neut readings. Lower reacts faster and looks noisier; higher is smoother and lags. |
| `EwarHoldSeconds` | `12` | How long an EWAR lamp stays lit without a refreshing log line. |

`EwarHoldSeconds` deserves a note: EVE logs when an effect is **applied** and never when it
is **released**, so this value is the app's guess at "still active". It should sit slightly
above one module activation cycle. Too low and the lamp flickers while you are still
tackled; too high and it stays lit after you are free.

### Window

| Key | Default | Meaning |
|---|---|---|
| `AlwaysOnTop` | `true` | Keep the dashboard above the EVE clients. Also toggleable in the UI. |
| `Opacity` | `0.95` | Window opacity, `0.2`–`1.0`. |
| `PanelSize` | `360×190` | Intended per-character panel size. Partially used — see the layout gap in [ARCHITECTURE.md](ARCHITECTURE.md#known-gaps). |

### Chat

| Key | Default | Meaning |
|---|---|---|
| `ChatChannels` | `["Fleet","Local","Corp","ViTA Intel"]` | Channels to watch. **Empty array means all channels.** Names must match EVE's channel name as written in the log header. |
| `ChatScrollbackLines` | `2000` | Rows kept in memory and in the pane. |

Channels are matched case-insensitively against the `Channel Name` in each chat log's
banner. Adding a channel here is all that is needed — file discovery is automatic.

### Alerts

Keys are `EwarType` names: `Jam`, `WarpScramble`, `WarpDisruption`, `EnergyNeutralizer`,
`Web`.

| Field | Meaning |
|---|---|
| `Enabled` | Play a sound when this effect lands. |
| `Tone` | Generated waveform shape: `warble`, `descend`, `double`, `low`, `buzz`, `beep`. |
| `Frequency` | Base frequency in Hz. |
| `DurationMs` | Length in milliseconds. |
| `WavPath` | Path to a `.wav` to play **instead** of the generated tone. `null` to generate. |
| `CooldownSeconds` | Minimum gap between repeats of this alert for the same character. |

Sounds are synthesised at runtime, so nothing needs installing. The shapes are chosen to be
told apart while you are looking at something else — pitch alone is a poor differentiator
under pressure:

- `warble` — rapid vibrato, reads as sensor interference (jams)
- `descend` — falling pitch, reads as being held down (scrambles)
- `double` — two short pips, distinguished by rhythm rather than pitch (disruption)
- `low` — slow throb at the bottom of the range (neuts)
- `buzz` — square-ish rasp (reserved for webs, disabled by default)

`Web` is present but disabled because EVE writes no log line when a web is applied. See
[EWAR-DETECTION.md](EWAR-DETECTION.md). If that ever changes, enabling it here is the only
config step needed.

To use your own sounds, set `WavPath` to a 16-bit PCM `.wav`.

### Layout — eve-o-preview compatible

| Key | Meaning |
|---|---|
| `FlatLayout` | `panelKey → {X,Y}`. The single arrangement used when per-client layouts are off. |
| `PerClientLayout` | `activeClientTitle → (panelKey → {X,Y})`. Alternative arrangements per focused client. |
| `EnablePerClientLayouts` | Switches which of the two is consulted. |
| `ClientLayout` | `windowTitle → {X,Y,Width,Height,IsMaximized}`. Tracked rectangles of the running EVE clients. |
| `CharacterIds` | `characterName → ESI id`, learned from log filenames and confirmed via ESI. |

Clients are keyed by **window title**, which EVE sets to `EVE - <Character Name>`:

```
"EVE - Commander Tyrael"
```

This is the same key eve-o-preview uses, which is what makes the two configs interchangeable
and lets MultiBox position panels against the rectangles you already arranged.

Lookup order when `EnablePerClientLayouts` is `true`: `PerClientLayout[activeClient][panel]`
→ `FlatLayout[panel]` → built-in default. When `false`, `PerClientLayout` is skipped
entirely.

---

## Importing from eve-o-preview

On first run, if `ClientLayout` is empty, MultiBox reads:

```
%LOCALAPPDATA%\EVE-O Preview\EVE-O Preview.json
```

and copies across `ClientLayout` and `FlatLayout`. Both point encodings Newtonsoft may have
written are accepted — `{"X":1,"Y":2}` and `"1,2"` — because which one appears depends on
the eve-o-preview version.

The import is one-way and non-destructive: eve-o-preview's file is only read, never written.
The two tools can run side by side.

---

## Resetting

Delete `%APPDATA%\MultiBox\multibox.json`. Every value regenerates at defaults on the next
run, and the eve-o-preview import runs again.
