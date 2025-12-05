using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace stepupadvanced;

static class SuaChat
{
    public const string CAccent = "#5bc0de";
    public const string CGood = "#5cb85c";
    public const string CWarn = "#f0ad4e";
    public const string CBad = "#d9534f";
    public const string CValue = "#ffd54f";
    public const string CMuted = "#a0a4a8";
    public const string CList = "#a12eff";

    public static string Font(string text, string hex) => $"<font color=\"{hex}\">{text}</font>";
    public static string Bold(string text) => $"<strong>{text}</strong>";
    public static string L(string key, params object[] args)
    => Lang.Get($"sua:{key}", args);
    public static string Tag => Bold(Font($"[{L("modname")}]", CAccent));
    public static string Ok(string t) => Bold(Font(t, CGood));
    public static string Warn(string t) => Bold(Font(t, CWarn));
    public static string Err(string t) => Bold(Font(t, CBad));
    public static string Val(string t) => Bold(Font(t, CValue));
    public static string Muted(string t) => Font(t, CMuted);
    public static string Arrow => Muted("» ");

    public static void Client(ICoreClientAPI capi, string msg) 
    { 
        if (StepUpAdvancedConfig.Current?.QuietMode == true) return;
        capi?.ShowChatMessage(msg);
    }

    public static void Server(IServerPlayer p, string msg) =>
        p?.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
}

static class SuaCmd
{
    public static TextCommandResult Ok(string headline, string detail = null)
        => TextCommandResult.Success($"{SuaChat.Tag} {SuaChat.Ok(headline)}{(detail == null ? "" : " " + detail)}");

    public static TextCommandResult Warn(string headline, string detail = null)
        => TextCommandResult.Success($"{SuaChat.Tag} {SuaChat.Warn(headline)}{(detail == null ? "" : " " + detail)}");

    public static TextCommandResult Err(string headline, string detail = null)
        => TextCommandResult.Error($"{SuaChat.Tag} {SuaChat.Err(headline)}{(detail == null ? "" : " " + detail)}");

    public static TextCommandResult Info(string headline, string detail = null)
        => TextCommandResult.Success($"{SuaChat.Tag} {SuaChat.Muted(headline)}{(detail == null ? "" : " " + detail)}");

