using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Domain.Blocks;
using StepUpAdvanced.Domain.Physics;
using StepUpAdvanced.Domain.Probes;
using StepUpAdvanced.Infrastructure.Input;
using StepUpAdvanced.Infrastructure.Network;

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

    private FieldInfo? fiStepHeight;
    private FieldInfo? fiElevateFactor;
    private float lastAppliedStepHeight = float.NaN;
    private double lastAppliedElevate = double.NaN;
    private readonly BlockPos scratchPos = new BlockPos(0);

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

        var basePhys = typeof(EntityBehaviorControlledPhysics);
        fiElevateFactor = basePhys.GetField("elevateFactor", BindingFlags.Instance | BindingFlags.NonPublic)
                       ?? basePhys.GetField("ElevateFactor", BindingFlags.Instance | BindingFlags.Public);
        fiStepHeight = null;

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

        lastAppliedStepHeight = float.NaN;
        lastAppliedElevate = double.NaN;

        ApplyStepHeightToPlayer();
        ApplyElevateFactorToPlayer(16f);
        SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Ok(SuaChat.L("config-reloaded"))}");
        return true;
    }
    private bool IsNearBlacklistedBlock(IClientPlayer player)
    {
        BlockPos playerPos = player.Entity.Pos.AsBlockPos;
        IWorldAccessor world = player.Entity.World;

        // Server list is server-owned: only consult when enforcement is
        // active. With the Phase 3b hotfix-of-the-hotfix, IsEnforced is
        // simply the value of ServerEnforceSettings — single-player no
        // longer short-circuits to false. So in SP, an opted-in player
        // (flag = true) will see the server list consulted; in SP without
        // the flag, the server list is ignored even if populated. The
        // client's own list is independent and always consulted.
        var serverList = IsEnforced
            ? (StepUpOptions.Current?.BlockBlacklist ?? new List<string>())
            : new List<string>();
        var clientList = BlockBlacklistOptions.Current?.BlockCodes ?? new List<string>();

        if (serverList.Count == 0 && clientList.Count == 0) return false;

        // Always check the 8-cell ring at distance 1 from the player's
        // current cell. This catches the "approaching slowly" case.
        var positionsToCheck = new List<BlockPos>(14)
        {
            playerPos.EastCopy(),
            playerPos.WestCopy(),
            playerPos.NorthCopy(),
            playerPos.SouthCopy(),
            playerPos.EastCopy().NorthCopy(),
            playerPos.EastCopy().SouthCopy(),
            playerPos.WestCopy().NorthCopy(),
            playerPos.WestCopy().SouthCopy(),
        };

        // Velocity-aware lookahead. The proximity probe runs at 50 ms
        // (20 Hz). At high StepSpeed (>2), the elevate factor is high
        // enough that a step-up animation completes faster than a probe
        // tick — a player can transition from "outside the distance-1
        // ring" to "stepping onto a blacklisted block" within a single
        // probe window and the static ring never observes them adjacent.
        // Projecting the player's motion onto the cardinal axes and
        // probing 1–2 extra cells in each dominant direction closes
        // that gap with negligible cost on the slow path.
        //
        // Thresholds tuned to be well below sprint speed (~0.15-0.20)
        // so the lookahead doesn't fire on idle drift, and to add a
        // second forward cell only when the player is sprinting/falling
        // toward the blacklisted block.
        Vec3d motion = player.Entity.Pos.Motion;
        double absX = Math.Abs(motion.X);
        double absZ = Math.Abs(motion.Z);

        if (absX > 0.05)
        {
            int dx = motion.X > 0 ? 1 : -1;
            positionsToCheck.Add(playerPos.AddCopy(2 * dx, 0, 0));
            if (absX > 0.15) positionsToCheck.Add(playerPos.AddCopy(3 * dx, 0, 0));
        }
        if (absZ > 0.05)
        {
            int dz = motion.Z > 0 ? 1 : -1;
            positionsToCheck.Add(playerPos.AddCopy(0, 0, 2 * dz));
            if (absZ > 0.15) positionsToCheck.Add(playerPos.AddCopy(0, 0, 3 * dz));
        }

        foreach (BlockPos pos in positionsToCheck)
        {
            var block = world.BlockAccessor.GetBlock(pos);
            var code = block?.Code.ToString();
            if (code == null) continue;

            if (BlacklistMatcher.MatchesAny(code, serverList, clientList))
                return true;
        }
        return false;
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

        try
        {
            Type type = physicsBehavior.GetType();
            fiStepHeight ??= type.GetField("StepHeight") ?? type.GetField("stepHeight");
            if (fiStepHeight != null)
            {
                fiStepHeight.SetValue(physicsBehavior, stepHeight);
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
        catch (Exception ex)
        {
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Err(SuaChat.L("error.stepheight-set-failed"))} {SuaChat.Muted(ex.Message)}");
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

        var fld = fiElevateFactor
                  ?? physicsBehavior.GetType().GetField("elevateFactor", BindingFlags.Instance | BindingFlags.NonPublic)
                  ?? physicsBehavior.GetType().GetField("ElevateFactor", BindingFlags.Instance | BindingFlags.Public);

        if (fld != null)
        {
            try
            {
                fld.SetValue(physicsBehavior, desiredElevate);
                lastAppliedElevate = desiredElevate;
            }
            catch (Exception ex)
            {
                if (!elevateWarnedOnce)
                {
                    ModLog.Warning(capi, "Failed to set elevateFactor via reflection: {0}", ex.Message);
                    elevateWarnedOnce = true;
                }
            }
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

    private static bool IsSolidBlock(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        return block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0;
    }

    private bool HasLandingSupport(IWorldAccessor world, BlockPos basePos, double yawRad, int dist, float requestedStep)
    {
        // Below ~1 block of rise, the foot is still inside the player's
        // current cell during the step animation — no need to verify a
        // landing surface.
        if (requestedStep < 0.95f) return true;

        var (sx, sz) = CeilingProbeMath.ForwardOffset(yawRad, dist);
        int fwdX = basePos.X + sx;
        int fwdZ = basePos.Z + sz;

        int maxRise = CeilingProbeMath.MaxRiseClamp(requestedStep, hardCap: 2);
        if (maxRise <= 0) return true;

        int yTopSupport = basePos.Y + maxRise - 1;
        for (int y = yTopSupport; y >= basePos.Y; y--)
        {
            // Per-iteration BlockPos allocation matches pre-Phase-5
            // behavior. Phase 6 swaps this to a scratch buffer.
            var pos = new BlockPos(fwdX, y, fwdZ);
            var b = world.BlockAccessor.GetBlock(pos);
            if (b?.CollisionBoxes != null && b.CollisionBoxes.Length > 0)
                return true;
        }
        return false;
    }

    private float DistanceToCeilingAt(IWorldAccessor world, BlockPos origin, float maxCheck, int startDy)
    {
        if (startDy < 1) startDy = 1;
        int steps = (int)Math.Ceiling(maxCheck) + 1;
        float pad = StepUpOptions.Current.CeilingHeadroomPad;

        for (int dy = startDy; dy <= steps; dy++)
        {
            scratchPos.Set(origin.X, origin.Y + dy, origin.Z);
            if (IsSolidBlock(world, scratchPos))
                return Math.Max(0f, dy - pad);
        }
        return maxCheck;
    }

    private float DistanceToCeiling(IClientPlayer player, float requestedStep)
    {
        var world = player.Entity.World;
        var basePos = player.Entity.Pos.AsBlockPos;
        var cfg = StepUpOptions.Current;

        float hereClear = DistanceToCeilingAt(world, basePos, requestedStep, startDy: 1);

        if (!cfg.ForwardProbeCeiling || cfg.ForwardProbeDistance <= 0)
            return hereClear;

        double yaw = player.Entity.Pos.Yaw;
        bool supportedAhead = HasLandingSupport(world, basePos, yaw, cfg.ForwardProbeDistance, requestedStep);
        float tinySafe = Math.Max(0.25f, cfg.DefaultHeight);
        if (cfg.RequireForwardSupport && !supportedAhead) return Math.Min(hereClear, tinySafe);

        float entHeight = player.Entity.CollisionBox.Y2 - player.Entity.CollisionBox.Y1;
        var (yFrom, yTopInclusive) = CeilingProbeMath.LandingClearanceRange(
            basePos.Y, requestedStep, entHeight, cfg.CeilingHeadroomPad);
        if (yTopInclusive < yFrom) return hereClear;

        var columns = BuildForwardColumns(basePos, yaw, cfg.ForwardProbeDistance, cfg.ForwardProbeSpan);
        bool blockedAll = true;
        foreach (var col in columns)
        {
            if (!ColumnHasSolid(world, col, yFrom, yTopInclusive + 1))
            {
                blockedAll = false;
                break;
            }
        }
        return blockedAll ? Math.Min(hereClear, tinySafe) : hereClear;
    }

    private static bool ColumnHasSolid(IWorldAccessor world, BlockPos posXZ, int yFrom, int yToExclusive)
    {
        var bpos = new BlockPos(posXZ.X, 0, posXZ.Z);
        for (int y = yFrom; y < yToExclusive; y++)
        {
            bpos.Y = y;
            var block = world.BlockAccessor.GetBlock(bpos);
            if (block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Composes absolute world positions from the offset list produced by
    /// <see cref="CeilingProbeMath.ForwardSpanOffsets"/>. The math (forward
    /// direction, perpendicular axis, span fan-out) lives in the Domain
    /// layer; this method is the BlockPos-composition adapter.
    /// </summary>
    private static BlockPos[] BuildForwardColumns(BlockPos basePos, double yawRad, int dist, int span)
    {
        var offsets = CeilingProbeMath.ForwardSpanOffsets(yawRad, dist, span);
        var cols = new BlockPos[offsets.Count];
        int i = 0;
        foreach (var (dx, dz) in offsets)
        {
            cols[i++] = new BlockPos(basePos.X + dx, basePos.Y, basePos.Z + dz);
        }
        return cols;
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