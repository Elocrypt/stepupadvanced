# Changelog

All notable changes to StepUp Advanced will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Phase 3a (Network layer split — channel extraction):**
  - New `Infrastructure/Network/ConfigSyncChannel.cs` owns the network
    channel registration on both sides, the player-join push, and the
    broadcast helper used by the config-file watcher and `/sua reload`.
    Five `"stepupadvanced"` channel-name string references collapse into
    one `internal const string ChannelName` on the new class.
  - `StepUpAdvancedModSystem.OnPlayerJoin` removed; the equivalent
    handler now lives privately on `ConfigSyncChannel`. The `IsEnforced`
    gate is gone (see Fixed).
  - Three inline `sapi.Network.GetChannel(...).SendPacket(...)` blocks
    (in `OnPlayerJoin`, `OnConfigFileChanged`, and `ReloadServerConfig`)
    collapse into single `configSyncChannel.BroadcastToAll()` /
    join-handler calls. The channel reference is now cached at
    registration time rather than re-fetched per send.
  - Wire DTO unchanged for this sub-phase (still `StepUpOptions`).
    Phase 3b introduces a narrow `ConfigSyncPacket` and decouples
    persistence shape from wire shape.
  - `StepUpAdvancedModSystem` net 21 lines smaller; no behavior change
    other than the join-broadcast fix below.

- **Phase 2c (Configuration split — blacklist + watcher extraction):**
  - `BlockBlacklistConfig` class renamed to `BlockBlacklistOptions` and
    moved to `Configuration/BlockBlacklistOptions.cs`. Pure data class,
    same JSON file name on disk (`StepUpAdvanced_BlockBlacklist.json`).
  - New `Configuration/BlockBlacklistStore.cs` owns load and save logic.
    The 4 logger calls that were missed in Phase 1's `ModLog` migration
    now route through `ModLog`.
  - All call sites updated: 9 references in `StepUpAdvancedModSystem.cs`.
  - New `Configuration/DebouncedConfigWatcher.cs` extracted from the
    inline FileSystemWatcher + debounce-timer + suppress-flag plumbing
    in `StepUpAdvancedModSystem`. Self-contained class with a clean
    event-based API (`event Action ConfigFileChanged`), an explicit
    `Suppress(durationMs)` method that replaces the previous
    set-flag-then-schedule-callback pattern, and a single re-armable
    suppress-clear timer (avoids per-call `Threading.Timer` allocation).
  - The suppress flag is now `volatile` (was a plain `bool` previously),
    matching the audit note about the FileSystemWatcher's thread-pool
    callback racing with the main-thread writer.
  - `StepUpAdvancedModSystem` shrinks by ~30 lines: the inline watcher
    code is replaced with `new DebouncedConfigWatcher(...)` plus a thin
    `OnConfigFileChanged` handler. Unused `using System.Timers;` removed.

- **Phase 2b (Configuration split — migrations):**
  - New `Configuration/Migrations/` folder with one file per concern:
    - `IConfigMigration.cs` — interface for framework-free, idempotent
      schema transformations. Pure: takes only `StepUpOptions`, returns
      `bool`.
    - `MigrationRunner.cs` — orchestrator. Applies registered migrations
      in target-version order, gating each by the running version.
    - `MigrationToV2.cs` — extracted from the inline `Migrate` method.
      Brings configs from v0/v1 up to v2 (forward-probe defaults).
  - `ConfigStore.MergeAndMigrate` now delegates to `MigrationRunner.Run`.
    The old inline `Migrate` method is gone. The `ICoreAPI` parameter that
    the previous `Migrate` accepted-but-ignored is dropped at the migration
    layer — migrations are now genuinely framework-free.
  - The `BlockBlacklist ??= new List<string>()` defensive null-guard moved
    from the old `Migrate` to `Normalize` (where invariant enforcement
    lives). Behavior unchanged.
