# Handoff: EVE Fleet Command Dashboard (v2)

## Overview
A unified multibox monitoring dashboard for EVE Online, replacing the need to watch PyEveLiveDPS + EVE-O Preview + multiple chat windows separately. One 2160p screen shows 10 characters' combat state (incoming/outgoing DPS, reps, cap transfer, neut pressure, EWAR applied against them), per-character combat logs, live client thumbnails, and a deduplicated read-only chat feed across all clients' channels.

Target: **Windows desktop app on .NET**. Recommended: WPF/WinForms shell hosting this UI in **WebView2** (pixel-identical), with .NET doing log parsing, window capture/focus, and pushing data via `PostWebMessageAsJson`. Alternatively rebuild natively in WPF/WinUI 3.

## About the Design Files
`Fleet Command.dc.html` is a **design reference created in HTML** — a prototype showing intended look and behavior, not production code to copy directly. Recreate it in the target environment. All data in it is a hardcoded snapshot; the real app feeds live data. (`support.js` is the preview runtime — ignore it.)

## Fidelity
**High-fidelity.** Colors, typography, spacing, and alert behaviors are final. Recreate pixel-perfectly.

## Data Sources (real app)
- **Combat metrics + EWAR + per-char logs**: parse each client's combat log files (as PyEveLiveDPS does — see github.com/ArtificialQualia/PyEveLiveDPS). Combat log lines name the aggressor ship/pilot; surface it on EWAR badges.
- **Client thumbnails + click-to-focus**: DWM thumbnail APIs (`DwmRegisterThumbnail`) as EVE-O Preview does (github.com/Proopai/eve-o-preview). In WebView2, position DWM thumbnails as native overlays above the 16:9 slots from the .NET side.
- **Chat**: tail each client's chat log files; merge identical messages (same channel+sender+text within a window) across clients, showing a `×N` merge count.

## Layout (2160p, full monitor)
Root: full-viewport column, `background #0a0d11`, min 2200×1200 (scrolls below that).
1. **Header bar** (~52px): app title · fleet totals (incoming, reps in, neut pressure, EWAR alert count) · squad tabs (SQUAD 1 · 10 / SQUAD 2 for chars 11+) · 🔊 VOICE test button · EVE clock.
2. **Main row** (flex, 10px gap/padding):
   - **Character grid** (flex:1): CSS grid, `repeat(5,1fr)` × 2 rows, 10px gap. With N<10 chars: cols = ceil(N/2) capped at 5, one row when N ≤ cols.
   - **Unified comms panel** (fixed 440px).

