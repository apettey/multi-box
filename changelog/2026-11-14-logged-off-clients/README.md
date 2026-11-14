# Logged-off clients stay on the dashboard

**Date:** 2026-11-14

## Description

Logging a character off without closing the client used to make that client disappear from
the dashboard until someone logged back in. Each client sitting at character select now gets a
**Character select** card after the character cards, with its live thumbnail, and clicking it
brings that client to the front.

## Before / After

| Before | After |
| --- | --- |
| ![before](before.png) | ![after](after.png) |

## Change

- `MultiBox.Core.Model.EveWindowTitle`: the title rules (`EVE - <name>` logged in, bare `EVE`
  at character select) moved out of `EveClientLocator` so they are tested off Windows.
- `EveClientLocator.Scan` classifies every window in one pass into logged-in clients and clients
  at character select; a rescan uses that one snapshot throughout, so a client logging in or out
  mid-scan never shows as both or neither. `FindClients` and `CountOpenClients` share the
  enumeration helper. Same native calls as before: no new APIs, file writes or hosts.
- `FleetLayout.KeepOrder` keeps the character-select cards in first-seen order, because
  `EnumWindows` reports z-order and would otherwise reshuffle them on every alt-tab.
- `FleetLayout.Squad(characters, idle, ...)` puts character-select cards on the last squad
  without counting them against the squad size, so a full squad never pushes them onto a tab of
  their own. `FleetLayout.HotkeyLabel` gives them no F-key label.
- `ClientCardViewModel` base for anything on the grid; `IdleClientCardViewModel` and the
  `IdleClientCard` template. Character-select cards cannot be dragged.
- The header's "N closed" now reads "N offline": a character logged off to character select
  cannot be told apart from a closed one, since the window no longer names it.
- The grid now notices when the set of cards changes at the same count (a character logging
  off swaps one card for another); it used to compare counts only.
- Tests: `IdleClientTests`, 39 cases, covering the title rules, the stable order, idle clients on the last squad and the F-key labels. Full suite 238/238.
