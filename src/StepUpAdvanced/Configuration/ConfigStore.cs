using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using StepUpAdvanced.Configuration.Migrations;
using StepUpAdvanced.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Static facade for loading, saving, and migrating <see cref="StepUpOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// All imperative concerns that used to live on <c>StepUpAdvancedConfig</c>
/// (load/save/migrate/normalize/validate) live here. <see cref="StepUpOptions"/>
/// itself is now a pure data class.
/// </para>
/// <para>
/// Phase 2b will pull <see cref="Migrate"/> out into the
/// <c>Configuration/Migrations/</c> folder with one file per schema version.
/// For now, the existing single-method form is preserved.
/// </para>
/// </remarks>
internal static class ConfigStore
{
    /// <summary>
    /// Latest config schema version. Incremented when a load-time migration
    /// is required for older configs.
    /// </summary>
    public const int LatestSchema = 4;

    /// <summary>
    /// On-disk JSON file name. Must remain <c>StepUpAdvancedConfig.json</c> —
    /// changing it would orphan every existing user's saved config.
    /// </summary>
    private const string FileName = "StepUpAdvancedConfig.json";

    /// <summary>
    /// Cross-process mutex name. Multiple VS processes (e.g. dev workflows
    /// with both a server and a client running) need to coordinate writes.
    /// </summary>
    private const string GlobalMutexName = "Global\\VS-StepUpAdvanced-Config";

    /// <summary>
    /// In-process write lock. Held for the duration of a save.
    /// </summary>
    private static readonly object _saveLock = new();

    /// <summary>
    /// Floor value for the local client's <c>StepHeight</c> setting.
    /// Independent of server caps — applies even with enforcement off.
    /// </summary>
    private const float ClientMinStepHeight = 0.6f;

    /// <summary>Floor value for the local client's <c>StepSpeed</c> setting.</summary>
    private const float ClientMinStepSpeed = 0.7f;

    /// <summary>Hard ceiling for server-cap properties; protects against pathological config values.</summary>
    private const float ServerCapMinHeight = 0.6f;

    /// <summary>Hard ceiling for server-cap properties; protects against pathological config values.</summary>
    private const float ServerCapMaxHeight = 20f;

    /// <summary>Hard ceiling for server-cap properties; protects against pathological config values.</summary>
    private const float ServerCapMinSpeed = 0.7f;

    /// <summary>Hard ceiling for server-cap properties; protects against pathological config values.</summary>
    private const float ServerCapMaxSpeed = 20f;

