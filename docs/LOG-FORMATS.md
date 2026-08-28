# EVE log formats — reference

Everything MultiBox knows comes from two folders EVE writes to disk. This document records
the formats precisely, because they are undocumented by CCP and several details are
counter-intuitive enough to cost hours if rediscovered from scratch.

All observations below were verified against the real logs in `samples/`, captured from four
clients on 2026-08-27 and 2026-08-28.

---

## 1. Locations

EVE writes to `Documents\EVE\logs\`, containing `Gamelogs\` and `Chatlogs\`.

The catch: **OneDrive's "Known Folder Move" silently relocates `Documents`.** On this
machine the real path is:

```
C:\Users\apett\OneDrive\Documents\EVE\logs\Gamelogs
C:\Users\apett\OneDrive\Documents\EVE\logs\Chatlogs
```

`LogDirectory.CandidateRoots()` probes, in order:

1. `Environment.SpecialFolder.MyDocuments` + `EVE\logs` (follows the redirect on most setups)
2. `%USERPROFILE%\OneDrive\Documents\EVE\logs`
3. `%USERPROFILE%\Documents\EVE\logs`
4. `%OneDrive%\Documents\EVE\logs`

Two practical consequences of logs living under OneDrive:

- Files may be **locked mid-sync**. Every read is wrapped so an `IOException` skips that
  poll rather than killing the session.
- Change notifications are unreliable, so MultiBox **polls** on a timer instead of relying
  on `FileSystemWatcher`.

---

## 2. File naming

Both kinds encode the character id in the filename. This is the single most useful fact in
the whole format: it means a pilot's game log and their several channel logs can be grouped
with **no configuration and no ESI call**.

```
Gamelogs:  20260828_160148_1899648001.txt
           └─date─┘ └time┘ └─character id─┘

Chatlogs:  Fleet_20260828_162059_1899648001.txt
           │     └─date─┘ └time┘ └─character id─┘
           └ channel name (may contain spaces: "ViTA Intel_2026...")
```

Parsed by `LogFileName` with:

```
^(?:(?<channel>.+)_)?(?<date>\d{8})_(?<time>\d{6})_(?<charId>\d+)$
```

The timestamp is the **session start**, not the log's last write. EVE opens a brand new file
on every session change — docking, jumping clones, undocking — so a single play session
produces many files per character. `LogDirectory.LatestPerCharacter()` groups by
`(characterId, channel)` and keeps the newest.

Character ids observed in `samples/`:

| Character | Id |
|---|---|
| Commander Tyrael | 1899648001 |
| Major Tyrael | 1532110739 |
| Lieutent Tyrael | 1462945193 |
| Sergeant Tyrael | 1530728218 |

Verified against ESI `POST /universe/ids/` — all match.

---

## 3. Encoding — the biggest trap

**The two log types are encoded differently.**

| | Gamelogs | Chatlogs |
|---|---|---|
| Encoding | UTF-8 | **UTF-16LE** |
| BOM | once, at the start | **once per line** |
| Line endings | `\r\n` | `\r\n` (file starts `\r\n` too) |

Chat logs re-emit a byte-order mark (`U+FEFF`, bytes `FF FE`) in front of **every appended
line**, not just at the start of the file. A reader that decodes UTF-16 correctly but does
not strip the per-line BOM sees each line as `\uFEFF[ 2026.08.27 ...` and every timestamp
regex fails.

`ChatlogParser.Clean()` removes them. `LogTailer` opens files with
`detectEncodingFromByteOrderMarks: true` so the same tailer handles both types without the
caller knowing which it has.

This is almost certainly why PyEveLiveDPS only reads Gamelogs — the chat encoding is a
separate problem.

---

## 4. Header banner

Both file types open with a banner. This is where the **character name** comes from, and it
pairs with the id in the filename.

Gamelog:

```
------------------------------------------------------------
  Gamelog
  Listener: Commander Tyrael
  Session Started: 2026.08.28 16:01:48
------------------------------------------------------------
```

Chatlog:

```
        Channel ID:      fleet_11326123033340
        Channel Name:    Fleet
        Listener:        Commander Tyrael
        Session started: 2026.08.27 19:25:20
```

Note `Session Started` (gamelog, capital S) vs `Session started` (chatlog, lowercase).
`LogHeader` matches both. Only the first 12 lines are scanned.

`Listener` is the pilot whose client wrote the file. It is the anchor for the whole system:
it resolves `"you"` in combat lines, and it matches the Windows title `EVE - <Listener>`.

---

## 5. Chat lines

Simple and uniform:

```
[ 2026.08.27 21:34:24 ] Red Baldric > amazing boosh lmfao
[ 2026.08.27 19:25:28 ] EVE System > Channel changed to Local : J134414
```

```
^\[ (?<ts>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}) \] (?<sender>.+?) > (?<text>.*)$
```

Timestamps are **UTC** (EVE time). Sender `EVE System` marks system notices, which the UI
dims and italicises. In wormhole space, `Channel changed to Local : <system>` is how you can
follow which hole a character is sitting in.

---

## 6. Gamelog lines

```
[ 2026.08.28 16:23:51 ] (combat) <markup>36</markup> from Anchoring Damavik - Hits
└──── timestamp ─────┘ └category┘ └───────────────── body ──────────────────────┘
```

Categories observed, with counts across the three ECM-session logs:

| Category | Count | Meaning |
|---|---|---|
| `(combat)` | 14,388 | everything MultiBox parses |
| `(notify)` | 388 | UI messages: "Target is invulnerable", range errors, fleet chatter |
| `(question)` | 10 | confirmation dialogs |
| `(hint)` | 8 | tooltips |

Only `(combat)` is parsed. `(notify)` is deliberately ignored — see
[EWAR-DETECTION.md](EWAR-DETECTION.md) for why the webifier range errors that live there are
*not* usable as a web indicator.

### 6.1 Markup

Combat bodies are wrapped in pseudo-HTML used to colour the in-game log window:

```html
<color=0xffcc0000><b>36</b> <color=0x77ffffff><font size=10>from</font>
<b><color=0xffffffff>Anchoring Damavik</b><font size=10><color=0x77ffffff> - Hits
```

Tags are unbalanced and must not be parsed as XML. `EveMarkup.Strip()` removes
`<[^>]*>`, HTML-decodes, and collapses runs of whitespace. Everything downstream works on
the stripped text.

### 6.2 Entity format

A player is rendered as three coloured spans that strip down to:

```
Retribution // Commander Tyrael VI.TA /
└─ ship ──┘    └── character ──┘ └corp┘
```

An NPC is a bare name: `Anchoring Damavik`, `Sparkneedle Tessella`.

`EveEntity.Parse()` tries `^(?<ship>.+?)\s+//\s+(?<char>.+?)\s+(?<corp>\S+)\s*/\s*$`, then a
no-ticker variant, then falls back to a plain name. The literal string `you` becomes
`EveEntity.Self`.

