using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Domain.Physics;
using StepUpAdvanced.Domain.Probes;
using StepUpAdvanced.Infrastructure.Input;
using StepUpAdvanced.Infrastructure.Network;
using StepUpAdvanced.Infrastructure.Probes;
using StepUpAdvanced.Infrastructure.Reflection;

// Phase 1: SuaChat and SuaCmd moved to StepUpAdvanced.Core. These aliases keep
// existing call sites in this file compiling unchanged. Phase 8 sweeps call
// sites to the new full names and removes the aliases.
using SuaChat = StepUpAdvanced.Core.ChatFormatting;
using SuaCmd = StepUpAdvanced.Core.CommandResults;

namespace StepUpAdvanced;

public class StepUpAdvancedModSystem : ModSystem
{
    private DateTime lastSaveTime = DateTime.MinValue;
    private static readonly TimeSpan MinSaveInterval = TimeSpan.FromSeconds(0.5);

    private void SafeSaveConfig(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client) return;
        var now = DateTime.UtcNow;
        if (now - lastSaveTime < MinSaveInterval)
        {
            /*lock (ConfigQueueLock)
            {
                saveQueued = true;
            }*/
            return;
        }
        lastSaveTime = now;
        ConfigStore.Save(api);
    }

    private bool stepUpEnabled = true;

    private DebouncedConfigWatcher? configWatcher;
    private ConfigSyncChannel? configSyncChannel;
    private string configPath => Path.Combine(sapi?.GetOrCreateDataPath("ModConfig") ?? "", "StepUpAdvancedConfig.json");

    private bool IsEnforced
    {
        get
        {
            // Preserve original guard: if neither side has initialized yet,
            // never report as enforced. Avoids consulting the config flag
            // before there's any side context to interpret it in.
            if (sapi == null && capi == null) return false;
            return EnforcementState.IsEnforced(
                sapi != null ? EnumAppSide.Server : EnumAppSide.Client,
                capi?.IsSinglePlayer ?? false,
                StepUpOptions.Current);
        }
    }

    private static ICoreClientAPI? capi;
    private static ICoreServerAPI? sapi;

    private static readonly object ConfigQueueLock = new();
    private static volatile bool saveQueued;

    // Step-height and step-speed constants live on the Domain classes:
    //   StepHeightClamp.ClientMin / .Default
    //   ElevateFactorMath.ClientMin / .Default
    // Pre-Phase-5 they were public consts on this class. The four "absolute
    // max" / "min increment" constants were declared but unused — deleted
    // outright; if a concrete use case ever shows up, the right place to
    // add them is on the Domain class, not here.

    private float currentElevateFactor;
    private float currentStepHeight;

    private bool elevateWarnedOnce = false;
    private bool stepHeightWarnedOnce = false;
    private bool warnedPlayerNullOnce = false;
    private bool warnedPhysNullOnce = false;

    // Toast suppression — see Infrastructure/Input/MessageDebouncer.cs.
    // Replaces six shared-purpose hasShown* bool fields. Each named
    // OnceFlag is independent, so e.g. emitting "max-height" no longer
    // suppresses the next "max-speed" toast.
    private readonly MessageDebouncer toasts = new();

    // Hold-once trackers for toggle and reload — see KeyHoldTracker.cs.
    // Initialized in RegisterHotkeys (after capi is available); null
    // before that point. Holders self-subscribe to capi.Event.KeyUp.
    private KeyHoldTracker? toggleHeld;
    private KeyHoldTracker? reloadHeld;

    // Compiled-delegate field accessors for the two reflected hot-path
    // writes. Lazy-initialized in the apply methods once physicsBehavior
    // is first observed (so we resolve against the runtime type, which
    // may be a subclass of EntityBehaviorControlledPhysics with shadowed
    // fields). Pre-Phase-6 this was FieldInfo + SetValue, which boxed the
    // float/double argument on every per-tick call.
    private FieldAccessor<EntityBehaviorControlledPhysics, float>? stepHeightAccessor;
    private FieldAccessor<EntityBehaviorControlledPhysics, double>? elevateAccessor;
    private float lastAppliedStepHeight = float.NaN;
    private double lastAppliedElevate = double.NaN;

    // Per-tick probe state — scratch BlockPos, BlockPos[5] column buffer,
    // and the HashSet-cached blacklist (rebuilt on demand at mutation
    // sites). One instance per ModSystem; client-side only.
    private WorldProbe? worldProbe;

    private float ClampHeightClient(float height)
        => StepHeightClamp.Clamp(
            height,
            IsEnforced,
            StepUpOptions.Current.ServerMinStepHeight,
            StepUpOptions.Current.ServerMaxStepHeight);

    private float ClampSpeedClient(float speed)
        => ElevateFactorMath.Clamp(
            speed,
            IsEnforced,
            StepUpOptions.Current.ServerMinStepSpeed,
            StepUpOptions.Current.ServerMaxStepSpeed);

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
        RegisterServerCommands();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        configSyncChannel ??= new ConfigSyncChannel();
        configSyncChannel.RegisterClient(api, OnReceiveServerConfig);

        ConfigStore.LoadOrUpgrade(api);
        BlockBlacklistStore.Load(api);

        currentStepHeight = StepUpOptions.Current?.StepHeight ?? StepHeightClamp.Default;
        currentElevateFactor = StepUpOptions.Current?.StepSpeed ?? ElevateFactorMath.Default;

        bool changed = false;
        float newHeight = ClampHeightClient(currentStepHeight);
        float newSpeed = ClampSpeedClient(currentElevateFactor);
        if (newHeight != currentStepHeight) { currentStepHeight = newHeight; changed = true; }
        if (newSpeed != currentElevateFactor) { currentElevateFactor = newSpeed; changed = true; }
        if (changed)
        {
            StepUpOptions.Current.StepHeight = currentStepHeight;
            StepUpOptions.Current.StepSpeed = currentElevateFactor;
            QueueConfigSave(capi);
            ModLog.Verbose(capi, "Normalized runtime StepHeight/StepSpeed (client floors; server caps only if enforced).");
        }

        // WorldProbe owns the per-tick scratch state (BlockPos, BlockPos[5],
        // and the cached blacklist HashSet). Constructed before the first
        // tick fires below.
        worldProbe = new WorldProbe();

        // stepHeightAccessor and elevateAccessor stay null here — they're
        // lazy-init in their respective Apply methods once physicsBehavior
        // is first observed, so we can resolve fields against the runtime
        // type (which may be a subclass of EntityBehaviorControlledPhysics).

        stepUpEnabled = StepUpOptions.Current?.StepUpEnabled ?? true;

        RegisterHotkeys();
        capi.Event.RegisterGameTickListener((dt) => { ApplyStepHeightToPlayer(); }, 50);
        ApplyElevateFactorToPlayer(16f);
        RegisterCommands();
    }

    private void OnReceiveServerConfig(ConfigSyncPacket packet)
    {
        if (capi == null) return;

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

            if (!IsEnforced && toasts.ServerEnforcement.IsShown)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Ok(SuaChat.L("server-enforcement-off"))} " + $"{SuaChat.Muted(SuaChat.L("local-settings-enabled"))}");
                toasts.ServerEnforcement.Reset();
            }

            // TryShow returns true on the transition into enforced and marks
            // the flag — so we always note the transition, but only emit the
            // chat toast when the server explicitly asked us to (showNotice).
            if (IsEnforced && toasts.ServerEnforcement.TryShow())
            {
                if (showNotice)
                {
                    SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforcement-on"))} " + $"{SuaChat.Muted(SuaChat.L("local-settings-disabled"))}");
                }
            }

            ApplyStepHeightToPlayer();
            ApplyElevateFactorToPlayer(16f);
        }, "ApplyStepUpServerConfig");
    }

    private void RegisterCommands()
    {
        capi.ChatCommands.Create("sua").WithDescription(Lang.Get("desc.sua.client")).RequiresPrivilege("chat")
            .BeginSubCommand("add")
            .WithDescription(Lang.Get("desc.sua.add"))
            .HandleWith(AddToBlacklist)
            .EndSubCommand()
            .BeginSubCommand("remove")
            .WithDescription(Lang.Get("desc.sua.remove"))
            .HandleWith(RemoveFromBlacklist)
            // Nested 'all' sub-command — '.sua remove all' clears the
            // entire client-side list. Independent of the server-side
            // '/sua remove all'; the client config is owned locally and
            // is never touched by server admin actions.
            .BeginSubCommand("all")
            .WithDescription(Lang.Get("desc.sua.remove-all-client"))
            .HandleWith(ClearClientBlacklist)
            .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("list")
            .WithDescription(Lang.Get("desc.sua.list"))
            .HandleWith(ListBlacklist)
            .EndSubCommand();
    }
    private void RegisterServerCommands()
    {
        sapi.ChatCommands.Create("sua").WithDescription(Lang.Get("desc.sua.server")).RequiresPrivilege("controlserver")
            .BeginSubCommand("add")
            .WithDescription(Lang.Get("desc.sua.add"))
            .HandleWith(AddToServerBlacklist)
            .EndSubCommand()
            .BeginSubCommand("remove")
            .WithDescription(Lang.Get("desc.sua.remove"))
            .HandleWith(RemoveFromServerBlacklist)
            // Nested 'all' sub-command — '/sua remove all' clears the
            // entire server-side list. Pointedly does NOT touch any
            // connected client's local list (those live in a separate
            // config that the client owns).
            .BeginSubCommand("all")
            .WithDescription(Lang.Get("desc.sua.remove-all-server"))
            .HandleWith(ClearServerBlacklist)
            .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("reload")
            .WithDescription(Lang.Get("desc.sua.reload"))
            .HandleWith(ReloadServerConfig)
            .EndSubCommand();
    }
    private IClientPlayer ValidatePlayer(TextCommandCallingArgs args, out int groupId)
    {
        groupId = args.Caller.FromChatGroupId;
        if (!(args.Caller.Player is IClientPlayer result))
        {
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Err(SuaChat.L("cmd.must-be-player"))}");
            return null;
        }
        return result;
    }
    private TextCommandResult AddToBlacklist(TextCommandCallingArgs arg)
    {
        int groupId;
        IClientPlayer clientPlayer = ValidatePlayer(arg, out groupId);
        if (clientPlayer == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.must-be-player")));

        var blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null)
            return SuaCmd.Err(SuaChat.L("cmd.no-block-targeted"));

        string blockCode = blockSel.Block.Code.ToString();

        if (BlockBlacklistOptions.Current.BlockCodes.Contains(blockCode))
            return SuaCmd.Warn(SuaChat.L("cmd.already-listed"), $"{SuaChat.Val(blockCode)} {SuaChat.Muted(SuaChat.L("cmd.is-on-blacklist"))}");

        BlockBlacklistOptions.Current.BlockCodes.Add(blockCode);
        BlockBlacklistStore.Save(capi);
        worldProbe?.Blacklist.MarkDirty();
        return SuaCmd.Ok(SuaChat.L("cmd.added-client-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");
    }
    private TextCommandResult RemoveFromBlacklist(TextCommandCallingArgs arg)
    {
        int groupId;
        IClientPlayer clientPlayer = ValidatePlayer(arg, out groupId);
        if (clientPlayer == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.must-be-player")));

        var blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null)
            return SuaCmd.Err(SuaChat.L("cmd.no-block-targeted"));

        string blockCode = blockSel.Block.Code.ToString();

        if (!BlockBlacklistOptions.Current.BlockCodes.Contains(blockCode))
            return SuaCmd.Warn(SuaChat.L("cmd.not-listed-client"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");

        BlockBlacklistOptions.Current.BlockCodes.Remove(blockCode);
        BlockBlacklistStore.Save(capi);
        worldProbe?.Blacklist.MarkDirty();
        return SuaCmd.Ok(SuaChat.L("cmd.removed-client-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");
    }

    /// <summary>
    /// Handler for <c>.sua remove all</c>. Wipes the local client-side
    /// blacklist (<see cref="BlockBlacklistOptions.Current.BlockCodes"/>).
    /// Independent of the server-side blacklist; <c>/sua remove all</c>
    /// on the server-side never touches this list.
    /// </summary>
    private TextCommandResult ClearClientBlacklist(TextCommandCallingArgs arg)
    {
        IClientPlayer clientPlayer = ValidatePlayer(arg, out _);
        if (clientPlayer == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.must-be-player")));

        var list = BlockBlacklistOptions.Current?.BlockCodes;
        int removed = list?.Count ?? 0;

        if (removed == 0)
            return SuaCmd.Info(SuaChat.L("cmd.blacklist-already-empty-client"));

        list!.Clear();
        BlockBlacklistStore.Save(capi);
        worldProbe?.Blacklist.MarkDirty();
        return SuaCmd.Ok(SuaChat.L("cmd.cleared-client-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(removed.ToString())}");
    }
    private TextCommandResult ListBlacklist(TextCommandCallingArgs arg)
    {
        var serverList = StepUpOptions.Current?.BlockBlacklist ?? new List<string>();
        var clientList = BlockBlacklistOptions.Current?.BlockCodes ?? new List<string>();

        var merged = new HashSet<string>(serverList);
        merged.UnionWith(clientList);

        if (merged.Count == 0)
            return SuaCmd.Info(SuaChat.L("cmd.none-blacklisted"));

        var sorted = merged.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        return SuaCmd.List(SuaChat.L("cmd.blacklisted-blocks-title"), sorted);
    }
    private TextCommandResult AddToServerBlacklist(TextCommandCallingArgs arg)
    {
        var player = arg.Caller.Player as IServerPlayer;
        if (player == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.must-be-player")));

        var blockSel = player.CurrentBlockSelection;
        if (blockSel == null)
            return SuaCmd.Err(SuaChat.L("cmd.no-block-targeted"));

        string blockCode = blockSel.Block.Code.ToString();

        if (StepUpOptions.Current.BlockBlacklist.Contains(blockCode))
            return SuaCmd.Warn(SuaChat.L("cmd.already-listed-server"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");

        StepUpOptions.Current.BlockBlacklist.Add(blockCode);
        configWatcher?.Suppress(150);
        ConfigStore.Save(sapi);
        // Watcher is suppressed so OnConfigFileChanged won't fire for
        // this write — broadcast explicitly so connected clients see
        // the new entry without waiting for /sua reload or reconnect.
        configSyncChannel?.BroadcastToAll();
        // In SP, the server-side handler and the client's WorldProbe live
        // in the same process — mark the cache dirty immediately so the
        // next probe tick rebuilds. On a dedicated server, worldProbe is
        // null and this is a no-op (remote clients invalidate via the
        // broadcast → OnReceiveServerConfig path).
        worldProbe?.Blacklist.MarkDirty();
        return SuaCmd.Ok(SuaChat.L("cmd.added-server-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");
    }
    private TextCommandResult RemoveFromServerBlacklist(TextCommandCallingArgs arg)
    {
        var player = arg.Caller.Player as IServerPlayer;
        if (player == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.must-be-player")));

        var blockSel = player.CurrentBlockSelection;
        if (blockSel == null)
            return SuaCmd.Err(SuaChat.L("cmd.no-block-targeted"));

        string blockCode = blockSel.Block.Code.ToString();

        if (!StepUpOptions.Current.BlockBlacklist.Contains(blockCode))
            return SuaCmd.Warn(SuaChat.L("cmd.not-on-server-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");

        StepUpOptions.Current.BlockBlacklist.Remove(blockCode);
        configWatcher?.Suppress(150);
        ConfigStore.Save(sapi);
        // Watcher is suppressed so OnConfigFileChanged won't fire —
        // broadcast explicitly so connected clients see the removal
        // without waiting for /sua reload or reconnect.
        configSyncChannel?.BroadcastToAll();
        worldProbe?.Blacklist.MarkDirty();
        return SuaCmd.Ok(SuaChat.L("cmd.removed-server-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");
    }

    /// <summary>
    /// Handler for <c>/sua remove all</c>. Wipes the server-side
    /// blacklist (<see cref="StepUpOptions.Current.BlockBlacklist"/>),
    /// saves to disk, and broadcasts the change to every connected
    /// client. Pointedly does NOT touch any client's local blacklist —
    /// <c>BlockBlacklistOptions</c> is owned by the client and lives
    /// in a separate config file.
    /// </summary>
    private TextCommandResult ClearServerBlacklist(TextCommandCallingArgs arg)
    {
        if (sapi == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.server-api-not-initialized")));

        var list = StepUpOptions.Current?.BlockBlacklist;
        int removed = list?.Count ?? 0;

        if (removed == 0)
            return SuaCmd.Info(SuaChat.L("cmd.blacklist-already-empty-server"));

        list!.Clear();
        configWatcher?.Suppress(150);
        ConfigStore.Save(sapi);
        // Suppress(150) prevents the FileSystemWatcher from re-detecting
        // our own write and triggering OnConfigFileChanged (which would
        // double-broadcast). The downside is that the watcher's
        // broadcast path doesn't fire either, so we have to broadcast
        // explicitly here. Same pattern as ReloadServerConfig.
        configSyncChannel?.BroadcastToAll();
        worldProbe?.Blacklist.MarkDirty();
        return SuaCmd.Ok(SuaChat.L("cmd.cleared-server-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(removed.ToString())}");
    }
    private TextCommandResult ReloadServerConfig(TextCommandCallingArgs arg)
    {
        if (sapi == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.must-be-player")));

        configWatcher?.Suppress(150);
        ConfigStore.LoadOrUpgrade(sapi);

        configSyncChannel?.BroadcastToAll();

        if (StepUpOptions.Current.ServerEnforceSettings)
            return SuaCmd.Ok(SuaChat.L("cmd.server-config-reloaded"),
                $"{SuaChat.Muted(SuaChat.L("cmd.pushed"))} {SuaChat.Warn(SuaChat.L("cmd.enforced"))} {SuaChat.Muted(SuaChat.L("cmd.config"))}");

        return SuaCmd.Ok(SuaChat.L("cmd.server-config-reloaded"), SuaChat.Muted(SuaChat.L("cmd.clients-may-use-local")));
    }
    private void RegisterHotkeys()
    {
        var binder = new HotkeyBinder(capi);
        binder.Bind("increaseStepHeight", Lang.Get("key.increase-height"), GlKeys.PageUp,   OnIncreaseStepHeight);
        binder.Bind("decreaseStepHeight", Lang.Get("key.decrease-height"), GlKeys.PageDown, OnDecreaseStepHeight);
        binder.Bind("increaseStepSpeed",  Lang.Get("key.increase-speed"),  GlKeys.Up,       OnIncreaseElevateFactor);
        binder.Bind("decreaseStepSpeed",  Lang.Get("key.decrease-speed"),  GlKeys.Down,     OnDecreaseElevateFactor);
        binder.Bind("toggleStepUp",       Lang.Get("key.toggle"),          GlKeys.Insert,   OnToggleStepUp);
        binder.Bind("reloadConfig",       Lang.Get("key.reload"),          GlKeys.Home,     OnReloadConfig);

        // KeyHoldTracker subscribes to capi.Event.KeyUp internally for its
        // hotkey id and clears the held flag on key release. Constructed
        // after binder.Bind so the hotkey is in the HotKeys dictionary
        // when the tracker first resolves CurrentMapping.
        toggleHeld = new KeyHoldTracker(capi, "toggleStepUp");
        reloadHeld = new KeyHoldTracker(capi, "reloadConfig");
    }
    private bool OnIncreaseStepHeight(KeyCombination comb)
    {
        if (StepUpOptions.Current?.SpeedOnlyMode == true)
        {
            if (toasts.HeightSpeedOnlyMode.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("speed-only-mode"))} – {SuaChat.Muted(SuaChat.L("height-controls-disabled"))}");
            return false;
        }
        toasts.HeightSpeedOnlyMode.Reset();

        if (IsEnforced && !StepUpOptions.Current.AllowClientChangeStepHeight)
        {
            if (toasts.HeightEnforced.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("height-change-blocked"))}");
            return false;
        }
        toasts.HeightEnforced.Reset();

        if (IsEnforced && Math.Abs(currentStepHeight - StepUpOptions.Current.ServerMaxStepHeight) < 0.01f)
        {
            if (toasts.HeightAtMax.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("max-height"))} {SuaChat.Arrow} {SuaChat.Val($"{StepUpOptions.Current.ServerMaxStepHeight:0.0} blocks")}");
            return false;
        }
        toasts.HeightAtMax.Reset();

        float previousStepHeight = currentStepHeight;
        currentStepHeight += Math.Max(StepUpOptions.Current.StepHeightIncrement, 0.1f);

        currentStepHeight = ClampHeightClient(currentStepHeight);

        if (currentStepHeight > previousStepHeight)
        {
            StepUpOptions.Current.StepHeight = currentStepHeight;
            QueueConfigSave(capi);
            ApplyStepHeightToPlayer();
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("height"))} {SuaChat.Arrow} {SuaChat.Val($"{currentStepHeight:0.0} blocks")}");
        }
        return true;
    }
    private bool OnDecreaseStepHeight(KeyCombination comb)
    {
        if (StepUpOptions.Current?.SpeedOnlyMode == true)
        {
            if (toasts.HeightSpeedOnlyMode.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("speed-only-mode"))} – {SuaChat.Muted(SuaChat.L("height-controls-disabled"))}");
            return false;
        }
        toasts.HeightSpeedOnlyMode.Reset();

        if (IsEnforced && !StepUpOptions.Current.AllowClientChangeStepHeight)
        {
            if (toasts.HeightEnforced.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("height-change-blocked"))}");
            return false;
        }
        toasts.HeightEnforced.Reset();

        if (IsEnforced && Math.Abs(currentStepHeight - StepUpOptions.Current.ServerMinStepHeight) < 0.01f)
        {
            if (toasts.HeightAtMin.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("min-height"))} {SuaChat.Arrow} {SuaChat.Val($"{Math.Max(StepHeightClamp.ClientMin, StepUpOptions.Current.ServerMinStepHeight):0.0} blocks")}");
            return false;
        }
        toasts.HeightAtMin.Reset();

        float previousStepHeight = currentStepHeight;
        currentStepHeight -= Math.Max(StepUpOptions.Current.StepHeightIncrement, 0.1f);

        currentStepHeight = ClampHeightClient(currentStepHeight);

        if (currentStepHeight < previousStepHeight)
        {
            // True when this descent lands us at (or below) the client
            // hard floor. Used both to decide whether to emit the
            // "Minimum height" limit toast and to suppress the generic
            // "Height » X" update that would otherwise double up on the
            // same press — see CHANGELOG entry "Phase 4 polish".
            bool atFloor = currentStepHeight <= StepHeightClamp.ClientMin;

            // Post-clamp at-floor toast: same flag as the server-floor branch
            // above — they emit the same "min-height" text and should share
            // suppression. (This site previously also used hasShownMinMessage,
            // which was correct; the one bug here was the cross-axis sharing
            // with speed-at-min, which the split into HeightAtMin / SpeedAtMin
            // resolves.)
            if (atFloor && toasts.HeightAtMin.TryShow())
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("min-height"))} {SuaChat.Arrow} {SuaChat.Val($"{Math.Max(StepHeightClamp.ClientMin, StepUpOptions.Current.ServerMinStepHeight):0.0} blocks")}");
            }
            StepUpOptions.Current.StepHeight = currentStepHeight;
            QueueConfigSave(capi);
            ApplyStepHeightToPlayer();
            if (!atFloor)
            {
                // The "Minimum height" toast above already carries the value,
                // so emitting "Height » 0.6" right after it is redundant on
                // the press that lands at the floor.
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("height"))} {SuaChat.Arrow} {SuaChat.Val($"{currentStepHeight:0.0} blocks")}");
            }
        }
        return true;
    }
    private bool OnIncreaseElevateFactor(KeyCombination comb)
    {
        if (IsEnforced && !StepUpOptions.Current.AllowClientChangeStepSpeed)
        {
            if (toasts.SpeedEnforced.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("speed-change-blocked"))}");
            return false;
        }
        toasts.SpeedEnforced.Reset();

        if (IsEnforced && Math.Abs(currentElevateFactor - StepUpOptions.Current.ServerMaxStepSpeed) < 0.01f)
        {
            if (toasts.SpeedAtMax.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("max-speed"))} {SuaChat.Arrow} {SuaChat.Val($"{StepUpOptions.Current.ServerMaxStepSpeed:0.0}")}");
            return false;
        }
        toasts.SpeedAtMax.Reset();

        float previousElevateFactor = currentElevateFactor;
        currentElevateFactor += Math.Max(StepUpOptions.Current.StepSpeedIncrement, 0.01f);
        currentElevateFactor = ClampSpeedClient(currentElevateFactor);

        if (currentElevateFactor > previousElevateFactor)
        {
            StepUpOptions.Current.StepSpeed = currentElevateFactor;
            QueueConfigSave(capi);
            ApplyElevateFactorToPlayer(16f);
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("speed"))} {SuaChat.Arrow} {SuaChat.Val($"{currentElevateFactor:0.0}")}");
        }
        return true;
    }
    private bool OnDecreaseElevateFactor(KeyCombination comb)
    {
        if (IsEnforced && !StepUpOptions.Current.AllowClientChangeStepSpeed)
        {
            if (toasts.SpeedEnforced.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("speed-change-blocked"))}");
            return false;
        }
        toasts.SpeedEnforced.Reset();

        if (IsEnforced && Math.Abs(currentElevateFactor - StepUpOptions.Current.ServerMinStepSpeed) < 0.01f)
        {
            if (toasts.SpeedAtMin.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("min-speed"))} {SuaChat.Arrow} {SuaChat.Val($"{StepUpOptions.Current.ServerMinStepSpeed:0.0}")}");
            return false;
        }
        toasts.SpeedAtMin.Reset();

        float previousElevateFactor = currentElevateFactor;
        currentElevateFactor -= Math.Max(StepUpOptions.Current.StepSpeedIncrement, 0.01f);
        currentElevateFactor = ClampSpeedClient(currentElevateFactor);

        if (currentElevateFactor < previousElevateFactor)
        {
            // True when this descent lands us at (or below) the client
            // hard floor. See OnDecreaseStepHeight for the rationale —
            // same redundant-double-toast bug, same shape of fix.
            bool atFloor = currentElevateFactor <= ElevateFactorMath.ClientMin;

            // Pre-Phase-4 this branch gated on hasShownMinEMessage — the
            // enforced-blocked flag, not the at-min flag. So if the user had
            // just bounced off the enforced-blocked message earlier in the
            // session, reaching the speed floor here would silently swallow
            // the "min-speed" toast. Now correctly gated on SpeedAtMin.
            if (atFloor && toasts.SpeedAtMin.TryShow())
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("min-speed"))} {SuaChat.Arrow} {SuaChat.Val($"{ElevateFactorMath.ClientMin:0.0}")}");
            }
            StepUpOptions.Current.StepSpeed = currentElevateFactor;
            QueueConfigSave(capi);
            ApplyElevateFactorToPlayer(16f);
            if (!atFloor)
            {
                // Skip the generic "Speed » 0.7" update when the
                // "Minimum speed" toast above already carried the value.
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("speed"))} {SuaChat.Arrow} {SuaChat.Val($"{currentElevateFactor:0.0}")}");
            }
        }
        return true;
    }
    private bool OnToggleStepUp(KeyCombination comb)
    {
        if (toggleHeld?.TryFire() != true)
        {
            return false;
        }
        stepUpEnabled = !stepUpEnabled;
        StepUpOptions.Current.StepUpEnabled = stepUpEnabled;
        QueueConfigSave(capi);
        ApplyStepHeightToPlayer();
        ApplyElevateFactorToPlayer(16f);
        SuaChat.Client(capi, stepUpEnabled
            ? $"{SuaChat.Tag} {SuaChat.Ok(SuaChat.L("stepup-enabled"))}"
            : $"{SuaChat.Tag} {SuaChat.Err(SuaChat.L("stepup-disabled"))}");
        return true;
    }
    private bool OnReloadConfig(KeyCombination comb)
    {
        if (IsEnforced && !StepUpOptions.Current.AllowClientConfigReload)
        {
            if (toasts.ReloadBlocked.TryShow())
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("reload-blocked"))}");
            return false;
        }
        // Reset position deliberately mirrors the pre-Phase-4
        // hasShownMaxMessage = false at the same point: after the
        // enforcement guard passes, before the hold-guard. So the
        // ReloadBlocked toast re-arms as soon as enforcement permits
        // reload again, not only after a successful reload.
        toasts.ReloadBlocked.Reset();

        if (reloadHeld?.TryFire() != true)
        {
            return false;
        }
        ConfigStore.LoadOrUpgrade(capi);
        currentStepHeight = StepUpOptions.Current?.StepHeight ?? currentStepHeight;
        currentElevateFactor = StepUpOptions.Current?.StepSpeed ?? currentElevateFactor;
        stepUpEnabled = StepUpOptions.Current?.StepUpEnabled ?? stepUpEnabled;

        // The reload may have introduced new blacklist entries from disk
        // (the SP user's typical "edit JSON + Home key" workflow).
        worldProbe?.Blacklist.MarkDirty();

        lastAppliedStepHeight = float.NaN;
        lastAppliedElevate = double.NaN;

        ApplyStepHeightToPlayer();
        ApplyElevateFactorToPlayer(16f);
        SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Ok(SuaChat.L("config-reloaded"))}");
        return true;
    }
    /// <summary>
    /// Thin wrapper over <see cref="WorldProbe.NearBlacklistedBlock"/>.
    /// Encodes the policy decision: when enforcement is off, the
    /// server-side list is excluded from the union; the client-side list
    /// is always consulted.
    /// </summary>
    private bool IsNearBlacklistedBlock(IClientPlayer player)
    {
        if (worldProbe == null) return false;

        // Caller composes the effective lists; WorldProbe.BlacklistCache
        // observes a different reference (or null) when enforcement
        // transitions, which the MarkBlacklistDirty hooks at the
        // enforcement-change sites turn into a cache rebuild.
        var serverList = IsEnforced ? StepUpOptions.Current?.BlockBlacklist : null;
        var clientList = BlockBlacklistOptions.Current?.BlockCodes;

        return worldProbe.NearBlacklistedBlock(
            player.Entity.World,
            player.Entity.Pos.AsBlockPos,
            player.Entity.Pos.Motion,
            serverList,
            clientList);
    }
    private void ApplyStepHeightToPlayer()
    {
        if (StepUpOptions.Current?.SpeedOnlyMode == true) return;
        IClientPlayer player = capi.World?.Player;
        if (player == null)
        {
            if (!warnedPlayerNullOnce)
            {
                ModLog.Warning(capi, "Player object is null. Cannot apply step height.");
                warnedPlayerNullOnce = true;
            }
            return;
        }
        warnedPlayerNullOnce = false;

        EntityBehaviorControlledPhysics physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null)
        {
            if (!warnedPhysNullOnce)
            {
                ModLog.Warning(capi, "Physics behavior is missing on player entity. Cannot apply step height.");
                warnedPhysNullOnce = true;
            }
            return;
        }
        warnedPhysNullOnce = false;

        bool nearBlacklistedBlock = IsNearBlacklistedBlock(player);

        float stepHeight =
            !stepUpEnabled ? 0.6f :
            nearBlacklistedBlock ? StepUpOptions.Current.DefaultHeight :
            ClampHeightClient(currentStepHeight);

        if (StepUpOptions.Current.CeilingGuardEnabled && stepHeight > 0f)
        {
            float clearance = DistanceToCeiling(player, stepHeight);
            if (clearance <= 0.75f) stepHeight = Math.Min(stepHeight, StepUpOptions.Current.DefaultHeight);
            else if (clearance < stepHeight) stepHeight = clearance;
        }

        if (Math.Abs(stepHeight - lastAppliedStepHeight) < 1e-4f) return;

        // Lazy-init against the runtime type — physicsBehavior may be a
        // subclass of EntityBehaviorControlledPhysics with a shadowed
        // StepHeight field. The compiled-delegate setter eliminates the
        // per-call boxing that FieldInfo.SetValue performed pre-Phase-6.
        stepHeightAccessor ??= new FieldAccessor<EntityBehaviorControlledPhysics, float>(
            physicsBehavior.GetType(), "StepHeight", "stepHeight");

        if (stepHeightAccessor.TrySet(physicsBehavior, stepHeight))
        {
            lastAppliedStepHeight = stepHeight;
        }
        else
        {
            if (!stepHeightWarnedOnce)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Err(SuaChat.L("error.stepheight-field-missing"))}");
                stepHeightWarnedOnce = true;
            }
        }
    }
    private void ApplyElevateFactorToPlayer(float dt)
    {
        var player = capi.World?.Player;
        if (player?.Entity == null) return;

        var physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null) return;

        float logicalSpeed = IsEnforced ? StepUpOptions.Current.StepSpeed : currentElevateFactor;
        logicalSpeed = ClampSpeedClient(logicalSpeed);

        double desiredElevate = (stepUpEnabled ? logicalSpeed : StepUpOptions.Current.DefaultSpeed) * 0.05;

        if (Math.Abs(desiredElevate - lastAppliedElevate) < 1e-6) return;

        // Same lazy-init pattern as stepHeightAccessor — resolved against
        // the runtime type. The VS field name is lowercase 'elevateFactor';
        // 'ElevateFactor' is a defensive fallback for future API renames.
        elevateAccessor ??= new FieldAccessor<EntityBehaviorControlledPhysics, double>(
            physicsBehavior.GetType(), "elevateFactor", "ElevateFactor");

        if (elevateAccessor.TrySet(physicsBehavior, desiredElevate))
        {
            lastAppliedElevate = desiredElevate;
        }
        else if (!elevateWarnedOnce)
        {
            ModLog.Warning(capi, "Failed to resolve elevateFactor field on physics behavior; speed adjustments disabled.");
            elevateWarnedOnce = true;
        }
    }

    /// <summary>
    /// Initializes the config-file watcher. The watcher class itself owns
    /// the FileSystemWatcher, debounce timer, and suppression mechanism;
    /// this method just wires the resulting <see cref="DebouncedConfigWatcher.ConfigFileChanged"/>
    /// event to the reload-and-broadcast workflow.
    /// </summary>
    private void SetupConfigWatcher()
    {
        if (sapi == null) return;

        configWatcher = new DebouncedConfigWatcher(
            filePath: configPath,
            dispatchToMainThread: action => sapi.Event.EnqueueMainThreadTask(action, "ReloadStepUpOptions"));

        configWatcher.ConfigFileChanged += OnConfigFileChanged;

        configWatcher.Start(sapi);
    }

    /// <summary>
    /// Fired (on the main thread) by <see cref="DebouncedConfigWatcher"/>
    /// once per debounced file change. Reloads the config and broadcasts
    /// the new state to all connected clients.
    /// </summary>
    private void OnConfigFileChanged()
    {
        if (sapi == null) return;

        ModLog.Event(sapi, "Detected config file change. Reloading...");
        ConfigStore.LoadOrUpgrade(sapi);

        configSyncChannel?.BroadcastToAll();

        if (StepUpOptions.Current.ServerEnforceSettings)
            ModLog.Verbose(sapi, "Server-enforced config pushed to all clients.");
        else
            ModLog.Verbose(sapi, "Config pushed (server enforcement disabled, allows client-side config again).");
    }

    /// <summary>
    /// Orchestrator: combines the "here" clearance check with the
    /// forward-column check guarded by <c>RequireForwardSupport</c> and
    /// <c>ForwardProbeCeiling</c> flags. Per-cell world queries live on
    /// <see cref="WorldProbe"/>; math lives on
    /// <see cref="CeilingProbeMath"/>; this method holds the policy.
    /// </summary>
    private float DistanceToCeiling(IClientPlayer player, float requestedStep)
    {
        if (worldProbe == null) return requestedStep;

        var world = player.Entity.World;
        var basePos = player.Entity.Pos.AsBlockPos;
        var cfg = StepUpOptions.Current;

        float hereClear = worldProbe.DistanceToCeilingAt(
            world, basePos, requestedStep, startDy: 1, headroomPad: cfg.CeilingHeadroomPad);

        if (!cfg.ForwardProbeCeiling || cfg.ForwardProbeDistance <= 0)
            return hereClear;

        double yaw = player.Entity.Pos.Yaw;
        bool supportedAhead = worldProbe.HasLandingSupport(world, basePos, yaw, cfg.ForwardProbeDistance, requestedStep);
        float tinySafe = Math.Max(0.25f, cfg.DefaultHeight);
        if (cfg.RequireForwardSupport && !supportedAhead) return Math.Min(hereClear, tinySafe);

        float entHeight = player.Entity.CollisionBox.Y2 - player.Entity.CollisionBox.Y1;
        var (yFrom, yTopInclusive) = CeilingProbeMath.LandingClearanceRange(
            basePos.Y, requestedStep, entHeight, cfg.CeilingHeadroomPad);
        if (yTopInclusive < yFrom) return hereClear;

        int columnCount = worldProbe.BuildForwardColumns(basePos, yaw, cfg.ForwardProbeDistance, cfg.ForwardProbeSpan);
        bool blockedAll = true;
        for (int i = 0; i < columnCount; i++)
        {
            var col = worldProbe.GetColumn(i);
            if (!worldProbe.ColumnHasSolid(world, col, yFrom, yTopInclusive + 1))
            {
                blockedAll = false;
                break;
            }
        }
        return blockedAll ? Math.Min(hereClear, tinySafe) : hereClear;
    }

    private void QueueConfigSave(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client) return;

        lock (ConfigQueueLock)
        {
            if (saveQueued) return;
            saveQueued = true;
        }

        capi?.Event.RegisterCallback(_ =>
        {
            lock (ConfigQueueLock) saveQueued = false;
            SafeSaveConfig(api);
        }, 200);
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