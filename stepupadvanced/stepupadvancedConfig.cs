using Vintagestory.API.Common;

namespace stepupadvanced;

public class StepUpAdvancedConfig
{
    public bool StepUpEnabled { get; set; } = true;
    public float StepHeight { get; set; } = 1.2f;
    public float StepUpSpeed { get; set; } = 10.0f;
    public float DefaultHeight { get; set; } = 0.6f;
    public float DefaultSpeed { get; set; } = 0.07f;
    public float StepHeightIncrement { get; set; } = 0.1f;
    public float StepUpSpeedIncrement { get; set; } = 0.01f;
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
            if (Current.StepUpSpeed > StepUpAdvancedModSystem.AbsoluteMaxStepUpSpeed)
            {
                api.World.Logger.Warning($"StepUpSpeed in config exceeds maximum allowed value ({StepUpAdvancedModSystem.AbsoluteMaxStepUpSpeed}). Adjusting to maximum.");
                Current.StepUpSpeed = StepUpAdvancedModSystem.AbsoluteMaxStepUpSpeed;
            }
            if (Current.StepUpSpeedIncrement < StepUpAdvancedModSystem.MinStepUpSpeedIncrement)
            {
                api.World.Logger.Warning($"StepUpSpeedIncrement in config is below minimum allowed value ({StepUpAdvancedModSystem.MinStepUpSpeedIncrement}). Adjusting to minimum.");
                Current.StepUpSpeedIncrement = StepUpAdvancedModSystem.MinStepUpSpeedIncrement;
            }
            if (Current.DefaultSpeed < StepUpAdvancedModSystem.MinStepUpSpeed || Current.DefaultSpeed > StepUpAdvancedModSystem.AbsoluteMaxStepUpSpeed)
            {
                api.World.Logger.Warning($"DefaultSpeed in config is out of bounds (allowed range: {StepUpAdvancedModSystem.MinStepUpSpeed} - {StepUpAdvancedModSystem.AbsoluteMaxStepUpSpeed}). Resetting to default value ({StepUpAdvancedModSystem.DefaultStepSpeed}).");
                Current.DefaultSpeed = StepUpAdvancedModSystem.DefaultStepSpeed;
            }
            api.World.Logger.Event($"Config Loaded: StepUpSpeed = {Current.StepUpSpeed}");
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