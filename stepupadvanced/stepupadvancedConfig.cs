using ProtoBuf;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace stepupadvanced;

[ProtoContract]
public class StepUpAdvancedConfig
{
    [ProtoMember(1), DefaultValue(true)] public bool StepUpEnabled { get; set; } = true;
    [ProtoMember(2), DefaultValue(true)] public bool ServerEnforceSettings { get; set; } = true;
    [ProtoMember(3), DefaultValue(true)] public bool AllowClientChangeStepHeight { get; set; } = true;
    [ProtoMember(4), DefaultValue(true)] public bool AllowClientChangeStepSpeed { get; set; } = true;
    [ProtoMember(5), DefaultValue(false)] public bool AllowClientConfigReload { get; set; } = false;

    [ProtoMember(6), DefaultValue(1.2f)] public float StepHeight { get; set; } = 1.2f;
    [ProtoMember(7), DefaultValue(1.3f)] public float StepSpeed { get; set; } = 1.3f;

    [ProtoMember(8), DefaultValue(0.6f)] public float DefaultHeight { get; set; } = 0.6f;
    [ProtoMember(9), DefaultValue(0.7f)] public float DefaultSpeed { get; set; } = 0.7f;

    [ProtoMember(10), DefaultValue(0.1f)] public float StepHeightIncrement { get; set; } = 0.1f;
    [ProtoMember(11), DefaultValue(0.1f)] public float StepSpeedIncrement { get; set; } = 0.1f;

    [ProtoMember(12)] public List<string> BlockBlacklist { get; set; } = new List<string>();

    [ProtoMember(13), DefaultValue(0.6f)] public float ServerMinStepHeight { get; set; } = 0.6f;
    [ProtoMember(14), DefaultValue(1.2f)] public float ServerMaxStepHeight { get; set; } = 1.2f;
    [ProtoMember(15), DefaultValue(0.7f)] public float ServerMinStepSpeed { get; set; } = 0.7f;
    [ProtoMember(16), DefaultValue(1.3f)] public float ServerMaxStepSpeed { get; set; } = 1.3f;

    [ProtoMember(17), DefaultValue(true)] public bool EnableHarmonyTweaks { get; set; } = true;
    [ProtoMember(18), DefaultValue(false)] public bool SpeedOnlyMode { get; set; } = false;
    [ProtoMember(19), DefaultValue(true)] public bool CeilingGuardEnabled { get; set; } = true;
    [ProtoMember(20), DefaultValue(false)] public bool ForwardProbeCeiling { get; set; } = false;
    [ProtoMember(21), DefaultValue(false)] public bool RequireForwardSupport { get; set; } = false;
    [ProtoMember(22), DefaultValue(1)] public int ForwardProbeDistance { get; set; } = 1;
    [ProtoMember(23), DefaultValue(1)] public int ForwardProbeSpan { get; set; } = 1;
    [ProtoMember(24), DefaultValue(0.05f)] public float CeilingHeadroomPad { get; set; } = 0.05f;
    [ProtoMember(25), DefaultValue(true)] public bool ShowServerEnforcedNotice { get; set; } = true;
    [ProtoMember(26)] public int SchemaVersion { get; set; } = LatestSchema;
    [ProtoMember(27), DefaultValue(false)] public bool QuietMode { get; set; } = false;

    public const int LatestSchema = 4;

    private static readonly object _saveLock = new();
    private static readonly string FileName = "StepUpAdvancedConfig.json";
    private static readonly string GlobalMutexName = "Global\\VS-StepUpAdvanced-Config";

    public static StepUpAdvancedConfig Current { get; private set; } = new();

    private const float ClientMinStepHeight = 0.6f;
    private const float ClientMinStepSpeed = 0.7f;

    private const float ServerCapMinHeight = 0.6f;
    private const float ServerCapMaxHeight = 20f;
    private const float ServerCapMinSpeed = 0.7f;
    private const float ServerCapMaxSpeed = 20f;

