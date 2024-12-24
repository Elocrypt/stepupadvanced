using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace stepupadvanced;

public class StepUpAdvancedConfig
{
    public bool StepUpEnabled { get; set; } = true;
    public float StepHeight { get; set; } = 1.2f;
    public float DefaultHeight { get; set; } = 0.6f;
    public float StepHeightIncrement { get; set; } = 0.1f;

    public static StepUpAdvancedConfig Current { get; private set; }

    public static void Load(ICoreAPI api)
    {
        string configPath = api.GetOrCreateDataPath("ModConfig");
        string configFile = "StepUpAdvancedConfig.json";
        var loadedConfig = api.LoadModConfig<StepUpAdvancedConfig>(configFile);
        if (loadedConfig != null)
        {
            Current = loadedConfig;
            if (Current.StepHeight > StepUpAdvancedModSystem.AbsoluteMaxStepHeight)
            {
                api.World.Logger.Warning($"StepHeight in config exceeds maximum allowed value ({StepUpAdvancedModSystem.AbsoluteMaxStepHeight}). Adjusting to maximum.");
                Current.StepHeight = StepUpAdvancedModSystem.AbsoluteMaxStepHeight;
            }
            if (Current.StepHeightIncrement < StepUpAdvancedModSystem.MinStepHeightIncrement)
            {
                api.World.Logger.Warning($"StepHeightIncrement in config is below minimum allowed value ({StepUpAdvancedModSystem.MinStepHeightIncrement}). Adjusting to minimum.");
                Current.StepHeightIncrement = StepUpAdvancedModSystem.MinStepHeightIncrement;
            }
            if (Current.DefaultHeight < StepUpAdvancedModSystem.MinStepHeight || Current.DefaultHeight > StepUpAdvancedModSystem.AbsoluteMaxStepHeight)
            {
                api.World.Logger.Warning($"DefaultHeight in config is out of bounds (allowed range: {StepUpAdvancedModSystem.MinStepHeight} - {StepUpAdvancedModSystem.AbsoluteMaxStepHeight}). Resetting to default value ({StepUpAdvancedModSystem.DefaultStepHeight}).");
                Current.DefaultHeight = StepUpAdvancedModSystem.DefaultStepHeight;
            }
            api.World.Logger.Event($"Config Loaded: StepHeight = {Current.StepHeight}");
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
        string configPath = api.GetOrCreateDataPath("ModConfig");
        string configFile = "StepUpAdvancedConfig.json";
        api.StoreModConfig(Current, configFile);
        api.World.Logger.Event("Saved 'StepUp Advanced' configuration file.");
    }
}