using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace stepupadvanced;

public class stepupadvancedModSystem : ModSystem
{
	private bool stepUpEnabled = true;
	private ICoreClientAPI capi;
	private float lastYPosition;
	private const float MinStepHeight = 0.5f;
	private const float MaxStepHeight = 3.0f;
	public override void Start(ICoreAPI api)
	{
		base.Start(api);
		stepupadvancedConfig.Load(api);
		api.World.Logger.Event("Initialized 'StepUp Advanced' mod");
	}

	public override void StartClientSide(ICoreClientAPI api)
	{
		capi = api;
		api.Input.RegisterHotKey("increaseStepHeight", "Increase Step Height", GlKeys.PageUp, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("decreaseStepHeight", "Decrease Step Height", GlKeys.PageDown, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("toggleStepUp", "Toggle Step Up", GlKeys.Insert, HotkeyType.GUIOrOtherControls);
		api.Input.RegisterHotKey("reloadConfig", "Reload StepUp Config", GlKeys.Home, HotkeyType.GUIOrOtherControls);
		api.Event.PlayerEntitySpawn += OnPlayerEntitySpawn;
		api.Event.RegisterGameTickListener(OnPlayerMove, 50);
		api.Input.SetHotKeyHandler("toggleStepUp", OnToggleStepUp);
		api.Input.SetHotKeyHandler("reloadConfig", OnReloadConfig);
        api.Input.SetHotKeyHandler("increaseStepHeight", OnIncreaseStepHeight);
        api.Input.SetHotKeyHandler("decreaseStepHeight", OnDecreaseStepHeight);
    }

    private void OnPlayerEntitySpawn(IClientPlayer player)
	{
		float stepHeight = stepUpEnabled ? stepupadvancedConfig.Current.StepHeight : stepupadvancedConfig.Current.DefaultHeight;
        player.Entity.GetBehavior<EntityBehaviorControlledPhysics>().StepHeight = stepHeight;
		lastYPosition = (float)player.Entity.Pos.Y;
	}

	private bool OnToggleStepUp(KeyCombination comb)
	{
		stepUpEnabled = !stepUpEnabled;
		float stepHeight = stepUpEnabled ? stepupadvancedConfig.Current.StepHeight : stepupadvancedConfig.Current.DefaultHeight;
		IClientPlayer player = capi.World.Player;
		player.Entity.GetBehavior<EntityBehaviorControlledPhysics>().StepHeight = stepHeight;
		string message = stepUpEnabled ? "StepUp enabled" : "StepUp disabled";
		capi.ShowChatMessage(message);
		return true;
	}

	private bool OnReloadConfig(KeyCombination comb)
	{
		stepupadvancedConfig.Load(capi);
		IClientPlayer player = capi.World.Player;
		float stepHeight = stepUpEnabled ? stepupadvancedConfig.Current.StepHeight : stepupadvancedConfig.Current.DefaultHeight;
		player.Entity.GetBehavior<EntityBehaviorControlledPhysics>().StepHeight = stepHeight;
		capi.ShowChatMessage("StepUp Config reloaded dynamically!");
        capi.World.Logger.Event("StepUp config reloaded dynamically. Current Config: StepHeight = {0}", stepupadvancedConfig.Current.StepHeight);
		return true;
	}

    private bool OnIncreaseStepHeight(KeyCombination comb)
    {
        if (stepupadvancedConfig.Current.StepHeight < MaxStepHeight)
        {
            stepupadvancedConfig.Current.StepHeight = Math.Min(stepupadvancedConfig.Current.StepHeight + 0.1f, MaxStepHeight);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height increased to {stepupadvancedConfig.Current.StepHeight:0.0} blocks.");
            stepupadvancedConfig.Save(capi);
        }
        else
        {
            capi.ShowChatMessage($"Step height is already at the maximum of {MaxStepHeight} blocks.");
        }
        return true;
    }

    private bool OnDecreaseStepHeight(KeyCombination comb)
    {
        if (stepupadvancedConfig.Current.StepHeight > MinStepHeight)
        {
            stepupadvancedConfig.Current.StepHeight = Math.Max(stepupadvancedConfig.Current.StepHeight - 0.1f, MinStepHeight);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height decreased to {stepupadvancedConfig.Current.StepHeight:0.0} blocks.");
            stepupadvancedConfig.Save(capi);
        }
        else
        {
            capi.ShowChatMessage($"Step height is already at the minimum of {MinStepHeight} blocks.");
        }
        return true;
    }

	private void ApplyStepHeightToPlayer()
	{
		IClientPlayer player = capi.World.Player;
		player.Entity.GetBehavior<EntityBehaviorControlledPhysics>().StepHeight = stepupadvancedConfig.Current.StepHeight;
	}
    private void OnPlayerMove(float dt)
	{
		if (!stepUpEnabled) return;
		IClientPlayer player = capi.World.Player;
		Entity entity = player.Entity;
		float currentYPosition = (float)entity.Pos.Y;
		if (currentYPosition > lastYPosition)
		{
			float stepHeight = currentYPosition - lastYPosition;
			if (stepHeight > 1f)
			{
                capi.World.Logger.Event($"Player stepped up {stepHeight:0.0} blocks");
            }
		}
        lastYPosition = currentYPosition;
    }
}