    public static void LoadOrUpgrade(ICoreAPI api)
    {
        api.GetOrCreateDataPath("ModConfig");

        var loadedConfig = api.LoadModConfig<StepUpAdvancedConfig>(FileName);
        bool created = false;

        if (loadedConfig == null)
        {
            Current = new StepUpAdvancedConfig { SchemaVersion = LatestSchema };
            created = true;
            Save(api);
            api.World.Logger.Event("[StepUp Advanced] Created new config (schema v{0}).", LatestSchema);
            return;
        }
        else
        {
            Current = loadedConfig;
        }

        bool changed = MergeAndMigrate(api, loadedConfig, out var merged);
        Current = merged;

        if (Normalize(api, Current)) changed = true;

        if (changed)
        {
            Save(api);
            api.World.Logger.Event("[StepUp Advanced] Auto-upgraded config to schema v{0}.", Current.SchemaVersion);
        }

        bool dirty = created;

        dirty |= NormalizeCaps(api);

        if (Current.StepHeightIncrement < 0.1f)
        {
            api.World.Logger.Warning("StepHeightIncrement in config is below minimum allowed value (0.1). Adjusting to minimum.");
            Current.StepHeightIncrement = 0.1f; dirty = true;
        }
        if (Current.StepSpeedIncrement < 0.1f)
        {
            api.World.Logger.Warning("StepSpeedIncrement in config is below minimum allowed value (0.1). Adjusting to minimum.");
            Current.StepSpeedIncrement = 0.1f; dirty = true;
        }

        if (Current.DefaultHeight < Current.ServerMinStepHeight || Current.DefaultHeight > Current.ServerMaxStepHeight)
        {
            api.World.Logger.Warning(
                $"DefaultHeight in config is out of bounds (allowed range: {Current.ServerMinStepHeight} - {Current.ServerMaxStepHeight}). Resetting to min ({Current.ServerMinStepHeight}).");
            Current.DefaultHeight = Current.ServerMinStepHeight; dirty = true;
        }
        if (Current.DefaultSpeed < Current.ServerMinStepSpeed || Current.DefaultSpeed > Current.ServerMaxStepSpeed)
        {
            api.World.Logger.Warning(
                $"DefaultSpeed in config is out of bounds (allowed range: {Current.ServerMinStepSpeed} - {Current.ServerMaxStepSpeed}). Resetting to min ({Current.ServerMinStepSpeed}).");
            Current.DefaultSpeed = Current.ServerMinStepSpeed; dirty = true;
        }

        bool enforceCaps = IsEnforcedForThisSide(api);

        float stepHeight = Current.StepHeight;
        if (stepHeight < ClientMinStepHeight)
        {
            api.World.Logger.Warning($"StepHeight below minimum ({ClientMinStepHeight}). Adjusting to {ClientMinStepHeight}.");
            stepHeight = ClientMinStepHeight; dirty = true;
        }
        if (enforceCaps)
        {
            float clamped = GameMath.Clamp(stepHeight, Current.ServerMinStepHeight, Current.ServerMaxStepHeight);
            if (clamped != stepHeight)
            {
                api.World.Logger.Warning($"StepHeight outside enforced server range ({Current.ServerMinStepHeight}..{Current.ServerMaxStepHeight}). Adjusting.");
                stepHeight = clamped; dirty = true;
            }
        }
        if (stepHeight != Current.StepHeight) Current.StepHeight = stepHeight;
        api.World.Logger.VerboseDebug($"Config Loaded: StepHeight = {Current.StepHeight}");

        float stepSpeed = Current.StepSpeed;
        if (stepSpeed < ClientMinStepSpeed)
        {
            api.World.Logger.Warning($"StepSpeed below minimum ({ClientMinStepSpeed}). Adjusting to {ClientMinStepSpeed}.");
            stepSpeed = ClientMinStepSpeed; dirty = true;
        }
        if (enforceCaps)
        {
            float clamped = GameMath.Clamp(stepSpeed, Current.ServerMinStepSpeed, Current.ServerMaxStepSpeed);
            if (clamped != stepSpeed)
            {
                api.World.Logger.Warning($"StepSpeed outside enforced server range ({Current.ServerMinStepSpeed}..{Current.ServerMaxStepSpeed}). Adjusting.");
                stepSpeed = clamped; dirty = true;
            }
        }
        if (stepSpeed != Current.StepSpeed) Current.StepSpeed = stepSpeed;
        api.World.Logger.VerboseDebug($"Config Loaded: StepSpeed = {Current.StepSpeed}");

        if (api is ICoreClientAPI cApi && cApi.IsSinglePlayer && Current.ServerEnforceSettings)
        {
            api.World.Logger.VerboseDebug("[StepUp Advanced] Singleplayer detected. Disabling ServerEnforceSettings.");
            Current.ServerEnforceSettings = false; dirty = true;
        }

        int fwdOld = Current.ForwardProbeDistance;
        Current.ForwardProbeDistance = GameMath.Clamp(Current.ForwardProbeDistance, 0, 4);
        if (Current.ForwardProbeDistance != fwdOld)
        {
            api.World.Logger.VerboseDebug($"[StepUp Advanced] Normalized ForwardProbeDistance to {Current.ForwardProbeDistance}");
            dirty = true;
        }

        Current.ForwardProbeSpan = GameMath.Clamp(Current.ForwardProbeSpan, 0, 2);
        Current.CeilingHeadroomPad = GameMath.Clamp(Current.CeilingHeadroomPad, 0f, 0.4f);

        api.World.Logger.VerboseDebug(
            $"[StepUp Advanced] Flags: " +
            $"SpeedOnly={(Current.SpeedOnlyMode ? "on" : "off")}, " +
            $"HarmonyTweaks={(Current.EnableHarmonyTweaks ? "on" : "off")}, " +
            $"CeilingGuard={(Current.CeilingGuardEnabled ? "on" : "off")}, " +
            $"ForwardProbe={(Current.ForwardProbeCeiling ? "on" : "off")} (dist={Current.ForwardProbeDistance})"
        );

        if (dirty) Save(api);
    }

