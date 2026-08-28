# EULA compliance review

Audit of MultiBox against the EVE Online EULA and CCP's third-party application policy,
covering the specific concerns raised: **reading the screen**, and **reading game memory to
inspect state**.

**Reviewed:** 2026-08-28, against commit `67e84d7` plus the hardening described below.
**Scope:** all 42 source files in `src/` and `tests/`, excluding build output.

---

## Verdict

**No boundary is crossed. The application is safe to install and run.**

MultiBox reads two things: text files that the EVE client writes to disk, and the public
ESI web API. It additionally asks Windows for the titles and rectangles of visible windows,
which is how it knows which character is on which monitor.

It does not read game memory, does not capture or sample the screen, does not send input,
and cannot modify the game or any of its windows.

---

## The two specific concerns

### 1. Reading the screen — NOT DONE

There is no screen capture, pixel sampling or OCR anywhere in the codebase. Verified by
searching all source for `BitBlt`, `PrintWindow`, `CopyFromScreen`, `GetWindowDC`,
`GetDC`, `GetPixel`, `CaptureScreen` and `System.Drawing.Bitmap` — **zero matches**.

The application never sees a single pixel of the EVE client. It knows a window's
**position and size** (`GetWindowRect`) and its **title bar text** (`GetWindowText`), which
is metadata the window manager exposes about every window on the desktop, not image content.

This was a live design decision rather than an accident. Screen-scraping the HUD EWAR icons
is the *only* technique that could detect stasis webs, which the logs do not record. It was
explicitly rejected — see [EWAR-DETECTION.md](EWAR-DETECTION.md). The feature was dropped
rather than the constraint.

### 2. Reading game memory — NOT DONE

No process memory access of any kind. Verified by searching all source for
`ReadProcessMemory`, `WriteProcessMemory`, `OpenProcess`, `VirtualQuery`, `VirtualProtect`,
`NtReadVirtualMemory`, `CreateRemoteThread`, `LoadLibrary`, `MainModule` and
`System.Diagnostics.Process` — **zero matches**.

MultiBox never opens a handle to the EVE process. It does not enumerate processes at all.
It could not read game memory even if asked to: the application manifest requests
`asInvoker` (standard user, no elevation), and it never acquires the process handle such an
operation would require.

---

## Complete native API surface

This is the entire list. Every entry is a read-only query, and all of them concern *windows
on the desktop*, not the game process.

| Function | What it does | Can it affect the game? |
|---|---|---|
| `EnumWindows` | list visible top-level windows | no — enumeration only |
| `IsWindowVisible` | is a window visible | no — read-only |
| `GetWindowTextLength` | length of a window title | no — read-only |
| `GetWindowText` | read a window title (`EVE - Commander Tyrael`) | no — read-only |
| `GetWindowRect` | read a window's position and size | no — read-only |
| `GetForegroundWindow` | which window has focus | no — reads focus, cannot set it |

**Six functions, all read-only.** This is a strict subset of what eve-o-preview — a widely
used and long-tolerated tool — requires, since eve-o-preview additionally moves windows and
switches client focus, and MultiBox does neither.

### Hardening applied during this review

`SetWindowPos` and `FindWindow` were **declared but never called** — dead code left from
scaffolding. They have been **removed**, along with the `HWND_TOPMOST` and `SWP_*`
constants.

This matters beyond tidiness. `SetWindowPos` is the one API in the original file capable of
moving, resizing or reordering another application's window. With it deleted, the claim
"this program cannot touch a game window" is provable by reading one short file, rather than
something you have to take on trust. `NativeMethods.cs` now carries a comment saying so, and
a unit test enforces it.

---

## Checked against CCP's prohibitions

| Prohibited | Status | Evidence |
|---|---|---|
| Reading/modifying client memory | **not done** | no memory APIs; no process handle; no elevation |
| Screen scraping / pixel reading / OCR | **not done** | no capture APIs; deliberately rejected feature |
| Injecting code into the client | **not done** | no `CreateRemoteThread`, no `LoadLibrary`, no DLL |
| Automating gameplay / botting | **not done** | no input APIs; the app has no way to act in game |
| Input broadcasting (one keypress → many clients) | **not done** | no `SendInput`, `keybd_event`, `PostMessage`, `SendMessage`, `SendKeys`; no hotkey registration |
| Intercepting or modifying network traffic | **not done** | no sockets, no packet capture; only HTTPS to `esi.evetech.net` |
| Modifying client files | **not done** | every EVE file is opened `FileAccess.Read` |
| Circumventing client restrictions | **not done** | nothing interacts with the client at all |
| Misrepresenting itself to CCP services | **not done** | ESI requests send an identifying User-Agent |

### Explicitly permitted things it does do

- **Reading the game logs.** EVE writes these plain-text files specifically so players and
  tools can read them. This is the same mechanism PyEveLiveDPS, zKillboard log parsers and
  every combat-log analyser uses.
- **Calling ESI.** CCP's public, documented API, built for third-party tools. Only two
  unauthenticated public endpoints are used: `POST /universe/ids/` and
  `GET /characters/{id}/`, plus the public portrait image service. **No OAuth, no access
  token, no scopes** — so there is not even a credential that could be abused.
- **Reading window titles.** Standard OS metadata, and the documented convention
  (`EVE - <Character Name>`) that eve-o-preview relies on.

---

## Data handling

| Operation | Path | Mode |
|---|---|---|
| EVE game logs | `...\EVE\logs\Gamelogs\*.txt` | **read-only** |
| EVE chat logs | `...\EVE\logs\Chatlogs\*.txt` | **read-only** |
| eve-o-preview config | `%LOCALAPPDATA%\EVE-O Preview\EVE-O Preview.json` | **read-only** |
| MultiBox config | `%APPDATA%\MultiBox\multibox.json` | read/write |

The only file the application ever writes is its own configuration. Every EVE-owned file is
opened `FileMode.Open, FileAccess.Read`. Nothing is uploaded anywhere: log contents never
leave the machine, and the only outbound traffic is the ESI character-id lookup.

`FileShare.ReadWrite | FileShare.Delete` appears on those reads. This is *permissive
sharing*, not write access — it is required because EVE holds its own write lock on an open
log, and without it every read would throw. It grants MultiBox nothing.

## Third-party dependencies

`MultiBox.Core` and `MultiBox.App` have **no third-party NuGet packages** — .NET base class
library only. The four packages in the solution (`xunit`, `xunit.runner.visualstudio`,
`Microsoft.NET.Test.Sdk`, `coverlet.collector`) are test-only and are not shipped in the
published binary. There is no dependency that could introduce prohibited behaviour.

---

## Read-only by construction

The design makes overstep difficult rather than merely absent:

- The app has **no code path that writes to any process**. There is no message pump, no
  input synthesiser, no process handle.
- The chat pane is a **read-only view**. There is no compose box and no send path, because
  there is no mechanism by which text could reach the game.
- All game state arrives through **two parsers over text files**. The parsers cannot do
  anything but produce data structures.

## Enforcement

`EulaComplianceTests` scans the shipped source at test time and fails the build if any
prohibited API name appears, or if a new `DllImport` is added outside the approved
read-only list. A future change that crosses the line breaks the test suite rather than
silently shipping.

---

## Conclusion

**Cleared to install.** The two techniques you asked about — screen reading and memory
inspection — are both absent, and one of them was a feature deliberately abandoned to stay
on the right side of the line. The remaining native surface is six read-only window queries,
strictly fewer capabilities than the eve-o-preview you already run.

The honest caveat, unrelated to the EULA: the WPF window has not yet been rendered on
Windows, so expect UI rough edges on first run.
