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
    // Initialized in StartClientSide / StartServerSide respectively. VS
    // guarantees these methods run before any side-specific path that
    // uses them, so the null-forgiving initializer is sound: client-only
    // code paths only run on instances where StartClientSide ran, and
    // likewise for server. Phase 7b Step 5 moved these off static fields
    // — each ModSystem instance now owns its own references, resolving
    // the cluster of CS8602/CS8604 warnings rooted in the old statics.
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
    // Toast suppression — see Infrastructure/Input/MessageDebouncer.cs.
    // Replaces six shared-purpose hasShown* bool fields. Each named
    // OnceFlag is independent, so e.g. emitting "max-height" no longer
    // suppresses the next "max-speed" toast.
    private readonly MessageDebouncer toasts = new();

    // Step 8.10: the hotkey surface (bindings, callbacks, hold-once
    // trackers) lives in HotkeyHandlers; the /sua command surface (both
    // trees + all handlers) lives in SuaCommands. The ModSystem just
    // constructs and wires them. Client and server are separate instances,
    // so `commands` services whichever side this instance is.
    private SuaCommands? commands;
    private HotkeyHandlers? hotkeys;

    // PhysicsFieldWriter owns BOTH FieldAccessor instances and both
    // lastApplied* caches. Lazy-init against the runtime physics type
    // still happens — it just lives inside the writer now.
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

    // Dual-debounced config-save queue (200 ms callback schedule + 500 ms
    // hard floor). Owns its own lock and timestamp; passed to controllers
    // as a method reference. Phase 7b Step 5 extracted this from the
    // static ConfigQueueLock/saveQueued/lastSaveTime/MinSaveInterval/
    // SafeSaveConfig fields that previously lived here.
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

        // SetupConfigWatcher always succeeds now (the sapi null-guard
        // is gone in Phase 7b Step 5 — sapi is non-null after the
        // assignment above), so configWatcher and configSyncChannel are
        // both safe to pass non-nullable below.
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

        // WorldProbe owns the per-tick scratch state (BlockPos, BlockPos[5],
        // and the cached blacklist HashSet). Constructed before the first
        // tick fires below.
        worldProbe = new WorldProbe();

        // PhysicsFieldWriter owns the two compiled-delegate field
        // accessors and their idempotency caches. Lazy-init against the
        // runtime physics type happens inside the writer on first call.
        physicsWriter = new PhysicsFieldWriter(capi);

        // Dual-debounced save queue (200 ms callback + 500 ms hard floor).
        // Constructed once per client-side ModSystem; both controllers
        // route their persistence through the same instance so the
        // debounce is shared across axes.
        configSaveQueue = new ConfigSaveQueue(capi);

        // Both controllers route persistence through the same
        // ConfigSaveQueue method reference so the 200 ms debounce is
        // shared across axes.
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
        // Hotkey bindings, callbacks, and hold-once trackers live in the
        // registrar. Reload state-orchestration is owned here (ReloadConfig)
        // and passed in as a delegate; the registrar handles only the input
        // concerns (enforcement guard, blocked toast, hold-once gate).
        hotkeys = new HotkeyHandlers(capi, stepController!, elevateController!, toasts, ReloadConfig);
        hotkeys.Register();
    }

    private void RegisterClientCommands()
    {
        commands = new SuaCommands();
        commands.RegisterClient(capi, worldProbe!);
    }

    /// <summary>
    /// Re-loads <see cref="StepUpOptions"/> from disk and re-applies it
    /// through both axes. The cross-cutting reload orchestration — owned by
    /// the composition root because it spans ConfigStore, the world probe,
    /// the physics writer, and both controllers. Invoked by
    /// <see cref="HotkeyHandlers"/> after its enforcement and hold-once
    /// guards pass.
    /// </summary>
    private void ReloadConfig()
    {
        ConfigStore.LoadOrUpgrade(capi);

        // The reload may have introduced new blacklist entries from disk
        // (the SP user's typical "edit JSON + Home key" workflow).
        worldProbe?.Blacklist.MarkDirty();

        // Force the next Apply* on the elevate axis to re-fire; the step
        // axis re-asserts itself via read-back, but invalidating keeps the
        // reload path's intent explicit and covers the elevate path.
        physicsWriter?.InvalidateCache();

        // Each controller refreshes its own internal state from the
        // newly-loaded StepUpOptions.Current and re-applies. The order
        // matters: step must refresh first so its IsEnabled is current
        // when elevateController.OnConfigReloaded queries it.
        stepController?.OnConfigReloaded();
        elevateController?.OnConfigReloaded(stepController?.IsEnabled ?? true);
        ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Ok(ChatFormatting.L("config-reloaded"))}");
    }

    private void OnReceiveServerConfig(ConfigSyncPacket packet)
    {
        // Captured on the network thread, used by the main-thread continuation
        // below. Reading directly from the packet on the network thread is
        // safe (it's already deserialized into a fresh DTO).
        bool showNotice = packet.ShowServerEnforcedNotice;

        capi.Event.EnqueueMainThreadTask(() =>
        {
            // Merge the ten enforcement-relevant fields into Current,
            // preserving every client-local field (StepHeight, StepSpeed,
            // increments, probe tunables, QuietMode, etc.). Pre-3b this
            // was a wholesale ConfigStore.UpdateConfig(config) replace
            // that clobbered all of them.
            //
            // Note: a previous hotfix forced ServerEnforceSettings = false
            // here whenever the client wasn't a remote-MP client. That was
            // silent data loss — single-player and integrated-host players
            // could not legitimately opt in to enforcement on themselves.
            // EnforcementState.IsEnforced now honors the flag verbatim;
            // no per-side override is needed at the receive site.
            ConfigSyncPacketMapper.Apply(StepUpOptions.Current, packet);

            // The packet may have changed the server-side blacklist AND/OR
            // flipped ServerEnforceSettings. Either invalidates the cached
            // union (enforcement-flip changes whether the server list is
            // composed in at all). Mark dirty unconditionally — the next
            // probe call rebuilds from the new effective state.
            worldProbe?.Blacklist.MarkDirty();

            // Per Phase 3b, EnforcementState.IsEnforced is just a flag
            // read on the loaded options. Read it directly here — the
            // ModSystem-level IsEnforced property was removed in Step 3
            // since every other call site moved into the controllers.
            bool isEnforced = StepUpOptions.Current?.ServerEnforceSettings == true;

            if (!isEnforced && toasts.ServerEnforcement.IsShown)
            {
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Ok(ChatFormatting.L("server-enforcement-off"))} " + $"{ChatFormatting.Muted(ChatFormatting.L("local-settings-enabled"))}");
                toasts.ServerEnforcement.Reset();
            }

            // TryShow returns true on the transition into enforced and marks
            // the flag — so we always note the transition, but only emit the
            // chat toast when the server explicitly asked us to (showNotice).
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
    /// Initializes the config-file watcher. The watcher class itself owns
    /// the FileSystemWatcher, debounce timer, and suppression mechanism.
    /// Phase 7b Step 4 moved the <see cref="DebouncedConfigWatcher.ConfigFileChanged"/>
    /// subscription up to <see cref="StartServerSide"/> so it can target
    /// the coordinator (which doesn't exist yet when this runs).
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
        // Kept for back-compat with any external callers; routes through
        // the watcher's Suppress mechanism. The bool argument is now
        // interpreted as: true = suppress for 150 ms, false = no-op.
        if (suppress) configWatcher?.Suppress(150);
    }

    public override void Dispose()
    {
        configWatcher?.Dispose();
        base.Dispose();
    }
}