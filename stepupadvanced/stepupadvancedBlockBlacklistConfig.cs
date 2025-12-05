using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Vintagestory.API.Client;

namespace stepupadvanced
{
    public class BlockBlacklistConfig
    {
        public int SchemaVersion { get; set; } = 1;
        public List<string> BlockCodes { get; set; } = new List<string>();

        public static BlockBlacklistConfig Current { get; private set; } = new();

        private const string FileName = "StepUpAdvanced_BlockBlacklist.json";

        public static void Load(ICoreClientAPI api)
        {
            try
            {
                var loaded = api.LoadModConfig<BlockBlacklistConfig>(FileName);
                if (loaded == null)
                {
                    Current = new BlockBlacklistConfig();
                    Save(api);
                    api.World.Logger.Event("[StepUp Advanced] Created new BlockBlacklist config (v1).");
                    return;
                }

                bool changed = false;
                loaded.BlockCodes ??= new List<string>();

                var uniq = new HashSet<string>(loaded.BlockCodes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                var normalized = uniq.ToList();
                normalized.Sort(StringComparer.OrdinalIgnoreCase);
                if (loaded.BlockCodes.Count != normalized.Count) changed = true;
                loaded.BlockCodes = normalized;

                if (loaded.SchemaVersion < 1) { loaded.SchemaVersion = 1; changed = true; }

                Current = loaded;
                if (changed) Save(api);
            }
            catch (Exception e)
            {
                api.Logger.Error($"[StepUp Advanced] Failed to load BlockBlacklistConfig: {e.Message}");
                Current = new BlockBlacklistConfig();
            }
        }

        public static void Save(ICoreClientAPI api)
        {
            const int maxAttempts = 5;
            for (int i = 1; i <= maxAttempts; i++)
            {
                try
                {
                    api.StoreModConfig(Current, FileName);
                    api.World.Logger.VerboseDebug("[StepUp Advanced] Block blacklist saved.");
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(30 * i);
                }
            }
            api.Logger.Warning("[StepUp Advanced] Failed to save BlockBlacklistConfig after several attempts.");
        }
    }
}