# Changelog

All notable changes to StepUp Advanced will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.4.0] - 2026/06/09

### Added

- **Sprint-only step-up** (`SprintOnlyStepUp`, default off). The enhanced step
  height applies only while you're sprinting; the rest of the time you step at
  the vanilla height. Client-side preference — not affected by server
  enforcement.
- **Disable step-up while sneaking** (`DisableStepUpWhileSneaking`, default off).
  Suppresses the enhanced step height while sneaking, so you can sneak for
  precise edge placement without auto-stepping.
- **Disable step-up while airborne** (`DisableStepUpWhileAirborne`, default off).
  Keeps the enhanced step height from engaging while you're off the ground, so a
  tall step height can't extend your jump reach onto ledges you fell just short
  of. Mirrors the game's own on-ground/swimming step rule, so it never affects
  normal ground steps.

### Changed

- Internal rewrite to a layered, unit-tested architecture (pure movement logic
  separated from the game API; per-axis controllers; dedicated networking,
  input, config, and command/hotkey components). No change to gameplay,
  settings, or keybinds — existing configs load unchanged.
- Ceiling detection is more precise near partial blocks. Slabs, snow layers, and
  other partial-height blocks now register their true height instead of being
  treated as full cells, so the Ceiling Guard no longer holds you down under a
  block that isn't actually in the way.

### Fixed

- **Sprint step-up could stop working after extended play.** After certain
  events (respawning, changing dimension, or another mod resetting the player's
  physics) the enhanced step height could silently revert to vanilla and stay
  there until a reload or toggle — most noticeable while sprinting, since
  sprinting meets the taller ledges a vanilla step can't clear. Step height is
  now re-asserted continuously, so it recovers on its own.
- **Step-up could trigger mid-air and over-extend jumps.** A tall step height
  could snap you up onto a ledge during the brief ground contact of a landing,
  letting you clear gaps you'd otherwise fall short of. The new
  *disable while airborne* option keeps the step height at vanilla reach while
  you're off the ground.
- **Crash when stepping near unusual blocks at the edge of generated terrain.**
  The proximity check that disables step-up near blacklisted blocks could throw
  a `NullReferenceException` when it looked into an ungenerated chunk beside a
  freshly generated structure (reported with a Shale Boulder on a BetterRuins
  structure). It now handles missing blocks safely.
- **Blacklist and config commands now persist correctly in single-player.**
  `/sua add`, `/sua remove`, `/sua remove all`, and `/sua reload` were being
  dropped on the integrated server in single-player; they now save reliably.
- **Servers re-sync configuration on every join.** A client that reconnected
  after the server changed its enforcement settings could stay stuck behind the
  old limits; the server now sends the current configuration on each join.
- **Notification messages are no longer swallowed or duplicated.** Limit and
  status toasts (height/speed at max or min, server-enforced, reload-blocked)
  are tracked independently, so hitting one limit no longer suppresses an
  unrelated message, and pressing a key down to the floor no longer emits a
  redundant second toast.

## [1.3.0] - 2026/04/24

### Changed

- Updated for Vintage Story 1.22.0 compatibility.

## [1.2.4] - 2025/12/05

### Added

- Config-save debouncing: all client-side saves now queue through a single safe
  callback, eliminating overlapping writes and race conditions with the file
  watcher.
- Watcher suppression hook: internal guard explicitly prevents reload loops when
  the server writes config.

### Changed

- Blacklist merging: server and client lists now combine deterministically and
  sort stably.
- Toggle hotkeys: duplicate press detection improved, preventing accidental
  double-toggles.
- Harmony patch safety: reflection searches broadened for modded variations of
  `elevateFactor`.
- Non-critical state messages demoted from `Event` to `VerboseDebug`, reducing
  log noise significantly on verbose servers.

### Fixed

- Config-save spam: the mod no longer writes dozens of `[Event] Saved
  configuration.` lines during play.
- FileWatcher multi-fire: rapid edits no longer cause repeated reloads.
- StepHeight reapply flood: small float jitter no longer causes redundant
  physics writes.
- Missing player/physics NREs: validation added before applying movement
  properties.
- Server enforcement race: clients reliably receive the enforced config once and
  no longer re-announce repeatedly.

## [1.2.3] - 2025/10/03

### Added

- **QuietMode** (server sync): when enabled, hides all StepUp Advanced chat
  messages on clients except hard errors. Set `"QuietMode": true` to silence
  chat lines from this mod; use alongside your own server banner.

### Changed

- File watcher: replaced naive sleeps with a 150 ms timer debounce; eliminates
  multi-fire on Windows.
