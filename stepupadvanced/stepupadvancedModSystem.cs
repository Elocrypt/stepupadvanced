using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace stepupadvanced;

public class stepupadvancedModSystem : ModSystem
{
    private bool stepUpEnabled = true;
    private ICoreClientAPI capi;
    private const float MinStepHeight = 0.5f;
    private const float AbsoluteMaxStepHeight = 2.0f;
    private float currentStepHeight = 1.2f;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        stepupadvancedConfig.Load(api);
        api.World.Logger.Event("Initialized 'StepUp Advanced' mod");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        stepupadvancedConfig.Load(api);

        api.Input.RegisterHotKey("increaseStepHeight", "Increase Step Height", GlKeys.PageUp, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("decreaseStepHeight", "Decrease Step Height", GlKeys.PageDown, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("toggleStepUp", "Toggle Step Up", GlKeys.Insert, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("reloadConfig", "Reload Config", GlKeys.Home, HotkeyType.GUIOrOtherControls);

        api.Input.SetHotKeyHandler("increaseStepHeight", OnIncreaseStepHeight);
        api.Input.SetHotKeyHandler("decreaseStepHeight", OnDecreaseStepHeight);
        api.Input.SetHotKeyHandler("toggleStepUp", OnToggleStepUp);
        api.Input.SetHotKeyHandler("reloadConfig", OnReloadConfig);

        capi.Event.RegisterGameTickListener(dt =>
        {
            ApplyStepHeightToPlayer();
        }, 5000);
    }

    private bool OnIncreaseStepHeight(KeyCombination comb)
    {
        if (currentStepHeight < AbsoluteMaxStepHeight)
        {
            currentStepHeight += stepupadvancedConfig.Current.StepHeightIncrement;
            currentStepHeight = Math.Min(currentStepHeight, AbsoluteMaxStepHeight);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height increased to {currentStepHeight:0.0} blocks.");
            return true;
        }

        capi.ShowChatMessage($"Step height cannot exceed {AbsoluteMaxStepHeight} blocks.");
        return false;
    }

    private bool OnDecreaseStepHeight(KeyCombination comb)
    {
        if (currentStepHeight > MinStepHeight)
        {
            currentStepHeight -= stepupadvancedConfig.Current.StepHeightIncrement;
            currentStepHeight = Math.Max(currentStepHeight, MinStepHeight);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height decreased to {currentStepHeight:0.0} blocks.");
            return true;
        }

        capi.ShowChatMessage($"Step height cannot be less than {MinStepHeight} blocks.");
        return false;
    }

    private bool OnToggleStepUp(KeyCombination comb)
    {
        stepUpEnabled = !stepUpEnabled;
        ApplyStepHeightToPlayer();
        string message = stepUpEnabled ? "StepUp enabled." : "StepUp disabled.";
        capi.ShowChatMessage(message);
        return true;
    }

    private bool OnReloadConfig(KeyCombination comb)
    {
        ApplyStepHeightToPlayer();
        capi.ShowChatMessage("Configuration reloaded.");
        return true;
    }

    private void ApplyStepHeightToPlayer()
    {
        var player = capi.World.Player;
        float stepHeight = stepUpEnabled ? currentStepHeight : stepupadvancedConfig.Current.DefaultHeight;
        stepHeight = Math.Min(stepHeight, AbsoluteMaxStepHeight);
        player.Entity.GetBehavior<EntityBehaviorControlledPhysics>().StepHeight = stepHeight;
    }
}