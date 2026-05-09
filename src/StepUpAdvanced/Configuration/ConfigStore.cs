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

        bool enforceCaps = IsEnforcedForThisSide(api);

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

        if (api is ICoreClientAPI cApi && cApi.IsSinglePlayer && StepUpOptions.Current.ServerEnforceSettings)
        {
            ModLog.Verbose(api, "Singleplayer detected. Disabling ServerEnforceSettings.");
            StepUpOptions.Current.ServerEnforceSettings = false; dirty = true;
        }

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
    /// Skipped silently on non-dedicated server side (integrated server in
    /// single-player would otherwise race the client's own save).
    /// </remarks>
    public static void Save(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi && clientApi.IsSinglePlayer && StepUpOptions.Current.ServerEnforceSettings)
        {
            ModLog.Verbose(api, "Singleplayer detected during save. Disabling ServerEnforceSettings.");
            StepUpOptions.Current.ServerEnforceSettings = false;
        }

        if (api.Side == EnumAppSide.Server)
        {
            var sapi = api as ICoreServerAPI;
            if (sapi != null && !sapi.Server.IsDedicated) return;
        }

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

    /// <summary>
    /// Replaces the current options with a new instance. Used by the network
    /// channel handler when receiving server-pushed config.
    /// </summary>
    public static void UpdateConfig(StepUpOptions newConfig) => StepUpOptions.Current = newConfig;

    /// <summary>
    /// Internal predicate: should server caps be enforced for this side
    /// during load-time clamping? Distinct from the runtime
    /// <see cref="EnforcementState.IsEnforced"/> because it doesn't have
    /// access to <c>capi</c>/<c>sapi</c> — it works directly off the API
    /// argument passed to <see cref="LoadOrUpgrade"/>.
    /// </summary>
    private static bool IsEnforcedForThisSide(ICoreAPI api)
    {
        if (!StepUpOptions.Current.ServerEnforceSettings) return false;
        if (api is ICoreClientAPI c && c.IsSinglePlayer) return false;
        return true;
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