### 6.3 Line shapes

| Shape | Direction | Example (stripped) |
|---|---|---|
| `N to <entity> - [module - ]quality` | out | `295 to Sparkneedle Tessella - Small Focused Pulse Laser II - Penetrates` |
| `N from <entity> - quality` | in | `36 from Anchoring Damavik - Hits` |
| `N remote armor repaired to <entity> - <module>` | out | `103 remote armor repaired to Deacon // Major Tyrael VI.TA / - Coreli A-Type Small Remote Armor Repairer` |
| `N remote armor repaired by <entity> - <module>` | in | as above with `by` |
| `N GJ energy neutralized <entity> - <source>` | out | `66 GJ energy neutralized Starving Damavik - Starving Damavik` |
| `Warp scramble attempt from <A> to <B>` | either | `Warp scramble attempt from Anchoring Damavik to you!` |
| `Warp disruption attempt from <A> to <B>` | either | `Warp disruption attempt from Garmur // Commander Tyrael VI.TA / to you!` |
| `You're jammed by <entity> - <module>` | in | `You're jammed by Rook // Lieutent Tyrael VI.TA / - Radar ECM II` |
| `<entity> jammed - <module>` | out | `Garmur // Commander Tyrael VI.TA / jammed - Radar ECM II` |
| `<entity> misses you completely` | in | `Strikeneedle Tessella misses you completely` |
| `Your group of <module> misses <entity> completely - <module>` | out | — |

Two parsing hazards worth stating explicitly:

1. **Module names contain hyphens.** `Coreli A-Type Small Remote Armor Repairer` breaks any
   pattern using `[^-]+` for the module group. The real separator is `" - "` *with spaces*.
   This caused a genuine bug during development.
2. **`<entity> jammed` is extremely broad** — it will swallow other shapes. It must be
   matched **last**, after every more specific pattern has had first refusal.

Damage tails are either `Hits` or `Module - Hits`; splitting on the **last** `" - "`
preserves hyphenated module names.

---

## 7. Perspective — why the same event looks different per client

This is the property the whole deduplication design rests on.

A single warp scramble at `16:23:52` against Commander Tyrael is written to **three**
clients' logs, differently in each:

```
Commander's log:  Warp scramble attempt from Anchoring Damavik to you!
Major's log:      Warp scramble attempt from Anchoring Damavik to Retribution // Commander Tyrael VI.TA /
Lieutent's log:   Warp scramble attempt from Anchoring Damavik to Retribution // Commander Tyrael VI.TA /
```

So:

- **`to you!`** ⇒ the victim is that file's `Listener`.
- **`to <Ship> // <Name>`** ⇒ the victim is named outright, and the line also reveals the
  ship they are flying.

`EveEntity.ResolveSelf(listener)` normalises `"you"` to the listener's name, after which all
three lines agree on one victim and collapse to a single event.

Measured in `samples/`: **50 scramble lines → 19 distinct events**, and 969 of 14,383 parsed
events (6.7%) were cross-client duplicates.

A corollary that matters for correctness: **outgoing events only ever appear in the acting
pilot's own log.** Nobody else records your shots. So outgoing events are attributed by
*log owner*, and incoming events by *named victim*. Getting this backwards sums the whole
fleet's DPS onto every tile — see `AttributionTests`.

---

## 8. Timestamps

All log timestamps are UTC (EVE time), formatted `yyyy.MM.dd HH:mm:ss`, parsed with
`AssumeUniversal | AdjustToUniversal`. The UI converts to local time only for display.

Resolution is **one second**, which is why dedup keys use `yyyyMMddHHmmss` — two genuinely
distinct events within the same second, from the same attacker, of the same type, on the
same victim, for the same amount are indistinguishable in the log and are treated as one.
In practice that is the correct call: it is far more often one event seen twice.
