using System.Collections.Generic;
using Vintagestory.API.Common;
using ProtoBuf;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;

namespace stepupadvanced;
[ProtoBuf.ProtoContract]
public class StepUpAdvancedConfig
{
    [ProtoBuf.ProtoMember(1)]
    public bool StepUpEnabled { get; set; } = true;
    [ProtoBuf.ProtoMember(2)]
    public bool ServerEnforceSettings { get; set; } = true;
    [ProtoBuf.ProtoMember(3)]
    public bool AllowClientChangeStepHeight { get; set; } = true;
    [ProtoBuf.ProtoMember(4)]
    public bool AllowClientChangeStepSpeed { get; set; } = true;
    [ProtoBuf.ProtoMember(5)]
	public bool AllowClientConfigReload { get; set; } = false;
    [ProtoBuf.ProtoMember(6)]
    public float StepHeight { get; set; } = 1.2f;
    [ProtoBuf.ProtoMember(7)]
    public float StepSpeed { get; set; } = 1.3f;
    [ProtoBuf.ProtoMember(8)]
    public float DefaultHeight { get; set; } = 0.6f;
    [ProtoBuf.ProtoMember(9)]
    public float DefaultSpeed { get; set; } = 0.7f;
    [ProtoBuf.ProtoMember(10)]
    public float StepHeightIncrement { get; set; } = 0.1f;
    [ProtoBuf.ProtoMember(11)]
    public float StepSpeedIncrement { get; set; } = 0.1f;
    [ProtoBuf.ProtoMember(12)]
    public List<string> BlockBlacklist { get; set; } = new List<string>();
    [ProtoBuf.ProtoMember(13)]
    public float ServerMinStepHeight { get; set; } = 0.6f;

    [ProtoBuf.ProtoMember(14)]
    public float ServerMaxStepHeight { get; set; } = 1.2f;

    [ProtoBuf.ProtoMember(15)]
    public float ServerMinStepSpeed { get; set; } = 0.7f;

    [ProtoBuf.ProtoMember(16)]
    public float ServerMaxStepSpeed { get; set; } = 1.3f;

    /*[ProtoBuf.ProtoMember(17)]
    public bool UseBlockWhitelist { get; set; } = false;*/

    public static StepUpAdvancedConfig Current { get; private set; }

	public static void Load(ICoreAPI api)
	{
		api.GetOrCreateDataPath("ModConfig");
		string configFile = "StepUpAdvancedConfig.json";
		StepUpAdvancedConfig loadedConfig = api.LoadModConfig<StepUpAdvancedConfig>(configFile);
		if (loadedConfig != null)
		{
			Current = loadedConfig;
			if (Current.StepHeight > 2f)
			{
				api.World.Logger.Warning($"StepHeight in config exceeds maximum allowed value ({2f}). Adjusting to maximum.");
				Current.StepHeight = 2f;
			}
			if (Current.StepHeightIncrement < 0.1f)
			{
				api.World.Logger.Warning($"StepHeightIncrement in config is below minimum allowed value ({0.1f}). Adjusting to minimum.");
				Current.StepHeightIncrement = 0.1f;
			}
			if (Current.DefaultHeight < 0.2f || Current.DefaultHeight > 2f)
			{
				api.World.Logger.Warning($"DefaultHeight in config is out of bounds (allowed range: {0.2f} - {2f}). Resetting to default value ({0.2f}).");
				Current.DefaultHeight = 0.2f;
			}
			api.World.Logger.Event($"Config Loaded: StepHeight = {Current.StepHeight}");
			if (Current.StepSpeed > 2f)
			{
				api.World.Logger.Warning($"StepSpeed in config exceeds maximum allowed value ({2f}). Adjusting to maximum.");
				Current.StepSpeed = 2f;
			}
			if (Current.StepSpeedIncrement < 0.1f)
			{
				api.World.Logger.Warning($"StepSpeedIncrement in config is below minimum allowed value ({0.1f}). Adjusting to minimum.");
				Current.StepSpeedIncrement = 0.1f;
			}
			if (Current.DefaultSpeed < 0.5f || Current.DefaultSpeed > 2f)
			{
				api.World.Logger.Warning($"DefaultSpeed in config is out of bounds (allowed range: {0.5f} - {2f}). Resetting to default value ({0.7f}).");
				Current.DefaultSpeed = 0.7f;
			}
			api.World.Logger.Event($"Config Loaded: StepSpeed = {Current.StepSpeed}");
            Current.ServerMinStepHeight = GameMath.Clamp(Current.ServerMinStepHeight, 0.2f, 2f);
            Current.ServerMaxStepHeight = GameMath.Clamp(Current.ServerMaxStepHeight, 0.2f, 2f);
            Current.ServerMinStepSpeed = GameMath.Clamp(Current.ServerMinStepSpeed, 0.5f, 2f);
            Current.ServerMaxStepSpeed = GameMath.Clamp(Current.ServerMaxStepSpeed, 0.5f, 2f);
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
        }
        else
		{
			Current = new StepUpAdvancedConfig();
			Save(api);
			api.World.Logger.Event("Created default 'StepUp Advanced' configuration file.");
		}
        if (loadedConfig == null)
        {
            Save(api);
        }
    }

    public static void Save(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi && clientApi.IsSinglePlayer)
        {
            Current.ServerEnforceSettings = false;
        }

        string configFile = "StepUpAdvancedConfig.json";
        api.StoreModConfig(Current, configFile);
        api.World.Logger.Event("Saved 'StepUp Advanced' configuration file.");
    }

    public static void UpdateConfig(StepUpAdvancedConfig newConfig)
	{
		Current = newConfig;
	}
}
