using Vintagestory.API.Common;

namespace stepupadvanced;

public class stepupadvancedConfig
{
    public bool StepUpEnabled { get; set; } = true;
    public float StepHeight { get; set; } = 1.2f;
    public float DefaultHeight { get; set; } = 0.6f;
    public float StepHeightIncrement { get; set; } = 0.1f;

    public static stepupadvancedConfig Current { get; private set; }

    public static void Load(ICoreAPI api)
    {
        string configPath = api.GetOrCreateDataPath("ModConfig");
        string fullFilePath = $"{configPath}/stepupadvancedConfig.json";
        var loadedConfig = api.LoadModConfig<stepupadvancedConfig>("stepupadvancedConfig.json");
        if (loadedConfig != null )
        {
            Current = loadedConfig;
            api.World.Logger.Event($"Config Loaded: StepHeight = {Current.StepHeight}");
        }
        else
        {
            Current = new stepupadvancedConfig();
            Save(api);
            api.World.Logger.Event("Created default 'StepUp Advanced' configuration file.");
        }
    }

    public static void Save(ICoreAPI api)
    {
        string configPath = api.GetOrCreateDataPath("ModConfig");
        string configFile = "stepupadvancedConfig.json";
        string fullFilePath = $"{configPath}/{configFile}";
        api.StoreModConfig(Current, configFile);
        api.World.Logger.Event("Saved 'StepUp Advanced' configuration file.");
    }
}
public class stepupadvancedServerConfig
{
    public bool AllowStepUpAdvanced { get; set; } = true;
    public float MaxStepHeight { get; set; } = 1.0f;

    public static stepupadvancedServerConfig Current { get; private set; }

    public static void Load(ICoreAPI api)
    {
        string configPath = api.GetOrCreateDataPath("ServerConfig");
        string configFile = "stepupadvancedServerConfig.json";
        var loadedConfig = api.LoadModConfig<stepupadvancedServerConfig>(configFile);
        if (loadedConfig != null)
        {
            Current = loadedConfig;
            api.World.Logger.Event($"Server Config Loaded: AllowStepUpAdvanced = {Current.AllowStepUpAdvanced}, MaxStepHeight = {Current.MaxStepHeight}");
        }
        else
        {
            Current = new stepupadvancedServerConfig();
            Save(api);
            api.World.Logger.Event("Created default 'StepUp Advanced' server configuration file.");
        }
    }

    public static void Save(ICoreAPI api)
    {
        string configFile = "stepupadvancedServerConfig.json";
        api.StoreModConfig(Current, configFile);
        api.World.Logger.Event("Saved 'StepUp Advanced' server configuration file.");
    }
}