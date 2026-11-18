using HarmonyLib;
using System;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using StepUpAdvanced.Application;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Infrastructure.Input;
using StepUpAdvanced.Infrastructure.Network;
using StepUpAdvanced.Infrastructure.Probes;

namespace StepUpAdvanced;

public class StepUpAdvancedModSystem : ModSystem
{
    // ─── API references ─────────────────────────────────────────────────
    private ICoreClientAPI capi = null!;
    private ICoreServerAPI sapi = null!;

    // ─── Server-side state ──────────────────────────────────────────────
    private DebouncedConfigWatcher? configWatcher;
    private ConfigSyncChannel? configSyncChannel;

    // ServerEnforcementCoordinator owns the four /sua mutation command
    // bodies and the two reload paths (command + watcher-triggered).
    // Constructed in StartServerSide after configWatcher and
    // configSyncChannel are initialized.
    private ServerEnforcementCoordinator? serverCoordinator;

    // The watcher only reloads server-owned settings; players reload theirs with the hotkey.
    private string serverConfigPath => Path.Combine(sapi.GetOrCreateDataPath("ModConfig"), ConfigStore.ServerFileName);

    // ─── Client-side state ──────────────────────────────────────────────
    // Nine independent per-toast flags — see MessageDebouncer.
    private readonly MessageDebouncer toasts = new();

    private SuaCommands? commands;
    private HotkeyHandlers? hotkeys;

    private PhysicsFieldWriter? physicsWriter;

    // ElevateFactorController: speed axis. Push-driven (no tick listener);
    // pushed on user actions, toggle, server config receive, and config
    // reload.
    private ElevateFactorController? elevateController;

    // StepHeightController: height axis. Owns stepUpEnabled. Tick-driven
    // (50 ms listener); also push-driven on user actions, toggle, server
    // config receive, and config reload.
    private StepHeightController? stepController;

    // Per-tick probe state — scratch BlockPos, BlockPos[5] column buffer,
    // and the HashSet-cached blacklist (rebuilt on demand at mutation
    // sites). One instance per ModSystem; client-side only.
    private WorldProbe? worldProbe;

    // Dual-debounced save queue (200 ms scheduling + 500 ms hard floor).
    // Shared across both controllers so the debounce is axis-agnostic.
    private ConfigSaveQueue? configSaveQueue;

    // Watches StepUpAdvanced_Client.json so edits apply as soon as the file
    // is saved, as the Reload hotkey would. Separate from the server watcher:
    // each side watches only its own file.
    private DebouncedConfigWatcher? clientConfigWatcher;

    // Long enough to cover a save plus the watcher's own 150 ms debounce.
    private const int ClientWatcherSuppressMs = 300;

    // How long after level finalize to wait for the server's config packet
    // before telling the player this server doesn't run the mod. The server
    // sends it on PlayerNowPlaying, which waits for the client to finish
    // loading, so this leaves room for slow loads. A late packet still
    // unlocks normally; the delay only affects the notice.
    private const int BasicModeNoticeDelayMs = 10000;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        ConfigStore.LoadOrUpgrade(api);
        Harmony.DEBUG = false;
        if (api.Side == EnumAppSide.Client && StepUpOptions.Current.EnableHarmonyTweaks)
        {
            try
            {
                Harmony harmony = new Harmony("stepupadvanced.mod");
                PatchClassProcessor processor = new PatchClassProcessor(harmony, typeof(EntityBehaviorPlayerPhysicsPatch));
                processor.Patch();
                ModLog.Verbose(api, "Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                ModLog.Warning(api, "Harmony patches disabled (safe mode): {0}", ex.Message);
            }
        }
        ModLog.Event(api, "Initialized 'StepUp Advanced' mod");
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        sapi = api;
        ConfigStore.LoadOrUpgrade(api);

        configSyncChannel ??= new ConfigSyncChannel();
        configSyncChannel.RegisterServer(api);

        SetupConfigWatcher();

        serverCoordinator = new ServerEnforcementCoordinator(
            api, configWatcher!, configSyncChannel!,
            markBlacklistDirty: () => worldProbe?.Blacklist.MarkDirty());

        // File-watcher event subscription moves here (out of
        // SetupConfigWatcher) so it can target the coordinator, which
        // didn't exist when SetupConfigWatcher ran.
        configWatcher!.ConfigFileChanged += serverCoordinator.ReloadFromWatcher;

        commands = new SuaCommands();
        commands.RegisterServer(sapi, serverCoordinator);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        // Start locked; OnReceiveServerConfig unlocks once the server proves
        // it runs the mod. Set before anything can apply step height or speed.
        BasicModeState.Enter();

        configSyncChannel ??= new ConfigSyncChannel();
        configSyncChannel.RegisterClient(api, OnReceiveServerConfig);

        ConfigStore.LoadOrUpgrade(api);
        BlockBlacklistStore.Load(api);

        worldProbe = new WorldProbe();

        physicsWriter = new PhysicsFieldWriter(capi);

        configSaveQueue = new ConfigSaveQueue(capi, SuppressClientWatcher);

        elevateController = new ElevateFactorController(capi, toasts, configSaveQueue.RequestSave);
        stepController = new StepHeightController(capi, worldProbe, physicsWriter, toasts, configSaveQueue.RequestSave);

        bool heightNormalized = stepController.InitializeFromConfig();
        bool speedNormalized = elevateController.InitializeFromConfig();

        if (heightNormalized || speedNormalized)
        {
            ModLog.Verbose(capi, "Normalized runtime StepHeight/StepSpeed (client floors; server caps only if enforced).");
        }

        RegisterHotkeys();
        capi.Event.RegisterGameTickListener((dt) => { stepController.ApplyForTick(); }, 50);
        elevateController.ApplyNow(stepController.IsEnabled);
        RegisterClientCommands();
        SetupClientConfigWatcher();
        capi.Event.LevelFinalize += ScheduleBasicModeNotice;
    }