    public static TextCommandResult List(string title, IEnumerable<string> items)
    {
        var body = string.Join("\n", items.Select(i => $"• {SuaChat.Val(i)}"));
        return TextCommandResult.Success($"{SuaChat.Tag} {SuaChat.Bold(SuaChat.Font(title, SuaChat.CList))}\n{body}");
    }
}


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
        StepUpAdvancedConfig.Save(api);
    }

    private bool stepUpEnabled = true;

    private FileSystemWatcher configWatcher;
    private System.Timers.Timer watcherDebounce;
    private string configPath => Path.Combine(sapi?.GetOrCreateDataPath("ModConfig") ?? "", "StepUpAdvancedConfig.json");

    private bool suppressWatcher = false;

    private bool IsEnforced =>
        (sapi != null && StepUpAdvancedConfig.Current?.ServerEnforceSettings == true)
        || (capi != null && !capi.IsSinglePlayer && StepUpAdvancedConfig.Current?.ServerEnforceSettings == true);

    private static ICoreClientAPI capi;
    private static ICoreServerAPI sapi;

    private static readonly object ConfigQueueLock = new();
    private static volatile bool saveQueued;

    public const float MinStepHeight = 0.6f;
    public const float MinStepHeightIncrement = 0.1f;
    public const float AbsoluteMaxStepHeight = 2f;
    public const float DefaultStepHeight = 0.6f;

    public const float MinElevateFactor = 0.7f;
    public const float MinElevateFactorIncrement = 0.1f;
    public const float AbsoluteMaxElevateFactor = 2f;
    public const float DefaultElevateFactor = 0.7f;

    private float currentElevateFactor;
    private float currentStepHeight;

    private bool elevateWarnedOnce = false;
    private bool stepHeightWarnedOnce = false;
    private bool warnedPlayerNullOnce = false;
    private bool warnedPhysNullOnce = false;

    private bool hasShownMaxMessage;
    private bool hasShownMinMessage;
    private bool hasShownMaxEMessage;
    private bool hasShownMinEMessage;
    private bool hasShownServerEnforcedNotice;
    private bool hasShownHeightDisabled;

    private bool toggleStepUpKeyHeld;
    private bool reloadConfigKeyHeld;

    private FieldInfo fiStepHeight;
    private FieldInfo fiElevateFactor;
    private float lastAppliedStepHeight = float.NaN;
    private double lastAppliedElevate = double.NaN;
    private readonly BlockPos scratchPos = new BlockPos(0);

    private float ClampHeightClient(float height)
    {
        height = Math.Max(MinStepHeight, height);
        if (IsEnforced) height = GameMath.Clamp(height, StepUpAdvancedConfig.Current.ServerMinStepHeight, StepUpAdvancedConfig.Current.ServerMaxStepHeight);
        return height;
    }
    private float ClampSpeedClient(float speed)
    {
        speed = Math.Max(MinElevateFactor, speed);
        if (IsEnforced) speed = GameMath.Clamp(speed, StepUpAdvancedConfig.Current.ServerMinStepSpeed, StepUpAdvancedConfig.Current.ServerMaxStepSpeed);
        return speed;
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        StepUpAdvancedConfig.LoadOrUpgrade(api);
        Harmony.DEBUG = false;
        if (api.Side == EnumAppSide.Client && StepUpAdvancedConfig.Current.EnableHarmonyTweaks)
        {
            try
            {
                Harmony harmony = new Harmony("stepupadvanced.mod");
                PatchClassProcessor processor = new PatchClassProcessor(harmony, typeof(EntityBehaviorPlayerPhysicsPatch));
                processor.Patch();
                api.World.Logger.VerboseDebug("[StepUp Advanced] Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                api.World.Logger.Warning("[StepUp Advanced] Harmony patches disabled (safe mode): {0}", ex.Message);
            }
        }
        api.World.Logger.Event("Initialized 'StepUp Advanced' mod");
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        sapi = api;
        StepUpAdvancedConfig.LoadOrUpgrade(api);
        var channel = sapi.Network.RegisterChannel("stepupadvanced")
            .RegisterMessageType<StepUpAdvancedConfig>();

        if (channel == null)
        {
            sapi.World.Logger.Error("[StepUp Advanced] Failed to register network channel!");
        }
        api.Event.PlayerNowPlaying += OnPlayerJoin;

        SetupConfigWatcher();
        RegisterServerCommands();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;
        api.Network.RegisterChannel("stepupadvanced")
                .RegisterMessageType<StepUpAdvancedConfig>()
                .SetMessageHandler<StepUpAdvancedConfig>(OnReceiveServerConfig);

        StepUpAdvancedConfig.LoadOrUpgrade(api);
        BlockBlacklistConfig.Load(api);

        currentStepHeight = StepUpAdvancedConfig.Current?.StepHeight ?? DefaultStepHeight;
        currentElevateFactor = StepUpAdvancedConfig.Current?.StepSpeed ?? DefaultElevateFactor;

        bool changed = false;
        float newHeight = ClampHeightClient(currentStepHeight);
        float newSpeed = ClampSpeedClient(currentElevateFactor);
        if (newHeight != currentStepHeight) { currentStepHeight = newHeight; changed = true; }
        if (newSpeed != currentElevateFactor) { currentElevateFactor = newSpeed; changed = true; }
        if (changed)
        {
            StepUpAdvancedConfig.Current.StepHeight = currentStepHeight;
            StepUpAdvancedConfig.Current.StepSpeed = currentElevateFactor;
            QueueConfigSave(capi);
            capi.World.Logger.VerboseDebug("[StepUp Advanced] Normalized runtime StepHeight/StepSpeed (client floors; server caps only if enforced).");
        }

        var basePhys = typeof(EntityBehaviorControlledPhysics);
        fiElevateFactor = basePhys.GetField("elevateFactor", BindingFlags.Instance | BindingFlags.NonPublic)
                       ?? basePhys.GetField("ElevateFactor", BindingFlags.Instance | BindingFlags.Public);
        fiStepHeight = null;

        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? true;

        RegisterHotkeys();
        capi.Event.RegisterGameTickListener((dt) => { ApplyStepHeightToPlayer(); }, 50);
        ApplyElevateFactorToPlayer(16f);
        RegisterCommands();
    }

    private void OnPlayerJoin(IServerPlayer player)
    {
        if (StepUpAdvancedConfig.Current == null || sapi == null)
        {
            sapi?.World.Logger.Error("[StepUp Advanced] Cannot process OnPlayerJoin: config or server API is null.");
            return;
        }
        if (IsEnforced)
        {
            sapi.Network.GetChannel("stepupadvanced").SendPacket(StepUpAdvancedConfig.Current, player);
        }
    }

    private void OnReceiveServerConfig(StepUpAdvancedConfig config)
    {
        if (capi == null) return;

        bool isRemoteMultiplayerClient = !capi.IsSinglePlayer && sapi == null;

        if (!isRemoteMultiplayerClient)
        {
            config.ServerEnforceSettings = false;
        }

        capi.Event.EnqueueMainThreadTask(() =>
        {
            bool showNotice = config.ShowServerEnforcedNotice;

            StepUpAdvancedConfig.UpdateConfig(config);

            if (!isRemoteMultiplayerClient)
            {
                StepUpAdvancedConfig.Current.ServerEnforceSettings = false;
            }

            StepUpAdvancedConfig.Current.AllowClientChangeStepHeight = config.AllowClientChangeStepHeight;
            StepUpAdvancedConfig.Current.AllowClientChangeStepSpeed = config.AllowClientChangeStepSpeed;

            if (!IsEnforced && hasShownServerEnforcedNotice)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Ok(SuaChat.L("server-enforcement-off"))} " + $"{SuaChat.Muted(SuaChat.L("local-settings-enabled"))}");
                hasShownServerEnforcedNotice = false;
            }

            if (IsEnforced && !hasShownServerEnforcedNotice)
            {
                if (showNotice)
                {
                    SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforcement-on"))} " + $"{SuaChat.Muted(SuaChat.L("local-settings-disabled"))}");
                }
                hasShownServerEnforcedNotice = true;
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

        if (BlockBlacklistConfig.Current.BlockCodes.Contains(blockCode))
            return SuaCmd.Warn(SuaChat.L("cmd.already-listed"), $"{SuaChat.Val(blockCode)} {SuaChat.Muted(SuaChat.L("cmd.is-on-blacklist"))}");

        BlockBlacklistConfig.Current.BlockCodes.Add(blockCode);
        BlockBlacklistConfig.Save(capi);
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

        if (!BlockBlacklistConfig.Current.BlockCodes.Contains(blockCode))
            return SuaCmd.Warn(SuaChat.L("cmd.not-listed-client"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");

        BlockBlacklistConfig.Current.BlockCodes.Remove(blockCode);
        BlockBlacklistConfig.Save(capi);
        return SuaCmd.Err(SuaChat.L("cmd.removed-client-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");
    }
    private TextCommandResult ListBlacklist(TextCommandCallingArgs arg)
    {
        var serverList = StepUpAdvancedConfig.Current?.BlockBlacklist ?? new List<string>();
        var clientList = BlockBlacklistConfig.Current?.BlockCodes ?? new List<string>();

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

        if (StepUpAdvancedConfig.Current.BlockBlacklist.Contains(blockCode))
            return SuaCmd.Warn(SuaChat.L("cmd.already-listed-server"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");

        StepUpAdvancedConfig.Current.BlockBlacklist.Add(blockCode);
        suppressWatcher = true;
        StepUpAdvancedConfig.Save(sapi);
        sapi.Event.RegisterCallback(_ => suppressWatcher = false, 150);
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

        if (!StepUpAdvancedConfig.Current.BlockBlacklist.Contains(blockCode))
            return SuaCmd.Warn(SuaChat.L("cmd.not-on-server-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");

        StepUpAdvancedConfig.Current.BlockBlacklist.Remove(blockCode);
        suppressWatcher = true;
        StepUpAdvancedConfig.Save(sapi);
        sapi.Event.RegisterCallback(_ => suppressWatcher = false, 150);
        return SuaCmd.Err(SuaChat.L("cmd.removed-server-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");
    }
    private TextCommandResult ReloadServerConfig(TextCommandCallingArgs arg)
    {
        if (sapi == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.must-be-player")));

        suppressWatcher = true;
        StepUpAdvancedConfig.LoadOrUpgrade(sapi);
        sapi.Event.RegisterCallback(_ => suppressWatcher = false, 150);

        foreach (IServerPlayer p in sapi.World.AllOnlinePlayers)
            sapi.Network.GetChannel("stepupadvanced").SendPacket(StepUpAdvancedConfig.Current, p);

        if (StepUpAdvancedConfig.Current.ServerEnforceSettings)
            return SuaCmd.Ok(SuaChat.L("cmd.server-config-reloaded"),
                $"{SuaChat.Muted(SuaChat.L("cmd.pushed"))} {SuaChat.Warn(SuaChat.L("cmd.enforced"))} {SuaChat.Muted(SuaChat.L("cmd.config"))}");

        return SuaCmd.Ok(SuaChat.L("cmd.server-config-reloaded"), SuaChat.Muted(SuaChat.L("cmd.clients-may-use-local")));
    }
    private void RegisterHotkeys()
    {
        capi.Input.RegisterHotKey("increaseStepHeight", Lang.Get("key.increase-height"), GlKeys.PageUp, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("decreaseStepHeight", Lang.Get("key.decrease-height"), GlKeys.PageDown, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("increaseStepSpeed", Lang.Get("key.increase-speed"), GlKeys.Up, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("decreaseStepSpeed", Lang.Get("key.decrease-speed"), GlKeys.Down, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("toggleStepUp", Lang.Get("key.toggle"), GlKeys.Insert, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("reloadConfig", Lang.Get("key.reload"), GlKeys.Home, HotkeyType.GUIOrOtherControls);
        capi.Input.SetHotKeyHandler("increaseStepHeight", OnIncreaseStepHeight);
        capi.Input.SetHotKeyHandler("decreaseStepHeight", OnDecreaseStepHeight);
        capi.Input.SetHotKeyHandler("increaseStepSpeed", OnIncreaseElevateFactor);
        capi.Input.SetHotKeyHandler("decreaseStepSpeed", OnDecreaseElevateFactor);
        capi.Input.SetHotKeyHandler("toggleStepUp", OnToggleStepUp);
        capi.Input.SetHotKeyHandler("reloadConfig", OnReloadConfig);
        capi.Event.KeyUp += delegate (KeyEvent ke)
        {
            if (ke.KeyCode == capi.Input.HotKeys["toggleStepUp"].CurrentMapping.KeyCode)
            {
                toggleStepUpKeyHeld = false;
            }
            if (ke.KeyCode == capi.Input.HotKeys["reloadConfig"].CurrentMapping.KeyCode)
            {
                reloadConfigKeyHeld = false;
            }
        };
    }
    private bool OnIncreaseStepHeight(KeyCombination comb)
    {
        if (StepUpAdvancedConfig.Current?.SpeedOnlyMode == true)
        {
            if (!hasShownHeightDisabled)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("speed-only-mode"))} – {SuaChat.Muted(SuaChat.L("height-controls-disabled"))}");
                hasShownHeightDisabled = true;
            }
            return false;
        }
        hasShownHeightDisabled = false;

        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepHeight)
        {
            if (!hasShownMaxEMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("height-change-blocked"))}");
                hasShownMaxEMessage = true;
            }
            return false;
        }
        hasShownMaxEMessage = false;

        if (IsEnforced && Math.Abs(currentStepHeight - StepUpAdvancedConfig.Current.ServerMaxStepHeight) < 0.01f)
        {
            if (!hasShownMaxMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("max-height"))} {SuaChat.Arrow} {SuaChat.Val($"{StepUpAdvancedConfig.Current.ServerMaxStepHeight:0.0} blocks")}");
                hasShownMaxMessage = true;
            }
            return false;
        }
        hasShownMaxMessage = false;

        float previousStepHeight = currentStepHeight;
        currentStepHeight += Math.Max(StepUpAdvancedConfig.Current.StepHeightIncrement, 0.1f);

        currentStepHeight = ClampHeightClient(currentStepHeight);

        if (currentStepHeight > previousStepHeight)
        {
            StepUpAdvancedConfig.Current.StepHeight = currentStepHeight;
            QueueConfigSave(capi);
            ApplyStepHeightToPlayer();
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("height"))} {SuaChat.Arrow} {SuaChat.Val($"{currentStepHeight:0.0} blocks")}");
        }
        return true;
    }
    private bool OnDecreaseStepHeight(KeyCombination comb)
    {
        if (StepUpAdvancedConfig.Current?.SpeedOnlyMode == true)
        {
            if (!hasShownHeightDisabled)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("speed-only-mode"))} – {SuaChat.Muted(SuaChat.L("height-controls-disabled"))}");
                hasShownHeightDisabled = true;
            }
            return false;
        }
        hasShownHeightDisabled = false;

        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepHeight)
        {
            if (!hasShownMinEMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("height-change-blocked"))}");
                hasShownMinEMessage = true;
            }
            return false;
        }
        hasShownMinEMessage = false;

        if (IsEnforced && Math.Abs(currentStepHeight - StepUpAdvancedConfig.Current.ServerMinStepHeight) < 0.01f)
        {
            if (!hasShownMinMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("min-height"))} {SuaChat.Arrow} {SuaChat.Val($"{Math.Max(MinStepHeight, StepUpAdvancedConfig.Current.ServerMinStepHeight):0.0} blocks")}");
                hasShownMinMessage = true;
            }
            return false;
        }
        hasShownMinMessage = false;

        float previousStepHeight = currentStepHeight;
        currentStepHeight -= Math.Max(StepUpAdvancedConfig.Current.StepHeightIncrement, 0.1f);

        currentStepHeight = ClampHeightClient(currentStepHeight);

        if (currentStepHeight < previousStepHeight)
        {
            if (currentStepHeight <= MinStepHeight && !hasShownMinMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("min-height"))} {SuaChat.Arrow} {SuaChat.Val($"{Math.Max(MinStepHeight, StepUpAdvancedConfig.Current.ServerMinStepHeight):0.0} blocks")}");
                hasShownMinMessage = true;
            }
            StepUpAdvancedConfig.Current.StepHeight = currentStepHeight;
            QueueConfigSave(capi);
            ApplyStepHeightToPlayer();
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("height"))} {SuaChat.Arrow} {SuaChat.Val($"{currentStepHeight:0.0} blocks")}");
        }
        return true;
    }
    private bool OnIncreaseElevateFactor(KeyCombination comb)
    {
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepSpeed)
        {
            if (!hasShownMaxEMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("speed-change-blocked"))}");
                hasShownMaxEMessage = true;
            }
            return false;
        }
        hasShownMaxEMessage = false;

        if (IsEnforced && Math.Abs(currentElevateFactor - StepUpAdvancedConfig.Current.ServerMaxStepSpeed) < 0.01f)
        {
            if (!hasShownMaxMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("max-speed"))} {SuaChat.Arrow} {SuaChat.Val($"{StepUpAdvancedConfig.Current.ServerMaxStepSpeed:0.0}")}");
                hasShownMaxMessage = true;
            }
            return false;
        }
        hasShownMaxMessage = false;

        float previousElevateFactor = currentElevateFactor;
        currentElevateFactor += Math.Max(StepUpAdvancedConfig.Current.StepSpeedIncrement, 0.01f);
        currentElevateFactor = ClampSpeedClient(currentElevateFactor);

        if (currentElevateFactor > previousElevateFactor)
        {
            StepUpAdvancedConfig.Current.StepSpeed = currentElevateFactor;
            QueueConfigSave(capi);
            ApplyElevateFactorToPlayer(16f);
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("speed"))} {SuaChat.Arrow} {SuaChat.Val($"{currentElevateFactor:0.0}")}");
        }
        return true;
    }
    private bool OnDecreaseElevateFactor(KeyCombination comb)
    {
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepSpeed)
        {
            if (!hasShownMinEMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("speed-change-blocked"))}");
                hasShownMinEMessage = true;
            }
            return false;
        }
        hasShownMinEMessage = false;

        if (IsEnforced && Math.Abs(currentElevateFactor - StepUpAdvancedConfig.Current.ServerMinStepSpeed) < 0.01f)
        {
            if (!hasShownMinMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("min-speed"))} {SuaChat.Arrow} {SuaChat.Val($"{StepUpAdvancedConfig.Current.ServerMinStepSpeed:0.0}")}");
                hasShownMinMessage = true;
            }
            return false;
        }
        hasShownMinMessage = false;

        float previousElevateFactor = currentElevateFactor;
        currentElevateFactor -= Math.Max(StepUpAdvancedConfig.Current.StepSpeedIncrement, 0.01f);
        currentElevateFactor = ClampSpeedClient(currentElevateFactor);

        if (currentElevateFactor < previousElevateFactor)
        {
            if (currentElevateFactor <= MinElevateFactor && !hasShownMinEMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("min-speed"))} {SuaChat.Arrow} {SuaChat.Val($"{MinElevateFactor:0.0}")}");
                hasShownMinEMessage = true;
            }
            StepUpAdvancedConfig.Current.StepSpeed = currentElevateFactor;
            QueueConfigSave(capi);
            ApplyElevateFactorToPlayer(16f);
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("speed"))} {SuaChat.Arrow} {SuaChat.Val($"{currentElevateFactor:0.0}")}");
        }
        return true;
    }
    private bool OnToggleStepUp(KeyCombination comb)
    {
        if (toggleStepUpKeyHeld)
        {
            return false;
        }
        toggleStepUpKeyHeld = true;
        stepUpEnabled = !stepUpEnabled;
        StepUpAdvancedConfig.Current.StepUpEnabled = stepUpEnabled;
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
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientConfigReload)
        {
            if (!hasShownMaxMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("reload-blocked"))}");
                hasShownMaxMessage = true;
            }
            return false;
        }
        hasShownMaxMessage = false;
        if (reloadConfigKeyHeld)
        {
            return false;
        }
        reloadConfigKeyHeld = true;
        StepUpAdvancedConfig.LoadOrUpgrade(capi);
        currentStepHeight = StepUpAdvancedConfig.Current?.StepHeight ?? currentStepHeight;
        currentElevateFactor = StepUpAdvancedConfig.Current?.StepSpeed ?? currentElevateFactor;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? stepUpEnabled;

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

        var serverList = StepUpAdvancedConfig.Current?.BlockBlacklist ?? new List<string>();
        var clientList = BlockBlacklistConfig.Current?.BlockCodes ?? new List<string>();

        BlockPos[] positionsToCheck = new BlockPos[]
        {
        playerPos.EastCopy(),
        playerPos.WestCopy(),
        playerPos.NorthCopy(),
        playerPos.SouthCopy(),
        playerPos.EastCopy().NorthCopy(),
        playerPos.EastCopy().SouthCopy(),
        playerPos.WestCopy().NorthCopy(),
        playerPos.WestCopy().SouthCopy()
        };
        foreach (BlockPos pos in positionsToCheck)
        {
            var block = world.BlockAccessor.GetBlock(pos);
            var code = block?.Code.ToString();
            if (code == null) continue;

            bool serverMatch = serverList.Contains(code);
            bool clientMatch = clientList.Contains(code);

            if (serverMatch || clientMatch)
                return true;
        }
        return false;
    }
    private void ApplyStepHeightToPlayer()
    {
        if (StepUpAdvancedConfig.Current?.SpeedOnlyMode == true) return;
        IClientPlayer player = capi.World?.Player;
        if (player == null)
        {
            if (!warnedPlayerNullOnce)
            {
                capi.World.Logger.Warning("Player object is null. Cannot apply step height.");
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
                capi.World.Logger.Warning("Physics behavior is missing on player entity. Cannot apply step height.");
                warnedPhysNullOnce = true;
            }
            return;
        }
        warnedPhysNullOnce = false;

        bool nearBlacklistedBlock = IsNearBlacklistedBlock(player);

        float stepHeight =
            !stepUpEnabled ? 0.6f :
            nearBlacklistedBlock ? StepUpAdvancedConfig.Current.DefaultHeight :
            ClampHeightClient(currentStepHeight);

        if (StepUpAdvancedConfig.Current.CeilingGuardEnabled && stepHeight > 0f)
        {
            float clearance = DistanceToCeiling(player, stepHeight);
            if (clearance <= 0.75f) stepHeight = Math.Min(stepHeight, StepUpAdvancedConfig.Current.DefaultHeight);
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

        float logicalSpeed = IsEnforced ? StepUpAdvancedConfig.Current.StepSpeed : currentElevateFactor;
        logicalSpeed = ClampSpeedClient(logicalSpeed);

        double desiredElevate = (stepUpEnabled ? logicalSpeed : StepUpAdvancedConfig.Current.DefaultSpeed) * 0.05;

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
                    capi.World.Logger.Warning("[StepUp Advanced] Failed to set elevateFactor via reflection: {0}", ex.Message);
                    elevateWarnedOnce = true;
                }
            }
        }
    }

    private void SetupConfigWatcher()
    {
        string directory = Path.GetDirectoryName(configPath);
        string filename = Path.GetFileName(configPath);

        if (directory == null || filename == null)
        {
            sapi.World.Logger.Warning("[StepUp Advanced] Could not initialize config file watcher: invalid path.");
            return;
        }

        configWatcher = new FileSystemWatcher(directory, filename)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };

        watcherDebounce = new System.Timers.Timer(150) { AutoReset = false };
        watcherDebounce.Elapsed += (_, __) =>
        {
            sapi.Event.EnqueueMainThreadTask(() =>
            {
                if (suppressWatcher) return;

                sapi.World.Logger.Event("[StepUp Advanced] Detected config file change. Reloading...");
                StepUpAdvancedConfig.LoadOrUpgrade(sapi);

                foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
                    sapi.Network.GetChannel("stepupadvanced").SendPacket(StepUpAdvancedConfig.Current, player);

                if (StepUpAdvancedConfig.Current.ServerEnforceSettings)
                    sapi.World.Logger.VerboseDebug("[StepUp Advanced] Server-enforced config pushed to all clients.");
                else
                    sapi.World.Logger.VerboseDebug("[StepUp Advanced] Config pushed (server enforcement disabled, allows client-side config again).");

            }, "ReloadStepUpAdvancedConfig");
        };

        configWatcher.Changed += (sender, args) =>
        {
            if (suppressWatcher) return;
            watcherDebounce.Stop();
            watcherDebounce.Start();
        };

        configWatcher.EnableRaisingEvents = true;
        sapi.World.Logger.Event("[StepUp Advanced] File watcher initialized for config auto-reloading.");
    }

    private static bool IsSolidBlock(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        return block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0;
    }

    private static BlockPos ForwardBlock(BlockPos basePos, double yawRad, int dist)
    {
        int sx = (int)Math.Round(Math.Sin(yawRad)) * dist;
        int sz = (int)Math.Round(Math.Cos(yawRad)) * dist;
        return basePos.AddCopy(sx, 0, sz);
    }

    private bool HasLandingSupport(IWorldAccessor world, BlockPos basePos, double yawRad, int dist, float requestedStep)
    {
        if (requestedStep < 0.95f) return true;

        var fwdXZ = ForwardBlock(basePos, yawRad, dist, scratchPos);

        int maxRise = Math.Min((int)Math.Floor(requestedStep), 2);
        if (maxRise <= 0) return true;

        int yTopSupport = basePos.Y + maxRise - 1;
        for (int y = yTopSupport; y >= basePos.Y; y--)
        {
            var pos = new BlockPos(fwdXZ.X, y, fwdXZ.Z);
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
        float pad = StepUpAdvancedConfig.Current.CeilingHeadroomPad;

        for (int dy = startDy; dy <= steps; dy++)
        {
            scratchPos.Set(origin.X, origin.Y + dy, origin.Z);
            if (IsSolidBlock(world, scratchPos))
                return Math.Max(0f, dy - pad);
        }
        return maxCheck;
    }

    private static BlockPos ForwardBlock(BlockPos basePos, double yawRad, int dist, BlockPos into)
    {
        int sx = (int)Math.Round(Math.Sin(yawRad)) * dist;
        int sz = (int)Math.Round(Math.Cos(yawRad)) * dist;
        into.Set(basePos.X + sx, basePos.Y, basePos.Z + sz);
        return into;
    }

    private float DistanceToCeiling(IClientPlayer player, float requestedStep)
    {
        var world = player.Entity.World;
        var basePos = player.Entity.SidedPos.AsBlockPos;
        var cfg = StepUpAdvancedConfig.Current;

        float hereClear = DistanceToCeilingAt(world, basePos, requestedStep, startDy: 1);

        if (!cfg.ForwardProbeCeiling || cfg.ForwardProbeDistance <= 0)
            return hereClear;

        double yaw = player.Entity.SidedPos.Yaw;
        bool supportedAhead = HasLandingSupport(world, basePos, yaw, cfg.ForwardProbeDistance, requestedStep);
        float tinySafe = Math.Max(0.25f, cfg.DefaultHeight);
        if (cfg.RequireForwardSupport && !supportedAhead) return Math.Min(hereClear, tinySafe);
        float entHeight = player.Entity.CollisionBox.Y2 - player.Entity.CollisionBox.Y1;
        int yFeetLanding = basePos.Y + (int)Math.Floor(requestedStep);
        int yFrom = yFeetLanding + 1;
        int yTop = yFeetLanding + (int)Math.Floor(entHeight - cfg.CeilingHeadroomPad);
        if (yTop < yFrom) return hereClear;
        var columns = BuildForwardColumns(basePos, yaw, cfg.ForwardProbeDistance, cfg.ForwardProbeSpan);
        bool blockedAll = true;
        foreach (var col in columns)
        {
            if (!ColumnHasSolid(world, col, yFrom, yTop + 1))
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

    private static BlockPos[] BuildForwardColumns(BlockPos basePos, double yawRad, int dist, int span)
    {
        int fx = (int)Math.Round(Math.Sin(yawRad));
        int fz = (int)Math.Round(Math.Cos(yawRad));
        int px = -fz, pz = fx;

        var center = ForwardBlock(basePos, yawRad, dist, new BlockPos(0));

        if (span <= 0) return new[] { center };

        var cols = new List<BlockPos> { center, new BlockPos(center.X + px, center.Y, center.Z + pz),
                                             new BlockPos(center.X - px, center.Y, center.Z - pz) };

        if (span >= 2)
        {
            cols.Add(new BlockPos(center.X + 2 * px, center.Y, center.Z + 2 * pz));
            cols.Add(new BlockPos(center.X - 2 * px, center.Y, center.Z - 2 * pz));
        }

        return cols.ToArray();
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
        suppressWatcher = suppress;
    }

    public override void Dispose()
    {
        watcherDebounce?.Dispose();
        configWatcher?.Dispose();
        base.Dispose();
    }
}