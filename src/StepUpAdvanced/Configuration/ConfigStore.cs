using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using StepUpAdvanced.Configuration.Migrations;
using StepUpAdvanced.Core;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Loads and saves the two config files and keeps
/// <see cref="StepUpOptions.Current"/> (the merged runtime view) in sync with them.
/// </summary>
/// <remarks>
/// <para>
/// Each side owns one file and never touches the other:
/// the server side (dedicated, or integrated in single-player) reads and
/// writes <see cref="ServerFileName"/>; the client side reads and writes
/// <see cref="ClientFileName"/>. Loading writes only that side's fields onto
/// <see cref="StepUpOptions.Current"/>, in place. In single-player both sides
/// share that one object, so neither side's load or save can overwrite the
/// other's settings, and a remote server's enforcement can never end up in
/// the player's own files.
/// </para>
/// <para>
/// Before 1.6.0 everything lived in <see cref="LegacyFileName"/>. When a
/// side's new file is missing, it is seeded from the legacy file (after the
/// legacy migrations run) or from defaults. The legacy file is left untouched:
/// each side migrates on its own schedule (a dedicated server never runs the
/// client side and vice versa), so neither can know when it's safe to remove.
/// </para>
/// </remarks>
internal static class ConfigStore
{
    /// <summary>
    /// Latest schema of the pre-1.6.0 single file. Still used to migrate a
    /// legacy file before it seeds the new ones; never written again.
    /// </summary>
    public const int LatestSchema = 4;

    /// <summary>Schema of <see cref="ServerFileName"/>.</summary>
    public const int ServerSchema = 1;

    /// <summary>Schema of <see cref="ClientFileName"/>.</summary>
    public const int ClientSchema = 1;

    /// <summary>Server-owned settings. Must keep this name once released.</summary>
    public const string ServerFileName = "StepUpAdvanced_Server.json";

    /// <summary>Player-owned settings. Must keep this name once released.</summary>
    public const string ClientFileName = "StepUpAdvanced_Client.json";

    /// <summary>The pre-1.6.0 shared file. Read once per side to seed the new files; never written.</summary>
    public const string LegacyFileName = "StepUpAdvancedConfig.json";

    // One lock per file. In single-player the client and server threads save
    // different files, so they never need to wait on each other.
    private static readonly object serverSaveLock = new();
    private static readonly object clientSaveLock = new();

