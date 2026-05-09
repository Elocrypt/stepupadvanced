using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using StepUpAdvanced.Core;
using Vintagestory.API.Client;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Static facade for loading and saving <see cref="BlockBlacklistOptions"/>.
/// Client-side only — the server's blacklist lives in
/// <see cref="StepUpOptions.BlockBlacklist"/> and is handled by
/// <see cref="ConfigStore"/>.
/// </summary>
internal static class BlockBlacklistStore
{
    /// <summary>
    /// On-disk JSON file name. Must remain
    /// <c>StepUpAdvanced_BlockBlacklist.json</c> — changing it would orphan
    /// every existing user's saved blacklist.
    /// </summary>
    private const string FileName = "StepUpAdvanced_BlockBlacklist.json";

    /// <summary>
    /// Loads the blacklist from disk, normalizes (dedups + sorts case-insensitive),
    /// and writes back if anything changed. Idempotent.
    /// </summary>
    public static void Load(ICoreClientAPI api)
    {
        try
        {
            var loaded = api.LoadModConfig<BlockBlacklistOptions>(FileName);
            if (loaded == null)
            {
                BlockBlacklistOptions.Current = new BlockBlacklistOptions();
                Save(api);
                ModLog.Event(api, "Created new BlockBlacklist config (v1).");
                return;
            }

            bool changed = false;
            loaded.BlockCodes ??= new List<string>();

            // Dedup case-insensitively, then sort case-insensitively. If the
            // result differs from the loaded list (different size after dedup),
            // mark dirty so we save the normalized form back.
            var uniq = new HashSet<string>(loaded.BlockCodes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var normalized = uniq.ToList();
            normalized.Sort(StringComparer.OrdinalIgnoreCase);
            if (loaded.BlockCodes.Count != normalized.Count) changed = true;
            loaded.BlockCodes = normalized;

            if (loaded.SchemaVersion < 1) { loaded.SchemaVersion = 1; changed = true; }

            BlockBlacklistOptions.Current = loaded;
            if (changed) Save(api);
        }
        catch (Exception e)
        {
            ModLog.Error(api, $"Failed to load BlockBlacklist config: {e.Message}");
            BlockBlacklistOptions.Current = new BlockBlacklistOptions();
        }
    }

    /// <summary>
    /// Persists the blacklist to disk. Retries up to 5 times on
    /// <see cref="IOException"/> with linear backoff (30 ms × attempt).
    /// </summary>
    public static void Save(ICoreClientAPI api)
    {
        const int maxAttempts = 5;
        for (int i = 1; i <= maxAttempts; i++)
        {
            try
            {
                api.StoreModConfig(BlockBlacklistOptions.Current, FileName);
                ModLog.Verbose(api, "Block blacklist saved.");
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(30 * i);
            }
        }
        ModLog.Warning(api, "Failed to save BlockBlacklist config after several attempts.");
    }
}
