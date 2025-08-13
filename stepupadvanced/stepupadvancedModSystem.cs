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

namespace stepupadvanced;

static class SuaChat
{
    // palette (bootstrap-ish)
    public const string CAccent = "#5bc0de"; // teal
    public const string CGood = "#5cb85c"; // green
    public const string CWarn = "#f0ad4e"; // orange
    public const string CBad = "#d9534f"; // red
    public const string CValue = "#ffd54f"; // amber
    public const string CMuted = "#a0a4a8"; // gray
    public const string CList = "#a12eff"; // purple

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

    public static void Client(ICoreClientAPI capi, string msg) => capi?.ShowChatMessage(msg);
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

        private void SafeSaveConfig(ICoreAPI api)
        {
            if ((DateTime.Now - lastSaveTime).TotalMilliseconds < 500) return;
            lastSaveTime = DateTime.Now;

            if (api.ModLoader.GetModSystem<StepUpAdvancedModSystem>() is StepUpAdvancedModSystem modSystem)
            {
                modSystem.SuppressWatcher(true);
            }

            string configFile = "StepUpAdvancedConfig.json";
            api.StoreModConfig(StepUpAdvancedConfig.Current, configFile);
            api.World.Logger.Event("Saved 'StepUp Advanced' configuration file.");

            if (api.ModLoader.GetModSystem<StepUpAdvancedModSystem>() is StepUpAdvancedModSystem ms)
            {
                ms.SuppressWatcher(false);
            }
        }

        private bool stepUpEnabled = true;

        private FileSystemWatcher configWatcher;

        private string configPath => Path.Combine(sapi?.GetOrCreateDataPath("ModConfig") ?? "", "StepUpAdvancedConfig.json");

        private bool suppressWatcher = false;

        private bool IsEnforced =>
            (sapi != null && StepUpAdvancedConfig.Current?.ServerEnforceSettings == true)
            || (capi != null && !capi.IsSinglePlayer && StepUpAdvancedConfig.Current?.ServerEnforceSettings == true);

