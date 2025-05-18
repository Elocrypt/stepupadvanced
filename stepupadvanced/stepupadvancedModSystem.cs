using System;
using System.Collections.Generic;
using System.Reflection;
using ProtoBuf;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using System.IO;
using System.Threading;

namespace stepupadvanced;

public class StepUpAdvancedModSystem : ModSystem
{
    private bool stepUpEnabled = true;

    private FileSystemWatcher configWatcher;

    private string configPath => Path.Combine(sapi?.GetOrCreateDataPath("ModConfig") ?? "", "StepUpAdvancedConfig.json");

    private bool suppressWatcher = false;

    private bool IsEnforced =>
        (sapi != null && StepUpAdvancedConfig.Current?.ServerEnforceSettings == true)
        || (capi != null && !capi.IsSinglePlayer && StepUpAdvancedConfig.Current?.ServerEnforceSettings == true);

    private static ICoreClientAPI capi;

    private static ICoreServerAPI sapi;

    public const float MinStepHeight = 0.2f;

    public const float MinStepHeightIncrement = 0.1f;

    public const float AbsoluteMaxStepHeight = 2f;

    public const float DefaultStepHeight = 0.2f;

    public const float MinElevateFactor = 0.5f;

    public const float MinElevateFactorIncrement = 0.1f;

    public const float AbsoluteMaxElevateFactor = 2f;

    public const float DefaultElevateFactor = 0.7f;

    private float currentElevateFactor;

    private float currentStepHeight;

    private bool hasShownMaxMessage;

    private bool hasShownMinMessage;

    private bool hasShownMaxEMessage;

    private bool hasShownMinEMessage;

    private bool hasShownServerEnforcedNotice;

    private bool toggleStepUpKeyHeld;

