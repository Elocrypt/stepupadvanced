using System.Collections.Generic;
using Vintagestory.API.Common;

namespace stepupadvanced;

public class StepUpAdvancedConfig
{
	public bool StepUpEnabled { get; set; } = true;

	public float StepHeight { get; set; } = 1.2f;

	public float StepSpeed { get; set; } = 1.3f;

	public float DefaultHeight { get; set; } = 0.2f;

	public float DefaultSpeed { get; set; } = 0.7f;

	public float StepHeightIncrement { get; set; } = 0.1f;

	public float StepSpeedIncrement { get; set; } = 0.1f;

	public List<string> BlockBlacklist { get; set; } = new List<string>();

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
		}
		else
		{
			Current = new StepUpAdvancedConfig();
			Save(api);
			api.World.Logger.Event("Created default 'StepUp Advanced' configuration file.");
		}
		Save(api);
	}

	public static void Save(ICoreAPI api)
	{
		api.GetOrCreateDataPath("ModConfig");
		string configFile = "StepUpAdvancedConfig.json";
		api.StoreModConfig(Current, configFile);
		api.World.Logger.Event("Saved 'StepUp Advanced' configuration file.");
	}
}
