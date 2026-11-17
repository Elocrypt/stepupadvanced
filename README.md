<div align="center">

# StepUp Advanced

**Configurable step-up movement for [Vintage Story](https://www.vintagestory.at/).**

Step height · step speed · ceiling awareness · block blacklist · server-enforced limits.

[![Release](https://img.shields.io/github/v/release/Elocrypt/stepupadvanced?include_prereleases)](https://github.com/Elocrypt/stepupadvanced/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![VS 1.22.0](https://img.shields.io/badge/Vintage%20Story-1.22.0-purple)](https://www.vintagestory.at/)

</div>

---

> Builds on CopyGirl's original [StepUp](https://mods.vintagestory.at/stepup) with full configurability, live hotkeys, ceiling awareness, and multiplayer-friendly server enforcement. Install it on the server for full configurability and shared limits. On servers without it, you still get basic auto step-up.

## Features

<table>
<tr>
<td width="50%" valign="top">

### Step control
- **Configurable step height** from vanilla (0.6 blocks) up
- **Configurable step speed** from 0.7x up (1.0x is vanilla)
- **Live adjustments** — raise or lower height and speed on the fly with hotkeys
- **Dynamic toggle** — turn step-up on or off instantly; off means vanilla height and speed
- **Reloadable config** — apply edits mid-game without restarting

### Movement options
- **Ceiling Guard** — detects a low ceiling and holds your step height down so you don't bump up under trees and floors
- **Forward Ceiling Probe** — predictive check for fast movement, for when the Ceiling Guard would otherwise slip
- **Sprint-only step-up** — enhanced step height only while sprinting
- **Disable while sneaking** — suppress step-up while sneaking, for precise edge placement
- **Disable while airborne** — keep step-up from engaging mid-air so it can't extend your jump reach

</td>
<td width="50%" valign="top">

### Blacklist
- Per-block **step-up blacklist** — step-up disables near listed blocks (ladders, traps, anything you'd rather not auto-climb)
- Add or remove the block you're looking at with a command
- Client and server lists **merge**; the server list is enforced globally

### Multiplayer & enforcement
- **Server-enforced limits** — operators set min/max step height and speed; clients are clamped to those values
- Server config is **pushed to clients on join**
- Client hotkeys for height/speed are disabled where not permitted by the server
- Out-of-bounds server values are validated and corrected automatically
- **Basic mode** on servers without the mod: toggleable 1.2-block step-up at vanilla speed, with nothing adjustable

### Compatibility
- **Speed-Only Mode** — let another mod own step height (e.g. XSkills Steeplechaser) while StepUp Advanced manages speed
- **Chat notifications** keep you informed of changes and limits

</td>
</tr>
</table>

## Install

1. Download the latest `stepupadvanced_<version>.zip` from the [Releases](https://github.com/Elocrypt/stepupadvanced/releases) page (or the [mod portal](https://mods.vintagestory.at/stepupadvanced)).
2. Drop the zip (don't extract it) into your Vintage Story `Mods/` folder:
   - **Windows:** `%AppData%\VintagestoryData\Mods`
   - **Linux:** `~/.config/VintagestoryData/Mods`
   - **macOS:** `~/Library/Application Support/VintagestoryData/Mods`
3. Launch Vintage Story.

Install it on the server and on each client for the full feature set. Clients then receive the server's configuration when they join.

On a server that doesn't have the mod, it runs in **Basic mode**: step-up up to 1.2 blocks at vanilla speed, which you can toggle on and off. Height and speed can't be changed there, by hotkey or config file. That way it never gives more than that on a server whose owner hasn't installed it. In single-player you get everything.

## Using it

**Keybinds** (rebindable in the game's control settings, listed as "StepUp Advanced: …"):

| Key | Action |
| --- | --- |
| <kbd>Insert</kbd> | Toggle step-up on/off |
| <kbd>Home</kbd> | Reload config |
| <kbd>PageUp</kbd> / <kbd>PageDown</kbd> | Increase / decrease step height |
| <kbd>UpArrow</kbd> / <kbd>DownArrow</kbd> | Increase / decrease step speed |

**Commands:**

| Command | Effect |
| --- | --- |
| `.sua` | List StepUp Advanced commands |
| `.sua add` | Add the targeted block to your blacklist |
| `.sua remove` | Remove the targeted block from your blacklist |
| `.sua remove all` | Clear your entire client-side blacklist |
| `.sua list` | List blacklisted blocks (your list merged with the server's) |

Server operators use the same commands with a slash — `/sua add`, `/sua remove`, `/sua remove all`, `/sua reload` — to manage the server-side blacklist and reload the server config. The server list is enforced globally; your client list stays local and is never touched by server admin actions.

Settings live in `ModConfig/StepUpAdvancedConfig.json` under your Vintage Story data folder. Edit it and press <kbd>Home</kbd> to reload, or use the hotkeys for live adjustments. On a server, its limits still apply after a reload, and its settings are never saved into your file.

## Server enforcement

**`ServerEnforceSettings`** lets operators restrict step values globally. When enabled:

- Clients receive the server-defined configuration on join.
- Hotkeys for height/speed changes are disabled where not permitted.
- Step height and speed are clamped between the server's min/max limits as soon as you join, whatever your own config says.
- Server-configured limits are validated and corrected if out of bounds.
- The server's settings only apply while you're connected; they're never written to your local config.

## Compatibility

Latest version is compatible with older and current versions of Vintage Story! (not tested below version 1.19.8)

- Targets **Vintage Story 1.22.0** on .NET 10.
- Client **and** server-side. For server enforcement and the full feature set, the mod must be present on the server; without it, clients run in Basic mode.
- **Speed-Only Mode** is provided for coexistence with mods that manage step height themselves, such as XSkills' Steeplechaser perk — set `SpeedOnlyMode = true` and StepUp Advanced manages only step speed.

## Languages

English, Arabic, Czech, Danish, Dutch, Finnish, French, German, Italian, Japanese, Korean, Polish, Portuguese (Brazil), Russian, Spanish, Swedish, Turkish, Ukrainian, and Simplified Chinese. The game picks the right language from your client locale. Contributions welcome — the lang files are plain JSON.

<details>
<summary><b>Building from source</b></summary>

### Requirements

- Vintage Story 1.22.0 or later (for the referenced game DLLs)
- .NET 10 SDK

### Setup

1. Install the Vintage Story client at a known location.
2. Set environment variables pointing at your install and data directories:
   - `VINTAGE_STORY` — the game install directory (contains `Vintagestory.exe`).
   - `VINTAGE_STORY_DATA` — the data directory VS uses (contains `Mods/`, `ModConfig/`, `Saves/`).

3. Restart your IDE so it picks up the new variables.
4. Open `StepUpAdvanced.sln`.

### Build & test

```powershell
dotnet build -c Release
dotnet test  -c Release
```

Close Vintage Story before building — a running client locks the PDBs.

### Package a release

```powershell
./build/package.ps1 -Configuration Release -Version <version>
```

Produces a mod-portal-ready zip. CI builds and tests when `VINTAGE_STORY_PATH` points at a Vintage Story install the runner can reach. Pushing a `v*.*.*` tag publishes a GitHub release with notes from the matching CHANGELOG section; attach the packaged zip to it by hand.

### Architecture

The codebase is layered, with pure movement logic kept separate from the Vintage Story API:

- **`Domain/`** — math and decision logic with no game dependencies, each covered by unit tests.
- **`Application/`** — the per-axis controllers, save queue, enforcement coordinator, and command/hotkey registrars.
- **`Configuration/`** — options, persistence, migrations, and the debounced config file watcher.
- **`Infrastructure/`** — the game-facing layer: network sync, input binding, world probes, and reflection helpers.

</details>

## License

[MIT](LICENSE) — see the LICENSE file.

## Credits

Credits: [CopyGirl][CG] => [StepUp][SU]

[CG]: https://mods.vintagestory.at/list/mod?sortby=lastreleased&sortdir=desc&text=&side=&userid=41&mv=
[SU]: https://mods.vintagestory.at/stepup

StepUp Advanced by [Elocrypt](https://github.com/Elocrypt). If you'd like to support development: [Ko-fi](https://ko-fi.com/elo) · [Patreon](https://www.patreon.com/c/Elocrypt) · [Throne](https://throne.com/elocrypt). Discuss in the [VS Discord thread](https://discord.com/channels/302152934249070593/1331031307379146814).