    private bool reloadConfigKeyHeld;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        StepUpAdvancedConfig.Load(api);
        Harmony.DEBUG = false;
        Harmony harmony = new Harmony("stepupadvanced.mod");
        try
        {
            harmony.PatchAll();
            api.World.Logger.Event("[StepUp Advanced] Harmony patches applied successfully.");
        }
        catch (Exception ex)
        {
            api.World.Logger.Error("[StepUp Advanced] Failed to apply Harmony patches: " + ex.Message);
            throw;
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
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;
        api.Network.RegisterChannel("stepupadvanced")
                .RegisterMessageType<StepUpAdvancedConfig>()
                .SetMessageHandler<StepUpAdvancedConfig>(OnReceiveServerConfig);
        StepUpAdvancedConfig.Load(api);
        currentStepHeight = StepUpAdvancedConfig.Current?.StepHeight ?? 0.2f;
        currentElevateFactor = StepUpAdvancedConfig.Current?.StepSpeed ?? 0.7f;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? true;
        RegisterHotkeys();
        capi.Event.RegisterGameTickListener(delegate
        {
            ApplyStepHeightToPlayer();
        }, 0);
        capi.Event.RegisterGameTickListener(ApplyElevateFactorToPlayer, 16);
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

        try
        {
            // Determine if this is a real multiplayer client (not host, not SP)
            bool isRemoteMultiplayerClient = !capi.IsSinglePlayer && sapi == null;

            // Set enforcement BEFORE using the config
            if (isRemoteMultiplayerClient)
            {
                config.ServerEnforceSettings = true;
            }

            capi.Event.EnqueueMainThreadTask(() =>
            {
                // Apply the updated config to memory
                StepUpAdvancedConfig.UpdateConfig(config);

                // Reassert server enforcement after assignment
                if (isRemoteMultiplayerClient)
                {
                    StepUpAdvancedConfig.Current.ServerEnforceSettings = true;
                }

                StepUpAdvancedConfig.Save(capi);

                StepUpAdvancedConfig.Current.AllowClientChangeStepHeight = config.AllowClientChangeStepHeight;
                StepUpAdvancedConfig.Current.AllowClientChangeStepSpeed = config.AllowClientChangeStepSpeed;

                // Notify user only if something changed
                if (!IsEnforced && hasShownServerEnforcedNotice)
                {
                    capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings deactivated. Your local settings are now enabled.");
                    hasShownServerEnforcedNotice = false;
                }

                if (IsEnforced && !hasShownServerEnforcedNotice)
                {
                    capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Your local settings are disabled.");
                    hasShownServerEnforcedNotice = true;
                }

                ApplyStepHeightToPlayer();
                ApplyElevateFactorToPlayer(16f);

            }, "ApplyStepUpServerConfig");
        }
        catch (Exception ex)
        {
            capi.ShowChatMessage("[StepUp Advanced] ERROR applying server config. See client-main.txt.");
            capi.Logger.Error("[StepUp Advanced] Failed to apply server config: " + ex);
        }
    }
    private void RegisterCommands()
    {
        capi.ChatCommands.Create("sua").WithDescription("Manage the block blacklist").RequiresPrivilege("chat")
            .BeginSubCommand("add")
            .WithDescription("Adds the targeted block to the step-up blacklist.")
            .HandleWith(AddToBlacklist)
            .EndSubCommand()
            .BeginSubCommand("remove")
            .WithDescription("Removes the targeted block from the step-up blacklist.")
            .HandleWith(RemoveFromBlacklist)
            .EndSubCommand();
    }
    private IClientPlayer ValidatePlayer(TextCommandCallingArgs args, out int groupId)
    {
        groupId = args.Caller.FromChatGroupId;
        if (!(args.Caller.Player is IClientPlayer result))
        {
            capi.SendChatMessage("[StepUp Advanced] You must be a player to use this command.");
            return null;
        }
        return result;
    }
    private TextCommandResult AddToBlacklist(TextCommandCallingArgs arg)
    {
        int groupId;
        IClientPlayer clientPlayer = ValidatePlayer(arg, out groupId);
        if (clientPlayer == null)
        {
            return TextCommandResult.Error("[StepUp Advanced] Command execution failed.");
        }
        BlockSelection blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null)
        {
            return TextCommandResult.Error("[StepUp Advanced] No block targeted.");
        }
        string blockCode = blockSel.Block.Code.ToString();
        if (!StepUpAdvancedConfig.Current.BlockBlacklist.Contains(blockCode))
        {
            StepUpAdvancedConfig.Current.BlockBlacklist.Add(blockCode);
            StepUpAdvancedConfig.Save(capi);
            return TextCommandResult.Success($"[StepUp Advanced] Block {blockCode} added to the blacklist.");
        }
        else
        {
            return TextCommandResult.Error($"[StepUp Advanced] Block {blockCode} is already in the blacklist.");
        }
    }
    private TextCommandResult RemoveFromBlacklist(TextCommandCallingArgs arg)
    {
        int groupId;
        IClientPlayer player = ValidatePlayer(arg, out groupId);
        if (player == null)
        {
            return TextCommandResult.Error("[StepUp Advanced] Command execution failed.");
        }
        BlockSelection blockSel = capi.World.Player.CurrentBlockSelection;
        if (blockSel == null)
        {
            return TextCommandResult.Error("[StepUp Advanced] No block targeted.");
        }
        string blockCode = blockSel.Block.Code.ToString();
        if (StepUpAdvancedConfig.Current.BlockBlacklist.Contains(blockCode))
        {
            StepUpAdvancedConfig.Current.BlockBlacklist.Remove(blockCode);
            StepUpAdvancedConfig.Save(capi);
            return TextCommandResult.Success($"[StepUp Advanced] Block {blockCode} removed from the blacklist.");
        }
        else
        {
            return TextCommandResult.Error($"[StepUp Advanced] Block {blockCode} is not in the blacklist.");
        }
    }
    private void RegisterHotkeys()
    {
        capi.Input.RegisterHotKey("increaseStepHeight", "Increase Step Height", GlKeys.PageUp, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("decreaseStepHeight", "Decrease Step Height", GlKeys.PageDown, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("increaseStepSpeed", "Increase Step Speed", GlKeys.Up, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("decreaseStepSpeed", "Decrease Step Speed", GlKeys.Down, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("toggleStepUp", "Toggle Step Up", GlKeys.Insert, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("reloadConfig", "Reload Config", GlKeys.Home, HotkeyType.GUIOrOtherControls);
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
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepHeight)
        {
            if (!hasShownMaxEMessage)
            {
                capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot change step height.");
                hasShownMaxEMessage = true;
            }
            return false;
        }
        hasShownMaxEMessage = false;

        if (IsEnforced && Math.Abs(currentStepHeight - StepUpAdvancedConfig.Current.ServerMaxStepHeight) < 0.01f)
        {
            if (!hasShownMaxMessage)
            {
                capi.ShowChatMessage($"Step height cannot exceed {StepUpAdvancedConfig.Current.ServerMaxStepHeight:0.0} blocks.");
                hasShownMaxMessage = true;
            }
            return false;
        }
        hasShownMaxMessage = false;

        float previousStepHeight = currentStepHeight;
        currentStepHeight += Math.Max(StepUpAdvancedConfig.Current.StepHeightIncrement, 0.1f);

        if (IsEnforced)
        {
            currentStepHeight = GameMath.Clamp(currentStepHeight, StepUpAdvancedConfig.Current.ServerMinStepHeight, StepUpAdvancedConfig.Current.ServerMaxStepHeight);
        }

        if (currentStepHeight > previousStepHeight)
        {
            StepUpAdvancedConfig.Current.StepHeight = currentStepHeight;
            StepUpAdvancedConfig.Save(capi);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height increased to {currentStepHeight:0.0} blocks.");
        }
        return true;
    }
    private bool OnDecreaseStepHeight(KeyCombination comb)
    {
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepHeight)
        {
            if (!hasShownMinEMessage)
            {
                capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot change step height.");
                hasShownMinEMessage = true;
            }
            return false;
        }
        hasShownMinEMessage = false;

        if (IsEnforced && Math.Abs(currentStepHeight - StepUpAdvancedConfig.Current.ServerMinStepHeight) < 0.01f)
        {
            if (!hasShownMinMessage)
            {
                capi.ShowChatMessage($"Step height cannot be less than {StepUpAdvancedConfig.Current.ServerMinStepHeight:0.0} blocks.");
                hasShownMinMessage = true;
            }
            return false;
        }
        hasShownMinMessage = false;

        float previousStepHeight = currentStepHeight;
        currentStepHeight -= Math.Max(StepUpAdvancedConfig.Current.StepHeightIncrement, 0.1f);

        if (IsEnforced)
        {
            currentStepHeight = GameMath.Clamp(currentStepHeight, StepUpAdvancedConfig.Current.ServerMinStepHeight, StepUpAdvancedConfig.Current.ServerMaxStepHeight);
        }

        if (currentStepHeight < previousStepHeight)
        {
            StepUpAdvancedConfig.Current.StepHeight = currentStepHeight;
            StepUpAdvancedConfig.Save(capi);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height decreased to {currentStepHeight:0.0} blocks.");
        }
        return true;
    }
    private bool OnIncreaseElevateFactor(KeyCombination comb)
    {
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepSpeed)
        {
            if (!hasShownMaxEMessage)
            {
                capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot change step speed.");
                hasShownMaxEMessage = true;
            }
            return false;
        }
        hasShownMaxEMessage = false;

        if (IsEnforced && Math.Abs(currentElevateFactor - StepUpAdvancedConfig.Current.ServerMaxStepSpeed) < 0.01f)
        {
            if (!hasShownMaxMessage)
            {
                capi.ShowChatMessage($"Step speed cannot exceed {StepUpAdvancedConfig.Current.ServerMaxStepSpeed:0.0} blocks.");
                hasShownMaxMessage = true;
            }
            return false;
        }
        hasShownMaxMessage = false;

        float previousElevateFactor = currentElevateFactor;
        currentElevateFactor += Math.Max(StepUpAdvancedConfig.Current.StepSpeedIncrement, 0.01f);

        if (IsEnforced)
        {
            currentElevateFactor = GameMath.Clamp(currentElevateFactor, StepUpAdvancedConfig.Current.ServerMinStepSpeed, StepUpAdvancedConfig.Current.ServerMaxStepSpeed);
        }

        if (currentElevateFactor > previousElevateFactor)
        {
            StepUpAdvancedConfig.Current.StepSpeed = currentElevateFactor;
            StepUpAdvancedConfig.Save(capi);
            ApplyElevateFactorToPlayer(16f);
            capi.ShowChatMessage($"Step speed increased to {currentElevateFactor:0.0} blocks.");
        }
        return true;
    }
    private bool OnDecreaseElevateFactor(KeyCombination comb)
    {
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientChangeStepSpeed)
        {
            if (!hasShownMinEMessage)
            {
                capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot change step speed.");
                hasShownMinEMessage = true;
            }
            return false;
        }
        hasShownMinEMessage = false;

        if (IsEnforced && Math.Abs(currentElevateFactor - StepUpAdvancedConfig.Current.ServerMinStepSpeed) < 0.01f)
        {
            if (!hasShownMinMessage)
            {
                capi.ShowChatMessage($"Step speed cannot be less than {StepUpAdvancedConfig.Current.ServerMinStepSpeed:0.0} blocks.");
                hasShownMinMessage = true;
            }
            return false;
        }
        hasShownMinMessage = false;

        float previousElevateFactor = currentElevateFactor;
        currentElevateFactor -= Math.Max(StepUpAdvancedConfig.Current.StepSpeedIncrement, 0.01f);

        if (IsEnforced)
        {
            currentElevateFactor = GameMath.Clamp(currentElevateFactor, StepUpAdvancedConfig.Current.ServerMinStepSpeed, StepUpAdvancedConfig.Current.ServerMaxStepSpeed);
        }

        if (currentElevateFactor < previousElevateFactor)
        {
            StepUpAdvancedConfig.Current.StepSpeed = currentElevateFactor;
            StepUpAdvancedConfig.Save(capi);
            ApplyElevateFactorToPlayer(16f);
            capi.ShowChatMessage($"Step speed decreased to {currentElevateFactor:0.0} blocks.");
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
        StepUpAdvancedConfig.Save(capi);
        ApplyStepHeightToPlayer();
        ApplyElevateFactorToPlayer(16f);
        string message = (stepUpEnabled ? "StepUp enabled." : "StepUp disabled.");
        capi.ShowChatMessage(message);
        return true;
    }
    private bool OnReloadConfig(KeyCombination comb)
    {
        if (IsEnforced && !StepUpAdvancedConfig.Current.AllowClientConfigReload)
        {
            if (!hasShownMaxMessage)
            {
                capi.ShowChatMessage("[StepUp Advanced] Server-enforced settings active. Cannot reload config.");
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
        ApplyStepHeightToPlayer();
        ApplyElevateFactorToPlayer(16f);
        capi.ShowChatMessage("Configuration reloaded.");
        return true;
    }
    private bool IsNearBlacklistedBlock(IClientPlayer player)
    {
        BlockPos playerPos = player.Entity.Pos.AsBlockPos;
        IWorldAccessor world = player.Entity.World;
        List<string> blacklist = StepUpAdvancedConfig.Current.BlockBlacklist;
        BlockPos[] positionsToCheck = new BlockPos[]
        {
        //playerPos.DownCopy(), // Block directly below the player
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
            if (blacklist.Contains(block.Code.ToString()))
            {
                return true;
            }
        }
        return false;
    }
    private void ApplyStepHeightToPlayer()
    {
        IClientPlayer player = capi.World?.Player;
        if (player == null)
        {
            capi.World.Logger.Warning("Player object is null. Cannot apply step height.");
            return;
        }
        EntityBehaviorControlledPhysics physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null)
        {
            capi.World.Logger.Warning("Physics behavior is missing on player entity. Cannot apply step height.");
            return;
        }
        bool nearBlacklistedBlock = IsNearBlacklistedBlock(player);
        float stepHeight;

        if (!stepUpEnabled)
        {
            stepHeight = 0.6f;
        }
        else if (!IsEnforced)
        {
            stepHeight = currentStepHeight;
        }
        else if (!nearBlacklistedBlock)
        {
            stepHeight = GameMath.Clamp(currentStepHeight, StepUpAdvancedConfig.Current.ServerMinStepHeight, StepUpAdvancedConfig.Current.ServerMaxStepHeight);
        }
        else
        {
            stepHeight = StepUpAdvancedConfig.Current.DefaultHeight;
        }
        try
        {
            Type type = physicsBehavior.GetType();
            FieldInfo stepHeightField = type.GetField("StepHeight") ?? type.GetField("stepHeight");
            if (stepHeightField != null)
            {
                stepHeightField.SetValue(physicsBehavior, stepHeight);
            }
            else
            {
                capi.ShowChatMessage("StepUp Advanced: StepHeight field not found.");
            }
        }
        catch (Exception ex)
        {
            capi.ShowChatMessage("StepUp Advanced: Failed to set step height. Error: " + ex.Message);
        }
    }
    private void ApplyElevateFactorToPlayer(float dt)
    {
        IClientPlayer player = capi.World?.Player;
        if (player == null || player.Entity == null)
        {
            capi.World.Logger.Warning("Player object is null. Cannot apply elevate factor.");
            return;
        }
        EntityBehaviorControlledPhysics physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null)
        {
            capi.World.Logger.Warning("Physics behavior is missing on player entity. Cannot apply elevate factor.");
            return;
        }
        FieldInfo elevateFactorField = physicsBehavior.GetType().GetField("elevateFactor", BindingFlags.Instance | BindingFlags.NonPublic);
        if (!(elevateFactorField == null))
        {
            double customElevateFactor = (IsEnforced && stepUpEnabled ? ((double)StepUpAdvancedConfig.Current.StepSpeed) : 0.05);
            elevateFactorField.SetValue(physicsBehavior, customElevateFactor);
            _ = (double)elevateFactorField.GetValue(physicsBehavior);
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