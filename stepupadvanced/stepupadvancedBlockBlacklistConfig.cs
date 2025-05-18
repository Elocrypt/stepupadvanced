using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

public class BlockBlacklistConfig
{
    public List<string> BlockCodes { get; set; } = new List<string>();
    //public bool UseWhitelistMode { get; set; } = false;

    public static BlockBlacklistConfig Current { get; private set; } = new();

    private const string FileName = "StepUpAdvanced_BlockBlacklist.json";

    public static void Load(ICoreClientAPI api)
    {
        try
        {
            var loaded = api.LoadModConfig<BlockBlacklistConfig>(FileName);
            Current = loaded ?? new BlockBlacklistConfig();
        }
        catch (Exception e)
        {
            api.Logger.Error($"[StepUp Advanced] Failed to load BlockBlacklistConfig: {e.Message}");
            Current = new BlockBlacklistConfig();
        }
    }

    public static void Save(ICoreClientAPI api)
    {
        api.StoreModConfig(Current, FileName);
        api.World.Logger.Event("[StepUp Advanced] Block blacklist saved.");
    }
}
