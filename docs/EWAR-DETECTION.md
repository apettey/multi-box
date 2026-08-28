# EWAR detection — what is possible and what is not

This is the question the project started with: *"do I have EWAR applied against me?"* The
answer is partly yes and partly a hard no, and the boundary is a property of the EVE client
rather than of this tool.

## Summary

| Effect | Detected | Evidence |
|---|---|---|
| ECM jam | **yes** | `You're jammed by <attacker> - <module>` — 32 lines in the test session |
| Warp scramble | **yes** | `Warp scramble attempt from <attacker> to you!` — 50 lines / 19 events |
| Warp disruption | **yes** | `Warp disruption attempt from <attacker> to you!` |
| Energy neutraliser | **yes** | `<N> GJ energy neutralized` — 206 lines |
| **Stasis web** | **no** | no combat line exists |
| Target painter | **no** | no combat line exists |
| Sensor dampener | **no** | no combat line exists |
| Tracking disruptor | **no** | no combat line exists |

Detected effects appear per character in the dashboard with a distinct alert sound.
Undetectable effects render as **greyed lamps with a tooltip** explaining why, so a dark
indicator is never mistaken for "this is not happening to me".

---

## What the ECM test session actually showed

The session captured on 2026-08-28 (`samples/Gamelogs/`, three clients, 14,388 combat lines)
was set up deliberately to produce jams, tackle and webs. Parsed results:

```
Commander Tyrael    Jammed      x28      (Radar / Gravimetric / Ladar ECM II, from a Rook)
Commander Tyrael    Scrambled   x11
Lieutent Tyrael     Scrambled   x4
Lieutent Tyrael     Disrupted   x1
Major Tyrael        Scrambled   x3
```

The self-test is visible in the data: `You're jammed by Rook // Lieutent Tyrael VI.TA / -
Radar ECM II` in Commander's log, with the mirror line `Garmur // Commander Tyrael VI.TA /
jammed - Radar ECM II` in Lieutent's. One character jamming another, recorded from both
sides.

### The webifier result, stated precisely

The session contains **8 lines mentioning a stasis webifier, and 0 of them are combat
lines.** Every one is a `(notify)` range error:

```
[ 2026.08.28 17:55:11 ] (notify) Caracal is too far away to use your Fleeting Compact
                                 Stasis Webifier on, it needs to be closer than 10000 meters.
```

This is an honest ambiguity and worth being exact about:

> **The web never landed.** The module was activated from beyond 10 km every time, so the
> session contains no instance of a web actually being applied. It therefore does not, on
> its own, prove that a landed web goes unlogged.

`RealLogCorpusTests.StasisWebifiersNeverAppearAsCombatEvents` asserts exactly this and no
more: webifiers are mentioned, and never in a `(combat)` line.

### Why the conclusion is still "no"

Supporting evidence beyond this session:

- **PyEveLiveDPS's parser** — the most established log tool in this space — has regexes for
  damage, logistics, capacitor transfer, energy neutralisation, energy drain and mining, and
  **none** for webs, painters, dampeners or tracking disruptors, nor for incoming scrams or
  jams.
- No log-parsing tool in the wider ecosystem surfaces webs.
- The pattern in EVE's logging is consistent: the client writes a line when something has a
  **numeric magnitude** (damage, repair, GJ) or is a **discrete tackle/ECM event**
  (scramble, disruption, jam). Effects that only apply a modifier — speed, signature,
  targeting range, tracking — are rendered as HUD icons and never written to disk.

### The test that would settle it

Land a web on an alt **inside 10 km**, then run:

```powershell
multibox-replay.exe replay "%USERPROFILE%\OneDrive\Documents\EVE\logs\Gamelogs"
```

If a new line shape appears, it can be taught to the parser quickly: add a regex in
`GamelogParser`, flip `EwarTypeInfo.IsLogged(EwarType.Web)` to `true`, and enable the `Web`
alert in config (a `buzz` tone is already defined and disabled). The lamp, the config entry
and the alert slot all exist already — only the pattern is missing.

---

## What could detect webs, and why it is not built

For completeness, since the question will recur:

| Approach | Works? | Why it is not used |
|---|---|---|
| Parse game logs | no | the line does not exist |
| ESI API | no | ESI exposes no live combat, module or grid state |
| Read client memory | yes | **forbidden** — EULA, and an explicit project constraint |
| Screen-scrape the HUD EWAR icons | probably | reads pixels rather than logs; outside the "logs and API only" rule agreed for this project |

The third is off the table permanently. The fourth is technically feasible — the icons that
appear under the capacitor when EWAR is applied are exactly the missing signal — but it is a
different class of tool, so it was deliberately not built. Raise it if the constraint ever
changes.

---

## How detection behaves at runtime

**Application is logged; release is not.** There is no "you are no longer scrambled" line,
so `EwarStateTracker` uses a decaying hold: each matching line refreshes the effect, and it
lapses after `EwarHoldSeconds` (default 12) without a refresh. Scramblers and disruptors
re-log on every activation cycle, so a hold slightly above one cycle tracks reality closely
while genuinely tackled.

**Alerts fire on transition, not per line.** `EwarApplied` raises only on clear→applied, and
`AlertPlayer` additionally rate-limits per `(character, effect)` with `CooldownSeconds`
(default 8). Without both, a single tackle would produce an alert every few seconds for as
long as you were held.

**Attribution is per victim.** An EWAR event witnessed in another character's log still
lights the correct pilot's lamp, because incoming events name their victim explicitly and
are matched on that name, not on which file they came from.
