using System.Collections.Generic;
using Vintagestory.API.Common;
using ProtoBuf;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;

namespace stepupadvanced;

[ProtoContract]
public class StepUpAdvancedConfig
{
    [ProtoMember(1)] public bool StepUpEnabled { get; set; } = true;
    [ProtoMember(2)] public bool ServerEnforceSettings { get; set; } = true;
    [ProtoMember(3)] public bool AllowClientChangeStepHeight { get; set; } = true;
    [ProtoMember(4)] public bool AllowClientChangeStepSpeed { get; set; } = true;
    [ProtoMember(5)] public bool AllowClientConfigReload { get; set; } = false;

    [ProtoMember(6)] public float StepHeight { get; set; } = 1.2f;
    [ProtoMember(7)] public float StepSpeed { get; set; } = 1.3f;

    [ProtoMember(8)] public float DefaultHeight { get; set; } = 0.6f;
    [ProtoMember(9)] public float DefaultSpeed { get; set; } = 0.7f;

    [ProtoMember(10)] public float StepHeightIncrement { get; set; } = 0.1f;
    [ProtoMember(11)] public float StepSpeedIncrement { get; set; } = 0.1f;

    [ProtoMember(12)] public List<string> BlockBlacklist { get; set; } = new List<string>();

    [ProtoMember(13)] public float ServerMinStepHeight { get; set; } = 0.6f;
    [ProtoMember(14)] public float ServerMaxStepHeight { get; set; } = 1.2f;
    [ProtoMember(15)] public float ServerMinStepSpeed { get; set; } = 0.7f;
    [ProtoMember(16)] public float ServerMaxStepSpeed { get; set; } = 1.3f;

    [ProtoMember(17)] public bool EnableHarmonyTweaks { get; set; } = false;
    [ProtoMember(18)] public bool CeilingGuardEnabled { get; set; } = true;
    [ProtoMember(19)] public bool ForwardProbeCeiling { get; set; } = true;
    [ProtoMember(20)] public bool RequireForwardSupport { get; set; } = true;
    [ProtoMember(21)] public int ForwardProbeDistance { get; set; } = 1;
    [ProtoMember(22)] public bool SpeedOnlyMode { get; set; } = false;

    public static StepUpAdvancedConfig Current { get; private set; }

    private const float ClientMinStepHeight = 0.6f;
    private const float ClientMinStepSpeed = 0.7f;

    private const float ServerCapMinHeight = 0.6f;
    private const float ServerCapMaxHeight = 20f;
    private const float ServerCapMinSpeed = 0.7f;
    private const float ServerCapMaxSpeed = 20f;

    public static void Load(ICoreAPI api)
	{
		api.GetOrCreateDataPath("ModConfig");
		string configFile = "StepUpAdvancedConfig.json";

		StepUpAdvancedConfig loadedConfig = api.LoadModConfig<StepUpAdvancedConfig>(configFile);
        bool created = false;

        if (loadedConfig == null)
        {
            Current = new StepUpAdvancedConfig();
            created = true;
            Save(api);
            api.World.Logger.Event("Created default 'StepUp Advanced' configuration file.");
        }
        else
        {
            Current = loadedConfig;
        }

        bool dirty = created;

        dirty |= NormalizeCaps(api);


        // --- Increments sanity ---
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

        // --- Defaults vs caps ---
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

        // ----- live values vs caps -----
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
        api.World.Logger.Event($"Config Loaded: StepHeight = {Current.StepHeight}");

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
        api.World.Logger.Event($"Config Loaded: StepSpeed = {Current.StepSpeed}");

        // --- Singleplayer: never enforce server settings on yourself ---
        if (api is ICoreClientAPI cApi && cApi.IsSinglePlayer && Current.ServerEnforceSettings)
        {
            api.World.Logger.Event("[StepUp Advanced] Singleplayer detected. Disabling ServerEnforceSettings.");
            Current.ServerEnforceSettings = false; dirty = true;
        }

        // --- Forward probe normalization ---
        int fwdOld = Current.ForwardProbeDistance;
        Current.ForwardProbeDistance = GameMath.Clamp(Current.ForwardProbeDistance, 0, 4);
        if (Current.ForwardProbeDistance != fwdOld)
        {
            api.World.Logger.Event($"[StepUp Advanced] Normalized ForwardProbeDistance to {Current.ForwardProbeDistance}");
            dirty = true;
        }

        // --- Feature flags echo ---
        api.World.Logger.Event(
            $"[StepUp Advanced] Flags: " +
            $"SpeedOnly={(Current.SpeedOnlyMode ? "on" : "off")}, " +
            $"HarmonyTweaks={(Current.EnableHarmonyTweaks ? "on" : "off")}, " +
            $"CeilingGuard={(Current.CeilingGuardEnabled ? "on" : "off")}, " +
            $"ForwardProbe={(Current.ForwardProbeCeiling ? "on" : "off")} (dist={Current.ForwardProbeDistance})"
        );

        if (dirty) Save(api);
    }

    public static void Save(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi && clientApi.IsSinglePlayer && Current.ServerEnforceSettings)
        {
            api.World.Logger.Event("[StepUp Advanced] Singleplayer detected during save. Disabling ServerEnforceSettings.");
            Current.ServerEnforceSettings = false;
        }

        const string configFile = "StepUpAdvancedConfig.json";
        api.StoreModConfig(Current, configFile);
        api.World.Logger.Event("Saved 'StepUp Advanced' configuration file.");
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
            api.World.Logger.Event($"[StepUp Advanced] Normalized height caps: Min={Current.ServerMinStepHeight}, Max={Current.ServerMaxStepHeight}");
            dirty = true;
        }
        if (oldMinSpeed != Current.ServerMinStepSpeed || oldMaxSpeed != Current.ServerMaxStepSpeed)
        {
            api.World.Logger.Event($"[StepUp Advanced] Normalized speed caps: Min={Current.ServerMinStepSpeed}, Max={Current.ServerMaxStepSpeed}");
            dirty = true;
        }

        return dirty;
    }
}