    /// <summary>
    /// Loads this side's file (seeding it on first run), validates it, writes
    /// it back if anything was corrected, and applies it onto
    /// <see cref="StepUpOptions.Current"/>. Idempotent.
    /// </summary>
    public static void LoadOrUpgrade(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server) LoadServer(api);
        else LoadClient(api);
    }

    /// <summary>Writes this side's fields of <see cref="StepUpOptions.Current"/> to this side's file.</summary>
    public static void Save(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server)
        {
            Store(api, ConfigSplit.ToServer(StepUpOptions.Current), ServerFileName, serverSaveLock);
        }
        else
        {
            Store(api, ConfigSplit.ToClient(StepUpOptions.Current), ClientFileName, clientSaveLock);
        }
    }

    private static void LoadServer(ICoreAPI api)
    {
        api.GetOrCreateDataPath("ModConfig");

        if (!TryLoad(api, ServerFileName, out StepUpServerOptions? loaded))
        {
            // Unreadable file: run on defaults but don't overwrite it, so the
            // admin can fix the typo instead of losing their settings.
            ConfigSplit.ApplyServer(StepUpOptions.Current, new StepUpServerOptions());
            return;
        }

        bool dirty = false;
        StepUpServerOptions server;
        if (loaded == null)
        {
            StepUpOptions? legacy = LoadLegacy(api);
            server = legacy != null ? ConfigSplit.ToServer(legacy) : new StepUpServerOptions();
            dirty = true;
            ModLog.Event(api, legacy != null
                ? $"Created {ServerFileName} from your existing {LegacyFileName}. The old file is no longer used and can be deleted."
                : $"Created {ServerFileName} with default settings.");
        }
        else
        {
            server = loaded;
        }

        var messages = new List<string>();
        dirty |= ConfigNormalizer.NormalizeServer(server, messages);
        foreach (string m in messages) ModLog.Warning(api, m);

        if (server.SchemaVersion != ServerSchema) { server.SchemaVersion = ServerSchema; dirty = true; }

        ConfigSplit.ApplyServer(StepUpOptions.Current, server);
        if (dirty) Store(api, server, ServerFileName, serverSaveLock);
    }

    private static void LoadClient(ICoreAPI api)
    {
        api.GetOrCreateDataPath("ModConfig");

        if (!TryLoad(api, ClientFileName, out StepUpClientOptions? loaded))
        {
            ConfigSplit.ApplyClient(StepUpOptions.Current, new StepUpClientOptions());
            return;
        }

        bool dirty = false;
        StepUpClientOptions client;
        if (loaded == null)
        {
            StepUpOptions? legacy = LoadLegacy(api);
            client = legacy != null ? ConfigSplit.ToClient(legacy) : new StepUpClientOptions();
            dirty = true;
            ModLog.Event(api, legacy != null
                ? $"Created {ClientFileName} from your existing {LegacyFileName}. The old file is no longer used and can be deleted."
                : $"Created {ClientFileName} with default settings.");
        }
        else
        {
            client = loaded;
        }

        var messages = new List<string>();
        dirty |= ConfigNormalizer.NormalizeClient(client, messages);
        foreach (string m in messages) ModLog.Warning(api, m);

        if (client.SchemaVersion != ClientSchema) { client.SchemaVersion = ClientSchema; dirty = true; }

        ConfigSplit.ApplyClient(StepUpOptions.Current, client);
        if (dirty) Store(api, client, ClientFileName, clientSaveLock);

        var o = StepUpOptions.Current;
        ModLog.Verbose(api,
            $"Config loaded: StepHeight={o.StepHeight}, StepSpeed={o.StepSpeed}, " +
            $"SpeedOnly={(o.SpeedOnlyMode ? "on" : "off")}, " +
            $"HarmonyTweaks={(o.EnableHarmonyTweaks ? "on" : "off")}, " +
            $"CeilingGuard={(o.CeilingGuardEnabled ? "on" : "off")}, " +
            $"ForwardProbe={(o.ForwardProbeCeiling ? "on" : "off")} (dist={o.ForwardProbeDistance})");
    }

    /// <summary>
    /// Reads the pre-1.6.0 file, if present, and brings it to the last legacy
    /// schema so it seeds the new files with corrected values. Returns
    /// <c>null</c> when there is no legacy file or it can't be read.
    /// </summary>
    private static StepUpOptions? LoadLegacy(ICoreAPI api)
    {
        if (!TryLoad(api, LegacyFileName, out StepUpOptions? legacy) || legacy == null) return null;

        legacy.BlockBlacklist ??= new List<string>();
        MigrationRunner.Run(legacy, Math.Max(0, legacy.SchemaVersion));
        return legacy;
    }

    /// <summary>
    /// Loads <paramref name="fileName"/> from ModConfig. Returns <c>false</c>
    /// only when the file exists but can't be parsed; a missing file returns
    /// <c>true</c> with <paramref name="value"/> set to <c>null</c>.
    /// </summary>
    private static bool TryLoad<T>(ICoreAPI api, string fileName, out T? value) where T : class
    {
        try
        {
            value = api.LoadModConfig<T>(fileName);
            return true;
        }
        catch (Exception ex)
        {
            ModLog.Error(api, "Could not read {0}; using defaults until it's fixed. It will not be overwritten on load. {1}", fileName, ex.Message);
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Writes <paramref name="data"/> with retry and backoff. Sharing
    /// violations happen when an editor or antivirus briefly holds the file.
    /// </summary>
    private static void Store<T>(ICoreAPI api, T data, string fileName, object fileLock)
    {
        lock (fileLock)
        {
            const int maxAttempts = 8;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    api.StoreModConfig(data, fileName);
                    ModLog.Verbose(api, "Saved {0}.", fileName);
                    return;
                }
                catch (IOException ioex)
                {
                    int delayMs = 15 * (1 << Math.Min(attempt, 6)) + Random.Shared.Next(0, 10);
                    ModLog.Warning(api, "Save {0}/{1} of {2} failed: {3}. Retrying in {4} ms.",
                        attempt, maxAttempts, fileName, ioex.Message, delayMs);
                    Thread.Sleep(delayMs);
                }
            }
            ModLog.Error(api, "Could not save {0} after multiple attempts.", fileName);
        }
    }
}
