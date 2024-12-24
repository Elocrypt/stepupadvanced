using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace stepupadvanced;

public class StepUpAdvancedModSystem : ModSystem
{
    private bool stepUpEnabled = true;
    private ICoreClientAPI capi;
    public const float MinStepHeight = 0.5f;
    public const float MinStepHeightIncrement = 0.1f;
    public const float AbsoluteMaxStepHeight = 2.0f;
    public const float DefaultStepHeight = 0.6f;
    private float currentStepHeight;
    private bool hasShownMaxMessage = false;
    private bool hasShownMinMessage = false;
    private bool toggleStepUpKeyHeld = false;
    private bool reloadConfigKeyHeld = false;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        StepUpAdvancedConfig.Load(api);
        api.World.Logger.Event("Initialized 'StepUp Advanced' mod");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        StepUpAdvancedConfig.Load(api);
        currentStepHeight = StepUpAdvancedConfig.Current?.StepHeight ?? 1.2f;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? true;

        api.Input.RegisterHotKey("increaseStepHeight", "Increase Step Height", GlKeys.PageUp, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("decreaseStepHeight", "Decrease Step Height", GlKeys.PageDown, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("toggleStepUp", "Toggle Step Up", GlKeys.Insert, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("reloadConfig", "Reload Config", GlKeys.Home, HotkeyType.GUIOrOtherControls);

        api.Input.SetHotKeyHandler("increaseStepHeight", OnIncreaseStepHeight);
        api.Input.SetHotKeyHandler("decreaseStepHeight", OnDecreaseStepHeight);
        api.Input.SetHotKeyHandler("toggleStepUp", OnToggleStepUp);
        api.Input.SetHotKeyHandler("reloadConfig", OnReloadConfig);

        api.Event.KeyUp += (KeyEvent ke) =>
        {
            if (ke.KeyCode == api.Input.HotKeys["toggleStepUp"].CurrentMapping.KeyCode)
            {
                toggleStepUpKeyHeld = false;
            }
            if (ke.KeyCode == api.Input.HotKeys["reloadConfig"].CurrentMapping.KeyCode)
            {
                reloadConfigKeyHeld = false;
            }
        };
        capi.Event.RegisterGameTickListener(dt => ApplyStepHeightToPlayer(), 1000);
    }

    private bool OnIncreaseStepHeight(KeyCombination comb)
    {
        if (Math.Abs(currentStepHeight - AbsoluteMaxStepHeight) < 0.01f)
        {
            if (!hasShownMaxMessage)
            {
                capi.ShowChatMessage($"Step height cannot exceed {AbsoluteMaxStepHeight:0.0} blocks.");
                hasShownMaxMessage = true;
            }
            return false;
        }
        hasShownMaxMessage = false;
        float previousStepHeight = currentStepHeight;
        currentStepHeight += Math.Max(StepUpAdvancedConfig.Current.StepHeightIncrement, 0.1f);
        currentStepHeight = Math.Min(currentStepHeight, AbsoluteMaxStepHeight);
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
        if (Math.Abs(currentStepHeight - MinStepHeight) < 0.01f)
        {
            if (!hasShownMinMessage)
            {
                capi.ShowChatMessage($"Step height cannot be less than {MinStepHeight} blocks.");
                hasShownMinMessage= true;
            }
            return false;
        }
        hasShownMinMessage = false;
        float previousStepHeight = currentStepHeight;
        currentStepHeight -= Math.Max(StepUpAdvancedConfig.Current.StepHeightIncrement, 0.1f);
        currentStepHeight = Math.Max(currentStepHeight, MinStepHeight);
        if (currentStepHeight < previousStepHeight)
        {
            StepUpAdvancedConfig.Current.StepHeight = currentStepHeight;
            StepUpAdvancedConfig.Save(capi);
            ApplyStepHeightToPlayer();
            capi.ShowChatMessage($"Step height decreased to {currentStepHeight:0.0} blocks.");
        }
        return true;
    }

    private bool OnToggleStepUp(KeyCombination comb)
    {
        if (toggleStepUpKeyHeld) return false;
        toggleStepUpKeyHeld = true;
        stepUpEnabled = !stepUpEnabled;
        StepUpAdvancedConfig.Current.StepUpEnabled = stepUpEnabled;
        StepUpAdvancedConfig.Save(capi);
        ApplyStepHeightToPlayer();
        string message = stepUpEnabled ? "StepUp enabled." : "StepUp disabled.";
        capi.ShowChatMessage(message);
        return true;
    }

    private bool OnReloadConfig(KeyCombination comb)
    {
        if (reloadConfigKeyHeld) return false;
        reloadConfigKeyHeld = true;
        StepUpAdvancedConfig.Load(capi);
        currentStepHeight = StepUpAdvancedConfig.Current?.StepHeight ?? currentStepHeight;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? stepUpEnabled;
        ApplyStepHeightToPlayer();
        capi.ShowChatMessage("Configuration reloaded.");
        return true;
    }

    private void ApplyStepHeightToPlayer()
    {
        var player = capi.World?.Player;
        if (player == null)
        {
            capi.World.Logger.Warning("Player object is null. Cannot apply step height.");
            return;
        }
        var physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null)
        {
            capi.World.Logger.Warning("Physics behavior is missing on player entity. Cannot apply step height.");
            return;
        }
        float stepHeight = stepUpEnabled ? currentStepHeight : StepUpAdvancedConfig.Current.DefaultHeight;
        stepHeight = Math.Min(stepHeight, AbsoluteMaxStepHeight);
        var type = physicsBehavior.GetType();
        var stepHeightField = type.GetField("StepHeight") ?? type.GetField("stepHeight");

        if (stepHeightField == null)
        {
            capi.ShowChatMessage("StepUp Advanced: StepHeight field not found.");
            return;
        }
        try
        {
            stepHeightField.SetValue(physicsBehavior, stepHeight);
        }
        catch (Exception ex)
        {
            capi.ShowChatMessage($"StepUp Advanced: Failed to set step height. Error: {ex.Message}");
        }
    }
}