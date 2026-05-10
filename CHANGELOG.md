# Changelog

All notable changes to StepUp Advanced will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Phase 4 (Input layer extraction):**
  - New `Infrastructure/Input/HotkeyBinder.cs` collapses each
    `RegisterHotKey + SetHotKeyHandler` pair into a single
    `binder.Bind(id, name, key, handler)` call. The 12 lines of parallel
    registration in the previous `RegisterHotkeys()` become 6.
  - New `Infrastructure/Input/KeyHoldTracker.cs` encapsulates the
    "fire once per key press" pattern used by the toggle and reload
    hotkeys. Self-subscribes to `capi.Event.KeyUp` for its hotkey's
    current keycode (resolved per-event, so runtime remaps continue to
    work). The two `*KeyHeld` bool fields and the inline
    multi-condition KeyUp delegate are gone.
  - New `Infrastructure/Input/MessageDebouncer.cs` owns toast-suppression
    state via nine named `OnceFlag` properties — one per distinct toast.
    Replaces six shared-purpose `hasShown*` bool fields on
    `StepUpAdvancedModSystem`. Typed handles, no string keys. See Fixed
    below for the bugs this resolves.
  - `StepUpAdvancedModSystem` ~30 lines smaller; behavior preserved
    except for the toast-suppression bug fixes below.
  - **Tests:** new `tests/StepUpAdvanced.Tests/Infrastructure/Input/MessageDebouncerTests.cs`
    with 11 cases covering `OnceFlag`'s show-once-until-reset contract
    and `MessageDebouncer`'s flag independence (one positive case per
    historical shared-flag bug). `KeyHoldTracker` and `HotkeyBinder` are
    thin adapters over VS event APIs and aren't unit-tested — wrapping
    `ICoreClientAPI` for testability would add more code than it
    verifies.

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

- **Phase 4 (toast suppression — five distinct shared-flag bugs):**
  Six `hasShown*` bool fields were each driving multiple unrelated
  toast suppressions, causing toasts to be silently swallowed when an
  unrelated condition had already fired earlier in the session:
  - `hasShownMaxMessage` was shared across `OnIncreaseStepHeight`
    (height-at-max), `OnIncreaseElevateFactor` (speed-at-max), AND
    `OnReloadConfig` (reload-blocked). Hitting the height cap
    suppressed the next speed-at-max or reload-blocked toast even
    though the user had never seen them.
  - `hasShownMinMessage` was shared between `OnDecreaseStepHeight` and
    `OnDecreaseElevateFactor` — same cross-axis suppression at the floor.
  - `hasShownMaxEMessage` and `hasShownMinEMessage` were each shared
    across both axes for the "server-enforced – {height,speed}-change-blocked"
    toasts. Direction was incongruously baked into the flag name even
    though the toast text doesn't distinguish increase from decrease,
    so a single per-axis flag is the right shape.
  - `OnDecreaseElevateFactor`'s post-clamp branch gated the at-min
    toast on `hasShownMinEMessage` — the wrong flag (that one is for
    the enforced-blocked toast on the same handler). So if the user
    had bounced off the enforced-blocked toast earlier in the session,
    reaching the speed floor afterwards would silently swallow the
    "min-speed" toast. Single-character typo, real user-visible bug.
  Each toast now has its own `OnceFlag` instance:
  `HeightAtMax`, `HeightAtMin`, `SpeedAtMax`, `SpeedAtMin`,
  `HeightEnforced`, `SpeedEnforced`, `HeightSpeedOnlyMode`,
  `ReloadBlocked`, `ServerEnforcement`.

- **Phase 4 polish (post-smoke-test):** `OnDecreaseStepHeight` and
  `OnDecreaseElevateFactor` no longer double-emit on the descending key
  press that lands at the client floor. Previously the post-clamp block
  fired the "Minimum height" / "Minimum speed" limit toast AND then
  unconditionally followed it with the generic "Height » 0.6" /
  "Speed » 0.7" update — redundant since the limit toast already
  carries the value. The generic update is now suppressed when the
  press hits the floor; ascending toward the cap is unchanged (one
  toast per press: generic on the press that lands at cap, limit on
  the next press that bounces off it). Pre-existing UX bug, not a
  Phase 4 regression — Phase 4 surfaced it during smoke testing.

- **Phase 3b hotfix-of-the-hotfix:** Single-player no longer silently
  destroys server-side configuration:
  - `ServerEnforceSettings = true` is now honored verbatim from disk in
    every context, including single-player. Three load/save/receive sites
    were forcing the flag to `false` whenever they detected a
    single-player client (the original justification — "no remote
    authority to enforce against" — doesn't hold for a player who
    explicitly opted in to capping themselves). The flag means what it
    says: the player IS the server admin in SP and can enforce caps
    and the server blacklist on themselves.
  - `EnforcementState.IsEnforced` collapsed to a single check on
    `config.ServerEnforceSettings`. The `side` and `isSinglePlayer`
    parameters remain in the signature for API stability and future
    asymmetric rules; their use in the body is gone. Matching change
    in `ConfigStore.IsEnforcedForThisSide`. The previous
    "single-player short-circuit" rule is now reversed in
    `EnforcementStateTests.SinglePlayerClient_WithFlagOn_Enforces`.
  - `/sua add`, `/sua remove`, `/sua remove all`, and `/sua reload`
    now persist correctly in single-player. `ConfigStore.Save` was
    returning early on non-dedicated server side ("integrated server
    in single-player would otherwise race the client's own save") —
    but the global mutex + in-process lock already serialize concurrent
    writers, so that early-return was just dropping `/sua add`'s changes
    on the floor. Removed.
  - `Infrastructure/Network/ConfigSyncChannel` is now wired to
    `ConfigSyncPacket` end-to-end. `RegisterMessageType<StepUpOptions>()`
    and `SetMessageHandler<StepUpOptions>(...)` swapped to
    `<ConfigSyncPacket>`; `BroadcastToAll` and `OnPlayerJoin` build the
    wire packet via `ConfigSyncPacketMapper.ToPacket`. Phase 3b had
    introduced the packet type and mapper but never finished the channel
    wiring — `ToPacket` was dead code, and the handler-signature
    mismatch (`OnReceiveServerConfig(ConfigSyncPacket)` vs.
    `NetworkServerMessageHandler<StepUpOptions>`) broke the build.

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