### Character card (each of 10)
Column flex, `background #10151b`, border `1px solid #232b35`, radius 5px, hidden overflow. Under EWAR: border `#ff5c5c88` + pulsing `threatpulse` box-shadow animation (1.4s).
Top→bottom:
- **Header row**: name (14px 600 #e8eef5) · ship (10px mono #5c6875) · role chip (9px mono, outlined; DPS #ffb454, LOGI #4cc38a, CMD #58a6ff, EWAR #e864ff). Card is `draggable` — drag-and-drop reorders characters (drop target = another card; dragged card at 40% opacity).
- **Live thumbnail slot**: 16:9 aspect (matches game client ratio), 10px side margins, hatched placeholder + hotkey label (F1–F10). Click → **activates the REAL EVE client window**: the actual game window is restored/brought to foreground via `SetForegroundWindow` (+ `ShowWindow` if minimized) and receives keyboard/mouse input — the dashboard does NOT render the game or open any in-app screen. This is exactly EVE-O Preview's thumbnail-click behavior. (The prototype's modal overlay is only a stand-in to demonstrate the click.) Hover: blue border #4dabf7.
- **Incoming DPS block** (2px margin, 4px radius): label 9px mono #5c6875; value 24px mono 600 (#ff5c5c if >300, #c9a0a0 if >0, #3a4550 if 0); dual sparkline SVG (100×22 viewBox, non-scaling 1.4px strokes): red #ff5c5c = incoming DPS history, green #4cc38a = reps-in history.
  - **DPS > reps**: block flashes red (`tankred` 0.8s: bg #ff5c5c1a→#ff5c5c40, glowing border) + "▲ EXCEEDS REPS" tag (9px mono 600 #ff5c5c).
  - **Taking damage but reps ≥ DPS**: calm green pulse (`repgreen` 1.6s) + "● REPS HOLDING" tag (#4cc38a).
- **Stats row** (4-col grid, 8px gap): DPS OUT (#ffb454) · REPS IN (#4cc38a) · CAP XFER (+N, #58a6ff) · NEUT/NOS (-N, #4dabf7). Values 15px mono 600; zero values render "—" in #3a4550. Labels 9px mono #5c6875, 1px letter-spacing.
- **Per-character combat log** (flex:1, scrollable, top border #1a212a): rows `HH:MM:SS  message`, 10px mono. Color by kind: damage #ff5c5c (heavy) / #c9a0a0 (light), reps #4cc38a, cap #58a6ff, neut #4dabf7, EWAR in its badge color, idle #48545f.
- **EWAR badge row** (bottom, wraps): per active effect a chip `TYPE Source Ship "Pilot"` — 9px mono 600 type + 400 #8a96a3 source, 1px border in effect color, bg effectColor+10% alpha, radius 3px. Colors: JAM #e864ff, DAMP #8f7bff, SCRAM #ff6b6b, **WEB #4dabf7 with fast flash** (`webflash` 0.9s: bg/glow pulse — web is highest priority), TP #ffd43b, NEUT #b197fc. No effects → "NO EWAR" in #3a4550.

### Unified comms panel (440px)
`background #10151b`, border #232b35, radius 5px.
- Header: "UNIFIED COMMS" (11px mono 600, 2px tracking) · "READ ONLY" chip · "N dupes merged" (#4cc38a).
- **Channel filter chips**: pinned order **ALL · CORP · ALLIANCE · LOCAL**, then all other channels **alphabetically** (list can get long — row wraps, max-height 64px, scrolls). Active chip: bg #1a212a, border #3a4550, text #e8eef5; inactive text #5c6875. Clicking filters messages.
- **Message list** (scrollable, newest first): `HH:MM:SS · CHANNEL · Sender › text`, 13px. Channel tag 9px mono 600, min-width 52px, max-width 110px ellipsized. Channel colors: CORP #4cc38a, ALLIANCE #d0a04a, LOCAL #8a96a3, any intel/other channel #ff5c5c. Duplicate-merged messages show `×N` right-aligned (#48545f).
- Footer hint line explaining ×N.

## Interactions & Behavior
- **Drag-reorder cards** (persist order per squad).
- **Click thumbnail → bring the real game window to foreground as active input** (SetForegroundWindow on the EVE client process — not an in-app view).
- **Channel filter chips** toggle message filtering.
- **Voice alerts**: on new WEB applied, speak "Web detected. <Character name>." (SpeechSynthesis in prototype; System.Speech/SAPI in .NET). Toggleable.
- **Flash animations** (all toggleable via one "alertFlash" setting): threatpulse (EWAR card border), webflash (WEB badge), tankred (DPS>reps), repgreen (stable reps).
- Settings from the prototype's tweaks: characterCount (1–10, grid reflows), showThumbnails, alertFlash, voiceAlerts.
- Squad 2 tab: characters 11+ on a second identical grid.

## State Management
- Per character: id, name, ship, role, hotkey, dpsIn/dpsOut/repsIn/capXfer/neutNos (rolling values), history buffers for dpsIn + repsIn (sparklines, ~16 samples), active EWAR effects [{type, sourceShip, sourcePilot}], combat log ring buffer.
- Global: character order, active squad, chat messages [{time, channel, sender, text, mergeCount}], channel filter, settings (thumbnails, alertFlash, voiceAlerts).
- Triggers: log-file tail events update metrics/logs/EWAR; EWAR add/remove drives border state + voice alert; per-tick comparison dpsIn vs repsIn drives red/green flash.

## Design Tokens
- **Fonts**: IBM Plex Sans (UI), IBM Plex Mono (all numerics, labels, logs, chat tags). Both free (Google Fonts / ships with many systems).
- **Backgrounds**: page #0a0d11, header #0d1117, panel/card #10151b, inset #1a212a, thumbnail hatch #0c1015/#0e1319.
- **Borders**: #1d242e (dividers), #232b35 (cards), #3a4550 (active/hover).
- **Text**: primary #e8eef5, body #cfd8e3, secondary #8a96a3, muted #5c6875, faint #48545f, disabled #3a4550.
- **Semantic**: danger/incoming #ff5c5c, success/reps #4cc38a, dps-out #ffb454, cap #58a6ff, web/neut-blue #4dabf7, gold #d0a04a; EWAR palette above.
- **Radii**: 3px chips, 4–5px cards/panels. Gaps: 10px grid, 8px intra-card.
- Label style: 9px mono, 1–2px letter-spacing, uppercase.

## Assets
None — no images. Thumbnails are live native window previews; hatched rectangles are placeholders only.

## Files
- `Fleet Command.dc.html` — the full prototype (markup between `<x-dc>` tags + `Component` class with the snapshot data and interaction logic).
