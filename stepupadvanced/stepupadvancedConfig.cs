using Vintagestory.API.Common;

namespace stepupadvanced;

public class StepUpAdvancedConfig
{
    public bool StepUpEnabled { get; set; } = true;
    public float StepHeight { get; set; } = 1.2f;
    public float StepSpeed { get; set; } = 1.3f;
    public float DefaultHeight { get; set; } = 0.6f;
    public float DefaultSpeed { get; set; } = 0.7f;
    public float StepHeightIncrement { get; set; } = 0.1f;
    public float StepSpeedIncrement { get; set; } = 0.1f;
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
            if (Current.StepSpeed > StepUpAdvancedModSystem.AbsoluteMaxElevateFactor)
            {
                api.World.Logger.Warning($"StepSpeed in config exceeds maximum allowed value ({StepUpAdvancedModSystem.AbsoluteMaxElevateFactor}). Adjusting to maximum.");
                Current.StepSpeed = StepUpAdvancedModSystem.AbsoluteMaxElevateFactor;
            }
            if (Current.StepSpeedIncrement < StepUpAdvancedModSystem.MinElevateFactorIncrement)
            {
                api.World.Logger.Warning($"StepSpeedIncrement in config is below minimum allowed value ({StepUpAdvancedModSystem.MinElevateFactorIncrement}). Adjusting to minimum.");
                Current.StepSpeedIncrement = StepUpAdvancedModSystem.MinElevateFactorIncrement;
            }
            if (Current.DefaultSpeed < StepUpAdvancedModSystem.MinElevateFactor || Current.DefaultSpeed > StepUpAdvancedModSystem.AbsoluteMaxElevateFactor)
            {
                api.World.Logger.Warning($"DefaultSpeed in config is out of bounds (allowed range: {StepUpAdvancedModSystem.MinElevateFactor} - {StepUpAdvancedModSystem.AbsoluteMaxElevateFactor}). Resetting to default value ({StepUpAdvancedModSystem.DefaultElevateFactor}).");
                Current.DefaultSpeed = StepUpAdvancedModSystem.DefaultElevateFactor;
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
        string configPath = api.GetOrCreateDataPath("ModConfig");
        string configFile = "StepUpAdvancedConfig.json";
        api.StoreModConfig(Current, configFile);
        api.World.Logger.Event("Saved 'StepUp Advanced' configuration file.");
    }
}