- **Tests:** Added `MigrationRunnerTests` (6 cases: gating, idempotence,
  negative-version handling, schema-version-untouched contract) and
  `MigrationToV2Tests` (9 cases covering the specific transformation:
  zero-values, negative values, partial fixes, no-op-on-positive,
  no-other-fields-touched). 15 new cases in this phase, 28 cumulative.

- **Phase 2a (Configuration split — rename + ConfigStore extraction):**
  - `StepUpAdvancedConfig` class renamed to `StepUpOptions` and moved to
    `src/StepUpAdvanced/Configuration/StepUpOptions.cs`. Now a pure data
    class — only `[ProtoMember]` properties and the `Current` global accessor.
  - All imperative concerns (`Save`, `LoadOrUpgrade`, `MergeAndMigrate`,
    `Migrate`, `Normalize`, `NormalizeCaps`, `UpdateConfig`) moved to a new
    `Configuration/ConfigStore.cs`. Schema constants and the cross-process
    save mutex live there too.
  - **On-disk file name unchanged** (`StepUpAdvancedConfig.json`). Existing
    user configs load identically. The wire-protocol format (protobuf member
    numbers) is also unchanged — the rename is .NET-side only.
  - All call sites updated: 76 references in `StepUpAdvancedModSystem.cs`,
    6 in the Harmony patch, 2 in `Core/`, 4 in tests.

- **Phase 1 (Core extraction):** Cross-cutting helpers moved to
  `src/StepUpAdvanced/Core/`:
  - `SuaChat` (chat formatting) → `Core/ChatFormatting`. Marked `internal`.
    `using SuaChat = ...` alias kept in `StepUpAdvancedModSystem` so existing
    call sites compile unchanged; the alias is removed in Phase 8.
  - `SuaCmd` (text command result builders) → `Core/CommandResults`. Same
    alias treatment as above.
  - New `Core/ModLog`: every log call now routes through this helper, which
    prefixes `[StepUp Advanced]` exactly once. Previously 30 of 38 logger
    calls had the prefix and 8 didn't — log output is now consistent.
  - New `Core/EnforcementState`: pure (framework-free) helper for the
    "is server enforcement currently active" predicate. The
    `StepUpAdvancedModSystem.IsEnforced` property delegates to it; behavior
    is identical, including the original guard that returns false when
    neither side has initialized yet.
- **Tests:** `EnforcementStateTests` added — 11 cases covering the full
  matrix of (side, isSinglePlayer, config-flag) combinations.

- **Phase 0 (repo skeleton):** Project moved to `src/StepUpAdvanced/` layout to
  match the layered structure used by HUD Clock. Test project added at
  `tests/StepUpAdvanced.Tests/`. C# namespace renamed from `stepupadvanced` to
  `StepUpAdvanced` (PascalCase). The mod ID, network channel name, and Harmony
  patch ID remain `stepupadvanced` — protocol identifiers, unchanged.
- Build settings centralized in `Directory.Build.props`. Vintage Story DLL
  references now use `<Private>false</Private>` so the mod output folder no
  longer contains copies of the engine DLLs.
- `modinfo.json` and `assets/` are now copied to the build output via explicit
  `<None Include>` entries; previously they were excluded from compilation but
  not copied.

### Added

- `.editorconfig` with C# conventions matching the HUD Clock house style.
- `build/package.ps1` for producing release zips.
- `.github/workflows/ci.yml` and `release.yml` for CI and tagged releases.

### Fixed

- **Phase 3a:** `OnPlayerJoin` now broadcasts the current server options
  unconditionally on every player join, instead of only when enforcement
  was active. This fixes the case where a server toggled enforcement off
  while a client was disconnected: the client would reconnect and never
  be told the new state, leaving it stuck behind stale server limits
  until something else triggered a broadcast (a `/sua reload`, a
  config-file edit, etc.). The single-player path is unaffected — the
  client-side handler still short-circuits enforcement on
  `capi.IsSinglePlayer`.

[Unreleased]: https://github.com/Elocrypt/stepupadvanced/compare/v1.2.4...HEAD