    /// <summary>
    /// Loads the config from disk, runs schema migrations and runtime
    /// invariant checks (clamping out-of-range values), and writes back to
    /// disk if anything changed. Idempotent across calls — running
    /// <c>LoadOrUpgrade</c> twice with no external file changes yields the
    /// same on-disk state.
    /// </summary>
    public static void LoadOrUpgrade(ICoreAPI api)
    {
        api.GetOrCreateDataPath("ModConfig");

        var loadedConfig = api.LoadModConfig<StepUpOptions>(FileName);
        bool created = false;

        if (loadedConfig == null)
        {
            StepUpOptions.Current = new StepUpOptions { SchemaVersion = LatestSchema };
            created = true;
            Save(api);
            ModLog.Event(api, "Created new config (schema v{0}).", LatestSchema);
            return;
        }
        else
        {
            StepUpOptions.Current = loadedConfig;
        }

        bool changed = MergeAndMigrate(api, loadedConfig, out var merged);
        StepUpOptions.Current = merged;

        if (Normalize(api, StepUpOptions.Current)) changed = true;

        if (changed)
        {
            Save(api);
            ModLog.Event(api, "Auto-upgraded config to schema v{0}.", StepUpOptions.Current.SchemaVersion);
        }

        bool dirty = created;

        dirty |= NormalizeCaps(api);

        if (StepUpOptions.Current.StepHeightIncrement < 0.1f)
        {
            ModLog.Warning(api, "StepHeightIncrement in config is below minimum allowed value (0.1). Adjusting to minimum.");
            StepUpOptions.Current.StepHeightIncrement = 0.1f; dirty = true;
        }
        if (StepUpOptions.Current.StepSpeedIncrement < 0.1f)
        {
            ModLog.Warning(api, "StepSpeedIncrement in config is below minimum allowed value (0.1). Adjusting to minimum.");
            StepUpOptions.Current.StepSpeedIncrement = 0.1f; dirty = true;
        }

        if (StepUpOptions.Current.DefaultHeight < StepUpOptions.Current.ServerMinStepHeight || StepUpOptions.Current.DefaultHeight > StepUpOptions.Current.ServerMaxStepHeight)
        {
            ModLog.Warning(api,
                $"DefaultHeight in config is out of bounds (allowed range: {StepUpOptions.Current.ServerMinStepHeight} - {StepUpOptions.Current.ServerMaxStepHeight}). Resetting to min ({StepUpOptions.Current.ServerMinStepHeight}).");
            StepUpOptions.Current.DefaultHeight = StepUpOptions.Current.ServerMinStepHeight; dirty = true;
        }
        if (StepUpOptions.Current.DefaultSpeed < StepUpOptions.Current.ServerMinStepSpeed || StepUpOptions.Current.DefaultSpeed > StepUpOptions.Current.ServerMaxStepSpeed)
        {
            ModLog.Warning(api,
                $"DefaultSpeed in config is out of bounds (allowed range: {StepUpOptions.Current.ServerMinStepSpeed} - {StepUpOptions.Current.ServerMaxStepSpeed}). Resetting to min ({StepUpOptions.Current.ServerMinStepSpeed}).");
            StepUpOptions.Current.DefaultSpeed = StepUpOptions.Current.ServerMinStepSpeed; dirty = true;
        }

        bool enforceCaps = IsEnforcedForThisSide();

        float stepHeight = StepUpOptions.Current.StepHeight;
        if (stepHeight < ClientMinStepHeight)
        {
            ModLog.Warning(api, $"StepHeight below minimum ({ClientMinStepHeight}). Adjusting to {ClientMinStepHeight}.");
            stepHeight = ClientMinStepHeight; dirty = true;
        }
        if (enforceCaps)
        {
            float clamped = GameMath.Clamp(stepHeight, StepUpOptions.Current.ServerMinStepHeight, StepUpOptions.Current.ServerMaxStepHeight);
            if (clamped != stepHeight)
            {
                ModLog.Warning(api, $"StepHeight outside enforced server range ({StepUpOptions.Current.ServerMinStepHeight}..{StepUpOptions.Current.ServerMaxStepHeight}). Adjusting.");
                stepHeight = clamped; dirty = true;
            }
        }
        if (stepHeight != StepUpOptions.Current.StepHeight) StepUpOptions.Current.StepHeight = stepHeight;
        ModLog.Verbose(api, $"Config Loaded: StepHeight = {StepUpOptions.Current.StepHeight}");

        float stepSpeed = StepUpOptions.Current.StepSpeed;
        if (stepSpeed < ClientMinStepSpeed)
        {
            ModLog.Warning(api, $"StepSpeed below minimum ({ClientMinStepSpeed}). Adjusting to {ClientMinStepSpeed}.");
            stepSpeed = ClientMinStepSpeed; dirty = true;
        }
        if (enforceCaps)
        {
            float clamped = GameMath.Clamp(stepSpeed, StepUpOptions.Current.ServerMinStepSpeed, StepUpOptions.Current.ServerMaxStepSpeed);
            if (clamped != stepSpeed)
            {
                ModLog.Warning(api, $"StepSpeed outside enforced server range ({StepUpOptions.Current.ServerMinStepSpeed}..{StepUpOptions.Current.ServerMaxStepSpeed}). Adjusting.");
                stepSpeed = clamped; dirty = true;
            }
        }
        if (stepSpeed != StepUpOptions.Current.StepSpeed) StepUpOptions.Current.StepSpeed = stepSpeed;
        ModLog.Verbose(api, $"Config Loaded: StepSpeed = {StepUpOptions.Current.StepSpeed}");

        // Note: <c>ServerEnforceSettings</c> is honored verbatim from disk in
        // every context, including single-player. The player IS the server
        // admin in SP and may legitimately opt in to enforcing caps and the
        // server blacklist on themselves. Earlier hotfix code clobbered the
        // flag to <c>false</c> here on every load — that was silent data
        // loss and is gone.

        int fwdOld = StepUpOptions.Current.ForwardProbeDistance;
        StepUpOptions.Current.ForwardProbeDistance = GameMath.Clamp(StepUpOptions.Current.ForwardProbeDistance, 0, 4);
        if (StepUpOptions.Current.ForwardProbeDistance != fwdOld)
        {
            ModLog.Verbose(api, $"Normalized ForwardProbeDistance to {StepUpOptions.Current.ForwardProbeDistance}");
            dirty = true;
        }

        StepUpOptions.Current.ForwardProbeSpan = GameMath.Clamp(StepUpOptions.Current.ForwardProbeSpan, 0, 2);
        StepUpOptions.Current.CeilingHeadroomPad = GameMath.Clamp(StepUpOptions.Current.CeilingHeadroomPad, 0f, 0.4f);

        ModLog.Verbose(api,
            $"Flags: " +
            $"SpeedOnly={(StepUpOptions.Current.SpeedOnlyMode ? "on" : "off")}, " +
            $"HarmonyTweaks={(StepUpOptions.Current.EnableHarmonyTweaks ? "on" : "off")}, " +
            $"CeilingGuard={(StepUpOptions.Current.CeilingGuardEnabled ? "on" : "off")}, " +
            $"ForwardProbe={(StepUpOptions.Current.ForwardProbeCeiling ? "on" : "off")} (dist={StepUpOptions.Current.ForwardProbeDistance})"
        );

        if (dirty) Save(api);
    }

