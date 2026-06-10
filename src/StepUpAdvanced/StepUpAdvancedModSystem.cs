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

    private string configPath => Path.Combine(sapi.GetOrCreateDataPath("ModConfig"), "StepUpAdvancedConfig.json");

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

        configSyncChannel ??= new ConfigSyncChannel();
        configSyncChannel.RegisterClient(api, OnReceiveServerConfig);

        ConfigStore.LoadOrUpgrade(api);
        BlockBlacklistStore.Load(api);

        worldProbe = new WorldProbe();

        physicsWriter = new PhysicsFieldWriter(capi);

        configSaveQueue = new ConfigSaveQueue(capi);

        elevateController = new ElevateFactorController(capi, physicsWriter, toasts, configSaveQueue.RequestSave);
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
        ConfigStore.LoadOrUpgrade(capi);

        worldProbe?.Blacklist.MarkDirty();

        physicsWriter?.InvalidateCache();

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
            filePath: configPath,
            dispatchToMainThread: action => sapi.Event.EnqueueMainThreadTask(action, "ReloadStepUpOptions"));

        configWatcher.Start(sapi);
    }


    public void SuppressWatcher(bool suppress)
    {
        if (suppress) configWatcher?.Suppress(150);
    }

    public override void Dispose()
    {
        configWatcher?.Dispose();
        base.Dispose();
    }
}