    private static ICoreClientAPI capi;
    private static ICoreServerAPI sapi;

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
        if (IsEnforced) height = Math.Min(height, StepUpAdvancedConfig.Current.ServerMaxStepHeight);
        return height;
    }
    private float ClampSpeedClient(float speed)
    {
        speed = Math.Max(MinElevateFactor, speed);
        if (IsEnforced) speed = Math.Min(speed, StepUpAdvancedConfig.Current.ServerMaxStepSpeed);
        return speed;
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        StepUpAdvancedConfig.Load(api);
        Harmony.DEBUG = false;
        if (api.Side == EnumAppSide.Client && StepUpAdvancedConfig.Current.EnableHarmonyTweaks)
        {
            try
            {
                Harmony harmony = new Harmony("stepupadvanced.mod");
                PatchClassProcessor processor = new PatchClassProcessor(harmony, typeof(EntityBehaviorPlayerPhysicsPatch));
                processor.Patch();
                api.World.Logger.Event("[StepUp Advanced] Harmony patches applied successfully.");
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
            StepUpAdvancedConfig.Load(api);
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

        StepUpAdvancedConfig.Load(api);
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
            SafeSaveConfig(capi);
            capi.World.Logger.Event("[StepUp Advanced] Normalized runtime StepHeight/StepSpeed (client floors; server caps only if enforced).");
        }

        var basePhys = typeof(EntityBehaviorControlledPhysics);
        fiElevateFactor = basePhys.GetField("elevateFactor", BindingFlags.Instance | BindingFlags.NonPublic)
                       ?? basePhys.GetField("ElevateFactor", BindingFlags.Instance | BindingFlags.Public);
        fiStepHeight = null;

        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? true;

        RegisterHotkeys();
        capi.Event.RegisterGameTickListener((dt) => { ApplyStepHeightToPlayer(); }, 0);
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
                StepUpAdvancedConfig.UpdateConfig(config);

                if (!isRemoteMultiplayerClient)
                {
                    StepUpAdvancedConfig.Current.ServerEnforceSettings = false;
                }

                SafeSaveConfig(capi);

                StepUpAdvancedConfig.Current.AllowClientChangeStepHeight = config.AllowClientChangeStepHeight;
                StepUpAdvancedConfig.Current.AllowClientChangeStepSpeed = config.AllowClientChangeStepSpeed;

            if (!IsEnforced && hasShownServerEnforcedNotice)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Ok(SuaChat.L("server-enforcement-off"))} " + $"{SuaChat.Muted(SuaChat.L("local-settings-enabled"))}");
                //capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings deactivated. Your local settings are now enabled.");
                hasShownServerEnforcedNotice = false;
            }

            if (IsEnforced && !hasShownServerEnforcedNotice)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforcement-on"))} " + $"{SuaChat.Muted(SuaChat.L("local-settings-disabled"))}");
                //capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Your local settings are disabled.");
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
            //capi.SendChatMessage("[StepUp Advanced] You must be a player to use this command.");
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
        StepUpAdvancedConfig.Save(sapi);
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
        StepUpAdvancedConfig.Save(sapi);
        return SuaCmd.Err(SuaChat.L("cmd.removed-server-blacklist"), $"{SuaChat.Arrow}{SuaChat.Val(blockCode)}");
    }
    private TextCommandResult ReloadServerConfig(TextCommandCallingArgs arg)
    {
        if (sapi == null)
            return SuaCmd.Err(SuaChat.L("cmd.failed"), SuaChat.Muted(SuaChat.L("cmd.must-be-player")));

        StepUpAdvancedConfig.Load(sapi);

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
                //capi.ShowChatMessage("[StepUp Advanced] Height controls are disabled (Speed-only mode).");
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
                //capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot change step height.");
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
                //capi.ShowChatMessage($"Step height cannot exceed {StepUpAdvancedConfig.Current.ServerMaxStepHeight:0.0} blocks.");
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
            SafeSaveConfig(capi);
            ApplyStepHeightToPlayer();
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("height"))} {SuaChat.Arrow} {SuaChat.Val($"{currentStepHeight:0.0} blocks")}");
            //capi.ShowChatMessage($"Step height increased to {currentStepHeight:0.0} blocks.");
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
                //capi.ShowChatMessage("[StepUp Advanced] Height controls are disabled (Speed-only mode).");
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
                //capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot change step height.");
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
                //capi.ShowChatMessage($"Step height cannot be less than {StepUpAdvancedConfig.Current.ServerMinStepHeight:0.0} blocks.");
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
                //capi.ShowChatMessage($"Step height cannot be less than {MinStepHeight:0.0} blocks (client minimum).");
                hasShownMinMessage = true;
            }
            StepUpAdvancedConfig.Current.StepHeight = currentStepHeight;
            SafeSaveConfig(capi);
            ApplyStepHeightToPlayer();
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("height"))} {SuaChat.Arrow} {SuaChat.Val($"{currentStepHeight:0.0} blocks")}");
            //capi.ShowChatMessage($"Step height decreased to {currentStepHeight:0.0} blocks.");
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
                //capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot change step speed.");
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
                //capi.ShowChatMessage($"Step speed cannot exceed {StepUpAdvancedConfig.Current.ServerMaxStepSpeed:0.0} blocks.");
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
            SafeSaveConfig(capi);
            ApplyElevateFactorToPlayer(16f);
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("speed"))} {SuaChat.Arrow} {SuaChat.Val($"{currentElevateFactor:0.0}")}");
            //capi.ShowChatMessage($"Step speed increased to {currentElevateFactor:0.0}.");
        }
        return true;
    }
    private bool OnDecreaseElevateFactor(KeyCombination comb)
    {
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepSpeed)
        {
            if (!hasShownMinEMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("enforced"))} – {SuaChat.Muted(SuaChat.L("speed-change-blocked"))}");
                //capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot change step speed.");
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
                //capi.ShowChatMessage($"Step speed cannot be less than {StepUpAdvancedConfig.Current.ServerMinStepSpeed:0.0} blocks.");
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
                //capi.ShowChatMessage($"Step speed cannot be less than {MinElevateFactor:0.0} (client minimum).");
                hasShownMinEMessage = true;
            }
            StepUpAdvancedConfig.Current.StepSpeed = currentElevateFactor;
            SafeSaveConfig(capi);
            ApplyElevateFactorToPlayer(16f);
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Bold(SuaChat.L("speed"))} {SuaChat.Arrow} {SuaChat.Val($"{currentElevateFactor:0.0}")}");
            //capi.ShowChatMessage($"Step speed decreased to {currentElevateFactor:0.0}.");
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
        SafeSaveConfig(capi);
        ApplyStepHeightToPlayer();
        ApplyElevateFactorToPlayer(16f);
        SuaChat.Client(capi, stepUpEnabled
            ? $"{SuaChat.Tag} {SuaChat.Ok(SuaChat.L("stepup-enabled"))}"
            : $"{SuaChat.Tag} {SuaChat.Err(SuaChat.L("stepup-disabled"))}");
        //string message = (stepUpEnabled ? "StepUp enabled." : "StepUp disabled.");
        //capi.ShowChatMessage(message);
        return true;
    }
    private bool OnReloadConfig(KeyCombination comb)
    {
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientConfigReload)
        {
            if (!hasShownMaxMessage)
            {
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Warn(SuaChat.L("server-enforced"))} – {SuaChat.Muted(SuaChat.L("reload-blocked"))}");
                //capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot reload config.");
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
        StepUpAdvancedConfig.Load(capi);
        currentStepHeight = StepUpAdvancedConfig.Current?.StepHeight ?? currentStepHeight;
        currentElevateFactor = StepUpAdvancedConfig.Current?.StepSpeed ?? currentElevateFactor;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? stepUpEnabled;

        lastAppliedStepHeight = float.NaN;
        lastAppliedElevate = double.NaN;

        ApplyStepHeightToPlayer();
        ApplyElevateFactorToPlayer(16f);
        SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Ok(SuaChat.L("config-reloaded"))}");
        //capi.ShowChatMessage("Configuration reloaded.");
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
            Block block = world.BlockAccessor.GetBlock(pos);
            string code = block.Code.ToString();

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
            if (clearance <= 0.75f) stepHeight = System.Math.Min(stepHeight, StepUpAdvancedConfig.Current.DefaultHeight);
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
                SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Err(SuaChat.L("error.stepheight-field-missing"))}");
                //capi.ShowChatMessage("[StepUp Advanced] StepHeight field not found.");
            }
        }
        catch (Exception ex)
        {
            SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Err(SuaChat.L("error.stepheight-set-failed"))} {SuaChat.Muted(ex.Message)}");
            //capi.ShowChatMessage("[StepUp Advanced] Failed to set step height. Error: " + ex.Message);
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
            catch { }
        }
        else if (!elevateWarnedOnce)
        {
            //SuaChat.Client(capi, $"{SuaChat.Tag} {SuaChat.Err(SuaChat.L("warn.elevatefactor-field-missing"))}");
            capi.World.Logger.Warning("[StepUp Advanced] elevateFactor field not found; StepSpeed is controlled by Harmony only.");
            elevateWarnedOnce = true;
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

            configWatcher = new FileSystemWatcher(directory, filename);
            configWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;

            configWatcher.Changed += (sender, args) =>
            {
                if (suppressWatcher) return;

                Thread.Sleep(100);
                sapi.Event.EnqueueMainThreadTask(() =>
                {
                    sapi.World.Logger.Event("[StepUp Advanced] Detected config file change. Reloading...");
                    StepUpAdvancedConfig.Load(sapi);

                    foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
                    {
                        sapi.Network.GetChannel("stepupadvanced").SendPacket(StepUpAdvancedConfig.Current, player);
                    }

                    if (StepUpAdvancedConfig.Current.ServerEnforceSettings)
                    {
                        sapi.World.Logger.Event("[StepUp Advanced] Server-enforced config pushed to all clients.");
                    }
                    else
                    {
                        sapi.World.Logger.Event("[StepUp Advanced] Config pushed (server enforcement disabled, allows client-side config again).");
                    }
                }, "ReloadStepUpAdvancedConfig");
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
        int sx = (int)System.Math.Round(System.Math.Sin(yawRad)) * dist;
        int sz = (int)System.Math.Round(System.Math.Cos(yawRad)) * dist;
        return basePos.AddCopy(sx, 0, sz);
    }

    private bool HasForwardSupport(IWorldAccessor world, BlockPos basePos, double yawRad, int dist)
    {
        var fwd = ForwardBlock(basePos, yawRad, dist);
        var ground = world.BlockAccessor.GetBlock(fwd);
        return ground?.CollisionBoxes != null && ground.CollisionBoxes.Length > 0;
    }

    private float DistanceToCeilingAt(IWorldAccessor world, BlockPos origin, float maxCheck, int startDy)
    {
        if (startDy < 1) startDy = 1;
        int steps = (int)System.Math.Ceiling(maxCheck) + 1;

        for (int dy = startDy; dy <= steps; dy++)
        {
            scratchPos.Set(origin.X, origin.Y + dy, origin.Z);
            if (IsSolidBlock(world, scratchPos))
            {
                return System.Math.Max(0f, dy - 0.25f);
            }
        }
        return maxCheck;
    }

    private static BlockPos ForwardBlock(BlockPos basePos, double yawRad, int dist, BlockPos into)
    {
        int sx = (int)System.Math.Round(System.Math.Sin(yawRad)) * dist;
        int sz = (int)System.Math.Round(System.Math.Cos(yawRad)) * dist;
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
        var fwdCenter = ForwardBlock(basePos, yaw, cfg.ForwardProbeDistance, new BlockPos(0));
        bool supportedAhead = HasForwardSupport(world, basePos, yaw, cfg.ForwardProbeDistance);
        float tinySafe = Math.Max(0.25f, cfg.DefaultHeight);
        if (!supportedAhead)
            return Math.Min(hereClear, tinySafe);

        float entHeight = player.Entity.CollisionBox.Y2 - player.Entity.CollisionBox.Y1;
        int yFeetLanding = basePos.Y + (int)Math.Floor(requestedStep);
        int yFrom = yFeetLanding + 1;
        int yTo = yFrom + (int)Math.Ceiling(entHeight + 0.05f);
        var cols = BuildForwardColumns(basePos, yaw, cfg.ForwardProbeDistance);
        bool blockedAll = true;
        foreach (var col in cols)
        {
            if (!ColumnHasSolid(world, col, yFrom, yTo))
            {
                blockedAll = false;
                break;
            }
        }
        return blockedAll ? Math.Min(hereClear, tinySafe) : hereClear;
    }

    private static bool ColumnHasSolid(IWorldAccessor world, BlockPos posXZ, int yFrom, int yTo)
    {
        var bpos = new BlockPos(posXZ.X, 0, posXZ.Z);
        for (int y = yFrom; y < yTo; y++)
        {
            bpos.Y = y;
            var block = world.BlockAccessor.GetBlock(bpos);
            if (block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0)
                return true;
        }
        return false;
    }

    private static BlockPos[] BuildForwardColumns(BlockPos basePos, double yawRad, int dist)
    {
        int fx = (int)Math.Round(Math.Sin(yawRad));
        int fz = (int)Math.Round(Math.Cos(yawRad));

        int px = -fz;
        int pz = fx;

        var center = ForwardBlock(basePos, yawRad, dist, new BlockPos());
        var left = new BlockPos(center.X + px, center.Y, center.Z + pz);
        var right = new BlockPos(center.X - px, center.Y, center.Z - pz);
        return new[] { center, left, right };
    }

    public void SuppressWatcher(bool suppress)
    {
        suppressWatcher = suppress;
    }

        public override void Dispose()
        {
            configWatcher?.Dispose();
            base.Dispose();
        }
    }
}