- Ceiling guard: the local (here) probe now applies `CeilingHeadroomPad`
  consistently with forward probes.
- Transpiler safety: constant swaps preserve labels/blocks and no-op if target
  constants aren't found, preventing fragile patching.

### Fixed

- Banner flip on fresh connect: ProtoBuf default handling corrected via
  `[DefaultValue(true)]` so server `false` is transmitted and respected.
- Watcher echo cases that could cause double reload/push on quick successive
  edits.

## [1.2.2] - 2025/10/02

### Added

- Auto-migration pipeline: loads old JSON, fills new keys with safe defaults,
  repairs zeroed ranges, and writes back once — no more manual config deletion
  on update.
- `ShowServerEnforcedNotice` (default `true`): server owners can suppress the
  one-time "server enforced" chat notice to keep their own banner visible.
- Blacklist schema: `StepUpAdvanced_BlockBlacklist.json` now has its own
  schema/versioning; entries are deduped and sorted.

### Changed

- Save policy: single-player client writes; dedicated server writes; SP
  integrated server does not write (prevents double-writes).
- Debounced saves: hotkey/UI spam coalesces into one save (~200 ms).
- Runtime clamping: when enforcement is ON, height and speed are clamped to
  both server min and max, not just max.
- Watcher echo suppressed: server-initiated reloads avoid duplicate file-watch
  broadcasts.

### Fixed

- Rare crash on config save with "file in use by another process" (race vs.
  AV/indexer/cloud sync).
- Stale/zeroed values after updates when old configs lacked new fields — no
  config regeneration required.
- Occasional duplicate client pushes after `/reload` scenarios.

## [1.2.1] - 2025/08/25

Port of v1.1.1 to Vintage Story 1.21.x. Includes all additions, changes, and
fixes from that release.

## [1.2.0] - 2025/08/12

### Added

- **Speed-only mode** (`SpeedOnlyMode`): lets another mod (e.g., XSkills) own
  step height while this mod manages speed only. Height hotkeys are disabled
  when active; requires a relog or config reload to switch reliably.
- **Ceiling guard and forward probe**: prevents micro-bumping into low ceilings
  or floating roofs ahead of you. Options: `CeilingGuardEnabled`,
  `ForwardProbeCeiling`, `ForwardProbeDistance`, `RequireForwardSupport`.
- Localization: multi-language support added.

### Changed

- Chat output reformatted and colorized for better readability.
- Clamping rules: height and speed hotkeys apply a client floor (no negatives);
  server caps only apply when `ServerEnforceSettings = true`.
- Performance: caches reflection (`StepHeight`/`elevateFactor` fields), skips
  writes when the value hasn't changed, and reuses a scratch `BlockPos` to
  reduce allocations.

### Fixed

- Step speed doing nothing: the elevate factor is now properly written every
  tick even when the backing field is private; warns once if no compatible field
  can be found.

### Removed

- Experimental step-down logic removed due to collision/cache constraints.

## [1.1.1] - 2025/08/25

### Added

- `ForwardProbeSpan` — horizontal sweep width (blocks) when checking the
  forward ceiling.
- `CeilingHeadroomPad` — extra headroom margin (0.00–0.25 suggested) to avoid
  grazing ceilings.

### Changed

- Ceiling logic: forward probe respects support rules more narrowly; headroom
  clamping favors real traversable gaps over conservative blocks.
- Clamping path: client floors still apply (height ≥ 0.2; speed ≥ 0.6), but
  server caps only bind when enforcement is ON.
- Command outputs: unified success/warn/error styling with clearer values and
  reasons.
- Internals: cached field lookups, skip-same-value writes, and a small scratch
  position to reduce per-tick allocations.

### Fixed

- Players snapping upward under flat overhangs despite adequate headroom.
- Edge scenarios where legitimate climbs were blocked when a nearby ceiling was
  present.
- Minor chat spam/duplication on repeated enforcement notifications.

## [1.1.0] - 2025/08/12

### Fixed

- Ceiling guard & forward probe not properly detecting ledges.

[Unreleased]: https://github.com/Elocrypt/stepupadvanced/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/Elocrypt/stepupadvanced/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/Elocrypt/stepupadvanced/compare/v1.2.4...v1.3.0
[1.2.4]: https://github.com/Elocrypt/stepupadvanced/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/Elocrypt/stepupadvanced/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/Elocrypt/stepupadvanced/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/Elocrypt/stepupadvanced/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/Elocrypt/stepupadvanced/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/Elocrypt/stepupadvanced/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/Elocrypt/stepupadvanced/releases/tag/v1.1.0