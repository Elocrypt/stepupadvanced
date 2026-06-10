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

[Unreleased]: https://github.com/Elocrypt/stepupadvanced/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/Elocrypt/stepupadvanced/compare/v1.3.0...v1.4.0
[1.4.0]: https://github.com/Elocrypt/stepupadvanced/releases/tag/v1.4.0