    private static bool MergeAndMigrate(ICoreAPI api, StepUpAdvancedConfig loaded, out StepUpAdvancedConfig merged)
    {
        bool changed = false;

        var def = new StepUpAdvancedConfig();

        foreach (var p in typeof(StepUpAdvancedConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
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

        changed |= Migrate(api, def, from);

        if (def.SchemaVersion != LatestSchema) { def.SchemaVersion = LatestSchema; changed = true; }

        merged = def;
        return changed;
    }

    private static bool Migrate(ICoreAPI api, StepUpAdvancedConfig cfg, int from)
    {
        bool changed = false;

        if (from < 2)
        {
            if (cfg.ForwardProbeSpan <= 0) { cfg.ForwardProbeSpan = 1; changed = true; }
            if (cfg.ForwardProbeDistance <= 0) { cfg.ForwardProbeDistance = 1; changed = true; }
        }

        cfg.BlockBlacklist ??= new List<string>();
        return changed;
    }

    private static bool Normalize(ICoreAPI api, StepUpAdvancedConfig cfg)
    {
        bool dirty = false;

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

    public static void Save(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi && clientApi.IsSinglePlayer && Current.ServerEnforceSettings)
        {
            api.World.Logger.VerboseDebug("[StepUp Advanced] Singleplayer detected during save. Disabling ServerEnforceSettings.");
            Current.ServerEnforceSettings = false;
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
                api.World.Logger.Warning("[StepUp Advanced] Could not acquire config save mutex; skipping save.");
                return;
            }
            const int maxAttempts = 8;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    api.StoreModConfig(Current, FileName);
                    api.World.Logger.VerboseDebug("[StepUp Advanced] Saved configuration.");
                    return;
                }
                catch (IOException ioex)
                {
                    int delayMs = 15 * (1 << Math.Min(attempt, 6)) + new Random().Next(0, 10);
                    api.World.Logger.Warning("[StepUp Advanced] Save {0}/{1} failed: {2}. Retrying in {3} ms.",
                        attempt, maxAttempts, ioex.Message, delayMs);
                    Thread.Sleep(delayMs);
                }
            }
            api.World.Logger.Error("[StepUp Advanced] Could not save configuration after multiple attempts.");
        }
    }

    public static void UpdateConfig(StepUpAdvancedConfig newConfig) => Current = newConfig;

    private static bool IsEnforcedForThisSide(ICoreAPI api)
    {
        if (!Current.ServerEnforceSettings) return false;
        if (api is ICoreClientAPI c && c.IsSinglePlayer) return false;
        return true;
    }

    private static bool NormalizeCaps(ICoreAPI api)
    {
        bool dirty = false;

        float oldMinHeight = Current.ServerMinStepHeight, oldMaxHeight = Current.ServerMaxStepHeight;
        float oldMinSpeed = Current.ServerMinStepSpeed, oldMaxSpeed = Current.ServerMaxStepSpeed;

        Current.ServerMinStepHeight = GameMath.Clamp(Current.ServerMinStepHeight, ServerCapMinHeight, ServerCapMaxHeight);
        Current.ServerMaxStepHeight = GameMath.Clamp(Current.ServerMaxStepHeight, ServerCapMinHeight, ServerCapMaxHeight);
        Current.ServerMinStepSpeed = GameMath.Clamp(Current.ServerMinStepSpeed, ServerCapMinSpeed, ServerCapMaxSpeed);
        Current.ServerMaxStepSpeed = GameMath.Clamp(Current.ServerMaxStepSpeed, ServerCapMinSpeed, ServerCapMaxSpeed);

        if (Current.ServerMinStepHeight > Current.ServerMaxStepHeight)
        {
            api.World.Logger.Warning("[StepUp Advanced] ServerMinStepHeight > ServerMaxStepHeight. Adjusting Min to Max.");
            Current.ServerMinStepHeight = Current.ServerMaxStepHeight;
        }
        if (Current.ServerMinStepSpeed > Current.ServerMaxStepSpeed)
        {
            api.World.Logger.Warning("[StepUp Advanced] ServerMinStepSpeed > ServerMaxStepSpeed. Adjusting Min to Max.");
            Current.ServerMinStepSpeed = Current.ServerMaxStepSpeed;
        }

        if (oldMinHeight != Current.ServerMinStepHeight || oldMaxHeight != Current.ServerMaxStepHeight)
        {
            api.World.Logger.VerboseDebug($"[StepUp Advanced] Normalized height caps: Min={Current.ServerMinStepHeight}, Max={Current.ServerMaxStepHeight}");
            dirty = true;
        }
        if (oldMinSpeed != Current.ServerMinStepSpeed || oldMaxSpeed != Current.ServerMaxStepSpeed)
        {
            api.World.Logger.VerboseDebug($"[StepUp Advanced] Normalized speed caps: Min={Current.ServerMinStepSpeed}, Max={Current.ServerMaxStepSpeed}");
            dirty = true;
        }

        return dirty;
    }
}