    /// <summary>
    /// Once the world is loaded, waits briefly for the server's config packet;
    /// if it never came, tells the player once that this server doesn't run
    /// the mod and only Basic mode is available.
    /// </summary>
    private void ScheduleBasicModeNotice()
    {
        capi.Event.RegisterCallback(_ =>
        {
            if (!BasicModeState.Active) return;
            ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("basic-mode"))} – {ChatFormatting.Muted(ChatFormatting.L("basic-mode-notice"))}");
        }, BasicModeNoticeDelayMs);
    }

    private void RegisterHotkeys()
    {
        hotkeys = new HotkeyHandlers(capi, stepController!, elevateController!, toasts, ReloadConfig);
        hotkeys.Register();
    }

    private void RegisterClientCommands()
    {
        commands = new SuaCommands();
        commands.RegisterClient(capi, worldProbe!);
    }

    /// <summary>Reloads config from disk and re-applies through both axes.</summary>
    private void ReloadConfig()
    {
        // Loading can write corrections back to the file; don't let that
        // write bounce back through the watcher as a second reload.
        SuppressClientWatcher();

        // Reloads only the player's own file. Server-owned fields aren't in
        // it, so whatever the server last pushed stays in force.
        ConfigStore.LoadOrUpgrade(capi);

        worldProbe?.Blacklist.MarkDirty();

        // Step must refresh first so IsEnabled is current when elevate queries it.
        stepController?.OnConfigReloaded();
        elevateController?.OnConfigReloaded(stepController?.IsEnabled ?? true);
        ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Ok(ChatFormatting.L("config-reloaded"))}");
    }

    private void OnReceiveServerConfig(ConfigSyncPacket packet)
    {
        bool showNotice = packet.ShowServerEnforcedNotice;

        capi.Event.EnqueueMainThreadTask(() =>
        {
            // Any config packet proves the server runs the mod (it sends one
            // on every join), so leave Basic mode before re-applying below.
            BasicModeState.Exit();

            // Merge only the enforcement fields; all client-local fields
            // (StepHeight, StepSpeed, QuietMode, probe tunables) are preserved.
            ConfigSyncPacketMapper.Apply(StepUpOptions.Current, packet);

            // Either a blacklist change or an enforcement flip invalidates the
            // cached union — mark dirty so the next probe rebuilds.
            worldProbe?.Blacklist.MarkDirty();

            bool isEnforced = StepUpOptions.Current?.ServerEnforceSettings == true;

            if (!isEnforced && toasts.ServerEnforcement.IsShown)
            {
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Ok(ChatFormatting.L("server-enforcement-off"))} " + $"{ChatFormatting.Muted(ChatFormatting.L("local-settings-enabled"))}");
                toasts.ServerEnforcement.Reset();
            }

            // TryShow marks the transition; only emit the toast when the server
            // has ShowServerEnforcedNotice set.
            if (isEnforced && toasts.ServerEnforcement.TryShow())
            {
                if (showNotice)
                {
                    ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("server-enforcement-on"))} " + $"{ChatFormatting.Muted(ChatFormatting.L("local-settings-disabled"))}");
                }
            }

            stepController?.ApplyForTick();
            elevateController?.ApplyNow(stepController?.IsEnabled ?? true);
        }, "ApplyStepUpServerConfig");
    }

    /// <summary>
    /// Initializes the config-file watcher. The
    /// <see cref="DebouncedConfigWatcher.ConfigFileChanged"/> subscription
    /// is wired in <see cref="StartServerSide"/> after the coordinator exists.
    /// </summary>
    private void SetupConfigWatcher()
    {
        configWatcher = new DebouncedConfigWatcher(
            filePath: serverConfigPath,
            dispatchToMainThread: action => sapi.Event.EnqueueMainThreadTask(action, "ReloadStepUpOptions"));

        configWatcher.Start(sapi);
    }


    /// <summary>
    /// Starts watching the player's own config file. Runs after the initial
    /// load, so the corrections it may have written don't trigger a reload.
    /// </summary>
    private void SetupClientConfigWatcher()
    {
        string path = Path.Combine(capi.GetOrCreateDataPath("ModConfig"), ConfigStore.ClientFileName);
        clientConfigWatcher = new DebouncedConfigWatcher(
            filePath: path,
            dispatchToMainThread: action => capi.Event.EnqueueMainThreadTask(action, "ReloadStepUpClientConfig"));
        clientConfigWatcher.ConfigFileChanged += OnClientConfigFileChanged;
        clientConfigWatcher.Start(capi);
    }

    private void OnClientConfigFileChanged()
    {
        // Same rule as the Reload hotkey. The edit isn't lost: it loads on
        // the next allowed reload or the next session.
        if (EnforcementState.IsClientReloadBlocked(BasicModeState.Active, StepUpOptions.Current))
        {
            ModLog.Verbose(capi, "Client config changed on disk; reload blocked by server enforcement.");
            return;
        }

        ModLog.Event(capi, "Detected client config file change. Reloading...");
        ReloadConfig();
    }

    private void SuppressClientWatcher() => clientConfigWatcher?.Suppress(ClientWatcherSuppressMs);

    public void SuppressWatcher(bool suppress)
    {
        if (suppress) configWatcher?.Suppress(150);
    }

    public override void Dispose()
    {
        configWatcher?.Dispose();
        clientConfigWatcher?.Dispose();
        base.Dispose();
    }
}