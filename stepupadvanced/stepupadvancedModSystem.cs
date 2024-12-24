using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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
    public const float MinStepUpSpeed = 0.05f;
    public const float MinStepUpSpeedIncrement = 0.01f;
    public const float AbsoluteMaxStepUpSpeed = 10.0f;
    public const float DefaultStepSpeed = 0.07f;
    private float currentStepUpSpeed;
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
        currentStepUpSpeed = StepUpAdvancedConfig.Current?.StepUpSpeed ?? 1.0f;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? true;

        api.Input.RegisterHotKey("increaseStepHeight", "Increase Step Height", GlKeys.PageUp, HotkeyType.GUIOrOtherControls, false, false, false);
        api.Input.RegisterHotKey("decreaseStepHeight", "Decrease Step Height", GlKeys.PageDown, HotkeyType.GUIOrOtherControls, false, false, false);
        api.Input.RegisterHotKey("increaseStepUpSpeed", "Increase Step Up Speed", GlKeys.PageUp, HotkeyType.GUIOrOtherControls, false, false, true);
        api.Input.RegisterHotKey("decreaseStepUpSpeed", "Decrease Step Up Speed", GlKeys.PageDown, HotkeyType.GUIOrOtherControls, false, false, true);
        api.Input.RegisterHotKey("toggleStepUp", "Toggle Step Up", GlKeys.Insert, HotkeyType.GUIOrOtherControls);
        api.Input.RegisterHotKey("reloadConfig", "Reload Config", GlKeys.Home, HotkeyType.GUIOrOtherControls);

        api.Input.SetHotKeyHandler("increaseStepHeight", OnIncreaseStepHeight);
        api.Input.SetHotKeyHandler("decreaseStepHeight", OnDecreaseStepHeight);
        api.Input.SetHotKeyHandler("increaseStepUpSpeed", OnIncreaseStepUpSpeed);
        api.Input.SetHotKeyHandler("decreaseStepUpSpeed", OnDecreaseStepUpSpeed);
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
        capi.Event.RegisterGameTickListener(ApplyStepUpSpeedToPlayer, 16);
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

    private bool OnIncreaseStepUpSpeed(KeyCombination comb)
    {
        if (Math.Abs(currentStepUpSpeed - AbsoluteMaxStepUpSpeed) < 0.01f)
        {
            if (!hasShownMaxMessage)
            {
                capi.ShowChatMessage($"Step Up speed cannot exceed {AbsoluteMaxStepUpSpeed:0.0} blocks.");
                hasShownMaxMessage = true;
            }
            return false;
        }
        hasShownMaxMessage = false;
        float previousStepUpSpeed = currentStepUpSpeed;
        currentStepUpSpeed += Math.Max(StepUpAdvancedConfig.Current.StepUpSpeedIncrement, 0.1f);
        currentStepUpSpeed = Math.Min(currentStepUpSpeed, AbsoluteMaxStepUpSpeed);
        if (currentStepUpSpeed > previousStepUpSpeed)
        {
            StepUpAdvancedConfig.Current.StepUpSpeed = currentStepUpSpeed;
            StepUpAdvancedConfig.Save(capi);
            ApplyStepUpSpeedToPlayer(16);
            capi.ShowChatMessage($"Step Up speed increased to {currentStepUpSpeed:0.0} blocks.");
        }
        return true;
    }

    private bool OnDecreaseStepUpSpeed(KeyCombination comb)
    {
        if (Math.Abs(currentStepUpSpeed - MinStepUpSpeed) < 0.01f)
        {
            if (!hasShownMinMessage)
            {
                capi.ShowChatMessage($"Step Up speed cannot be less than {MinStepUpSpeed} blocks.");
                hasShownMinMessage = true;
            }
            return false;
        }
        hasShownMinMessage = false;
        float previousStepUpSpeed = currentStepUpSpeed;
        currentStepUpSpeed -= Math.Max(StepUpAdvancedConfig.Current.StepUpSpeedIncrement, 0.1f);
        currentStepUpSpeed = Math.Max(currentStepUpSpeed, MinStepUpSpeed);
        if (currentStepUpSpeed < previousStepUpSpeed)
        {
            StepUpAdvancedConfig.Current.StepUpSpeed = currentStepUpSpeed;
            StepUpAdvancedConfig.Save(capi);
            ApplyStepUpSpeedToPlayer(16);
            capi.ShowChatMessage($"Step Up speed decreased to {currentStepUpSpeed:0.0} blocks.");
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
        ApplyStepUpSpeedToPlayer(16);
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
        currentStepUpSpeed = StepUpAdvancedConfig.Current?.StepUpSpeed ?? currentStepUpSpeed;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? stepUpEnabled;
        ApplyStepHeightToPlayer();
        ApplyStepUpSpeedToPlayer(16);
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

    private void ApplyStepUpSpeedToPlayer(float dt)
    {
        var player = capi.World?.Player;
        if (player == null || player.Entity == null)
        {
            capi.World.Logger.Warning("Player object is null. Cannot apply step up speed.");
            return;
        }
        var physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null)
        {
            capi.World.Logger.Warning("Physics behavior is missing on player entity. Cannot apply step up speed.");
            return;
        }
        var type = physicsBehavior.GetType();
        var stepUpSpeedField = type.GetField("stepUpSpeed") ?? type.GetField("StepUpSpeed");
        if (stepUpSpeedField == null)
        {
            capi.ShowChatMessage("StepUp Advanced: StepUpSpeed field not found.");
            return;
        }
        float stepUpSpeed = StepUpAdvancedConfig.Current.StepUpSpeed;
        stepUpSpeedField.SetValue(physicsBehavior, stepUpSpeed);
        var posField = physicsBehavior.GetType().GetField("newPos", BindingFlags.Public | BindingFlags.Instance);
        if (posField == null)
        {
            capi.World.Logger.Warning("[StepUpForceMod] newPos field not found.");
            return;
        }

        Vec3d newPos = (Vec3d)posField.GetValue(physicsBehavior);
        var motionField = physicsBehavior.GetType().GetField("moveDelta", BindingFlags.Public | BindingFlags.Instance);

        if (motionField != null)
        {
            Vec3d moveDelta = (Vec3d)motionField.GetValue(physicsBehavior);

            if (moveDelta != null && moveDelta.Y > 0)
            {
                newPos.Y += stepUpSpeed * dt;
                posField.SetValue(physicsBehavior, newPos);
                capi.World.Logger.Event($"[Debug] Forced step motion. New Y: {newPos.Y}, stepUpSpeed: {stepUpSpeed}");
            }
        }
    }
}