    /// <summary>
    /// Reflectively merges the loaded config into a fresh defaults instance,
    /// then runs schema-version-specific migrations on the result.
    /// </summary>
    private static bool MergeAndMigrate(ICoreAPI api, StepUpOptions loaded, out StepUpOptions merged)
    {
        bool changed = false;

        var def = new StepUpOptions();

        foreach (var p in typeof(StepUpOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite) continue;
            var val = p.GetValue(loaded);

            if (p.PropertyType == typeof(List<string>))
            {
                if (val == null)
                {
                    changed = true;
                    continue;
                }
            }

            p.SetValue(def, val);
        }

        int from = Math.Max(0, loaded.SchemaVersion);

        changed |= MigrationRunner.Run(def, from);

        if (def.SchemaVersion != LatestSchema) { def.SchemaVersion = LatestSchema; changed = true; }

        merged = def;
        return changed;
    }

    /// <summary>
    /// Runtime invariant enforcement that runs every load. Differs from
    /// <see cref="Migrate"/> in that it's not version-gated — it always
    /// runs and re-establishes invariants regardless of where the config
    /// came from.
    /// </summary>
    private static bool Normalize(ICoreAPI api, StepUpOptions cfg)
    {
        bool dirty = false;

        // Defensive null-guard for the BlockBlacklist collection. The default
        // constructor initializes a fresh list and MergeAndMigrate preserves
        // non-null lists from disk, so this is normally a no-op — but kept
        // here as a safety net (was previously in Migrate before Phase 2b
        // separated migrations from invariant enforcement).
        if (cfg.BlockBlacklist == null)
        {
            cfg.BlockBlacklist = new List<string>();
            dirty = true;
        }

        float oldMinH = cfg.ServerMinStepHeight, oldMaxH = cfg.ServerMaxStepHeight;
        float oldMinS = cfg.ServerMinStepSpeed, oldMaxS = cfg.ServerMaxStepSpeed;

        if (oldMinH <= 0f) { cfg.ServerMinStepHeight = 0.6f; dirty = true; }
        if (oldMaxH <= 0f) { cfg.ServerMaxStepHeight = 1.2f; dirty = true; }
        if (oldMinS <= 0f) { cfg.ServerMinStepSpeed = 0.7f; dirty = true; }
        if (oldMaxS <= 0f) { cfg.ServerMaxStepSpeed = 1.3f; dirty = true; }

        if (cfg.ServerMinStepHeight > cfg.ServerMaxStepHeight)
        {
            (cfg.ServerMinStepHeight, cfg.ServerMaxStepHeight) = (cfg.ServerMaxStepHeight, cfg.ServerMinStepHeight);
            dirty = true;
        }
        if (cfg.ServerMinStepSpeed > cfg.ServerMaxStepSpeed)
        {
            (cfg.ServerMinStepSpeed, cfg.ServerMaxStepSpeed) = (cfg.ServerMaxStepSpeed, cfg.ServerMinStepSpeed);
            dirty = true;
        }

        if (cfg.ForwardProbeSpan < 1) { cfg.ForwardProbeSpan = 1; dirty = true; }
        if (cfg.ForwardProbeDistance < 1) { cfg.ForwardProbeDistance = 1; dirty = true; }

        return dirty;
    }

