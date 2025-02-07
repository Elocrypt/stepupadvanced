using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace stepupadvanced;

public class StepUpAdvancedModSystem : ModSystem
{
	private bool stepUpEnabled = true;

	private static ICoreClientAPI capi;

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
	public override void StartClientSide(ICoreClientAPI api)
	{
		base.StartClientSide(api);
		capi = api;
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
		capi.Event.KeyUp += delegate(KeyEvent ke)
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
		if (Math.Abs(currentStepHeight - 2f) < 0.01f)
		{
			if (!hasShownMaxMessage)
			{
				capi.ShowChatMessage($"Step height cannot exceed {2f:0.0} blocks.");
				hasShownMaxMessage = true;
			}
			return false;
		}
		hasShownMaxMessage = false;
		float previousStepHeight = currentStepHeight;
		currentStepHeight += Math.Max(StepUpAdvancedConfig.Current.StepHeightIncrement, 0.1f);
		currentStepHeight = Math.Min(currentStepHeight, 2f);
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
		if (Math.Abs(currentStepHeight - 0.2f) < 0.01f)
		{
			if (!hasShownMinMessage)
			{
				capi.ShowChatMessage($"Step height cannot be less than {0.2f} blocks.");
				hasShownMinMessage = true;
			}
			return false;
		}
		hasShownMinMessage = false;
		float previousStepHeight = currentStepHeight;
		currentStepHeight -= Math.Max(StepUpAdvancedConfig.Current.StepHeightIncrement, 0.1f);
		currentStepHeight = Math.Max(currentStepHeight, 0.2f);
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
		if (Math.Abs(currentElevateFactor - 2f) < 0.01f)
		{
			if (!hasShownMaxMessage)
			{
				capi.ShowChatMessage($"Step speed cannot exceed {2f:0.0} blocks.");
				hasShownMaxMessage = true;
			}
			return false;
		}
		hasShownMaxMessage = false;
		float previousElevateFactor = currentElevateFactor;
		currentElevateFactor += Math.Max(StepUpAdvancedConfig.Current.StepSpeedIncrement, 0.01f);
		currentElevateFactor = Math.Min(currentElevateFactor, 2f);
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
		if (Math.Abs(currentElevateFactor - 0.5f) < 0.01f)
		{
			if (!hasShownMinMessage)
			{
				capi.ShowChatMessage($"Step speed cannot be less than {0.5f} blocks.");
				hasShownMinMessage = true;
			}
			return false;
		}
		hasShownMinMessage = false;
		float previousElevateFactor = currentElevateFactor;
		currentElevateFactor -= Math.Max(StepUpAdvancedConfig.Current.StepSpeedIncrement, 0.01f);
		currentElevateFactor = Math.Max(currentElevateFactor, 0.5f);
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
        float stepHeight = (stepUpEnabled && !nearBlacklistedBlock) ? currentStepHeight : StepUpAdvancedConfig.Current.DefaultHeight;
        stepHeight = Math.Min(stepHeight, 2f);
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
			double customElevateFactor = (stepUpEnabled ? ((double)StepUpAdvancedConfig.Current.StepSpeed) : 0.05);
			elevateFactorField.SetValue(physicsBehavior, customElevateFactor);
			_ = (double)elevateFactorField.GetValue(physicsBehavior);
		}
	}
}