    /// <summary>
    /// Persists the current config to disk. Cross-process and cross-thread
    /// safe via global mutex + in-process lock + retry-with-backoff.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Client-side blacklist isolation:</b> when called with an
    /// <see cref="ICoreClientAPI"/> AND <c>!IsSinglePlayer</c>, the
    /// server-managed <c>BlockBlacklist</c> is temporarily swapped out
    /// for an empty list before writing, then restored in a
    /// <c>finally</c> block. The client never legitimately owns server
    /// blacklist data on a remote server — pre-3b the wholesale-replace
    /// receive path would deposit the remote server's list into
    /// <c>StepUpOptions.Current</c>, and any subsequent hotkey-triggered
    /// save would persist it into the player's local server-side config
    /// file. That polluted file would then haunt single-player sessions
    /// until manually cleaned. The swap closes that hole at the
    /// persistence layer; the Phase 3b runtime gate in
    /// <c>IsNearBlacklistedBlock</c> handles the in-memory side.
    /// Existing polluted files self-heal on the next remote-MP client
    /// save.
    /// </para>
    /// <para>
    /// In single-player the swap is bypassed: the player IS the server
    /// admin and legitimately owns the local <c>StepUpAdvancedConfig.json</c>,
    /// including its <c>BlockBlacklist</c>. <c>/sua add</c> in single-player
    /// persists through the server save path; <c>.sua add</c> writes to the
    /// separate <c>StepUpAdvanced_BlockBlacklist.json</c>. Both work.
    /// </para>
    /// </remarks>
    public static void Save(ICoreAPI api)
    {
        // Guarded blacklist swap — see remarks above.
        // Gated on !IsSinglePlayer: in single-player the player IS the
        // server admin and legitimately owns the blacklist, so we let
        // the full Current (including BlockBlacklist) persist through
        // the client save path. In remote multiplayer the swap fires
        // and prevents the server's pushed list from polluting the
        // client's local server-side config file.
        bool isClientSide = api is ICoreClientAPI;
        bool isRemoteMultiplayerClient = isClientSide && !((ICoreClientAPI)api).IsSinglePlayer;
        List<string>? savedBlacklist = null;
        if (isRemoteMultiplayerClient)
        {
            savedBlacklist = StepUpOptions.Current.BlockBlacklist;
            StepUpOptions.Current.BlockBlacklist = new List<string>();
        }

        try
        {
            lock (_saveLock)
            {
                using var mutex = new Mutex(false, GlobalMutexName);
                bool got = false;
                try { got = mutex.WaitOne(500); } catch { }

                if (!got)
                {
                    ModLog.Warning(api, "Could not acquire config save mutex; skipping save.");
                    return;
                }
                const int maxAttempts = 8;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        api.StoreModConfig(StepUpOptions.Current, FileName);
                        ModLog.Verbose(api, "Saved configuration.");
                        return;
                    }
                    catch (IOException ioex)
                    {
                        int delayMs = 15 * (1 << Math.Min(attempt, 6)) + new Random().Next(0, 10);
                        ModLog.Warning(api, "Save {0}/{1} failed: {2}. Retrying in {3} ms.",
                            attempt, maxAttempts, ioex.Message, delayMs);
                        Thread.Sleep(delayMs);
                    }
                }
                ModLog.Error(api, "Could not save configuration after multiple attempts.");
            }
        }
        finally
        {
            // Restore the in-memory blacklist regardless of save outcome,
            // so a save failure or exception doesn't leave the client's
            // runtime view of the server list empty. Only restores when
            // a swap actually happened (remote-MP client save).
            if (isRemoteMultiplayerClient && savedBlacklist != null)
            {
                StepUpOptions.Current.BlockBlacklist = savedBlacklist;
            }
        }
    }

    /// <summary>
    /// Replaces the current options with a new instance.
    /// </summary>
    /// <remarks>
    /// As of Phase 3b, this method is no longer called from the network
    /// receive path — the wholesale-replace was the bug that motivated
    /// the wire-shape decoupling. The receive handler now uses
    /// <c>ConfigSyncPacketMapper.Apply</c> to merge enforcement-only
    /// fields into <see cref="StepUpOptions.Current"/>, preserving
    /// every client-local field (step height/speed, increments, probe
    /// tunables, QuietMode, etc.).
    ///
    /// Retained as a defensive single-line facade in case a future
    /// caller needs an in-place options swap. Revisit for removal in
    /// Phase 8 once the codebase has settled.
    /// </remarks>
    public static void UpdateConfig(StepUpOptions newConfig) => StepUpOptions.Current = newConfig;

    /// <summary>
    /// Internal predicate: should server caps be enforced during load-time
    /// clamping? After Phase 3b's hotfix-of-the-hotfix, this is just the
    /// flag — single-player is no longer a forced-off case. The player IS
    /// the server admin in SP and may opt in to enforcing caps on themselves.
    /// </summary>
    private static bool IsEnforcedForThisSide()
    {
        return StepUpOptions.Current.ServerEnforceSettings;
    }

    /// <summary>
    /// Clamps server-cap properties to their hard ceilings and ensures
    /// min &lt;= max ordering. Idempotent.
    /// </summary>
    private static bool NormalizeCaps(ICoreAPI api)
    {
        bool dirty = false;

        float oldMinHeight = StepUpOptions.Current.ServerMinStepHeight, oldMaxHeight = StepUpOptions.Current.ServerMaxStepHeight;
        float oldMinSpeed = StepUpOptions.Current.ServerMinStepSpeed, oldMaxSpeed = StepUpOptions.Current.ServerMaxStepSpeed;

        StepUpOptions.Current.ServerMinStepHeight = GameMath.Clamp(StepUpOptions.Current.ServerMinStepHeight, ServerCapMinHeight, ServerCapMaxHeight);
        StepUpOptions.Current.ServerMaxStepHeight = GameMath.Clamp(StepUpOptions.Current.ServerMaxStepHeight, ServerCapMinHeight, ServerCapMaxHeight);
        StepUpOptions.Current.ServerMinStepSpeed = GameMath.Clamp(StepUpOptions.Current.ServerMinStepSpeed, ServerCapMinSpeed, ServerCapMaxSpeed);
        StepUpOptions.Current.ServerMaxStepSpeed = GameMath.Clamp(StepUpOptions.Current.ServerMaxStepSpeed, ServerCapMinSpeed, ServerCapMaxSpeed);

        if (StepUpOptions.Current.ServerMinStepHeight > StepUpOptions.Current.ServerMaxStepHeight)
        {
            ModLog.Warning(api, "ServerMinStepHeight > ServerMaxStepHeight. Adjusting Min to Max.");
            StepUpOptions.Current.ServerMinStepHeight = StepUpOptions.Current.ServerMaxStepHeight;
        }
        if (StepUpOptions.Current.ServerMinStepSpeed > StepUpOptions.Current.ServerMaxStepSpeed)
        {
            ModLog.Warning(api, "ServerMinStepSpeed > ServerMaxStepSpeed. Adjusting Min to Max.");
            StepUpOptions.Current.ServerMinStepSpeed = StepUpOptions.Current.ServerMaxStepSpeed;
        }

        if (oldMinHeight != StepUpOptions.Current.ServerMinStepHeight || oldMaxHeight != StepUpOptions.Current.ServerMaxStepHeight)
        {
            ModLog.Verbose(api, $"Normalized height caps: Min={StepUpOptions.Current.ServerMinStepHeight}, Max={StepUpOptions.Current.ServerMaxStepHeight}");
            dirty = true;
        }
        if (oldMinSpeed != StepUpOptions.Current.ServerMinStepSpeed || oldMaxSpeed != StepUpOptions.Current.ServerMaxStepSpeed)
        {
            ModLog.Verbose(api, $"Normalized speed caps: Min={StepUpOptions.Current.ServerMinStepSpeed}, Max={StepUpOptions.Current.ServerMaxStepSpeed}");
            dirty = true;
        }

        return dirty;
    }
}
