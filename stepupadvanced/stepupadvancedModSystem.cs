using HarmonyLib;
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
    public const float MinElevateFactor = 0.5f;
    public const float MinElevateFactorIncrement = 0.1f;
    public const float AbsoluteMaxElevateFactor = 2.0f;
    public const float DefaultElevateFactor = 0.7f;
    private float currentElevateFactor;
    private float currentStepHeight;
    private bool hasShownMaxMessage = false;
    private bool hasShownMinMessage = false;
    private bool toggleStepUpKeyHeld = false;
    private bool reloadConfigKeyHeld = false;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        StepUpAdvancedConfig.Load(api);
        Harmony.DEBUG = true;
        var harmony = new Harmony("stepupadvanced.mod");
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
        currentStepHeight = StepUpAdvancedConfig.Current?.StepHeight ?? DefaultStepHeight;
        currentElevateFactor = StepUpAdvancedConfig.Current?.StepSpeed ?? DefaultElevateFactor;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? true;

        RegisterHotkeys();
        
        capi.Event.RegisterGameTickListener(dt => ApplyStepHeightToPlayer(), 1000);
        capi.Event.RegisterGameTickListener(ApplyElevateFactorToPlayer, 16);

    }

    private void RegisterHotkeys()
    {
        capi.Input.RegisterHotKey("increaseStepHeight", "Increase Step Height", GlKeys.PageUp, HotkeyType.GUIOrOtherControls, false, false, false);
        capi.Input.RegisterHotKey("decreaseStepHeight", "Decrease Step Height", GlKeys.PageDown, HotkeyType.GUIOrOtherControls, false, false, false);
        capi.Input.RegisterHotKey("increaseStepSpeed", "Increase Step Speed", GlKeys.Up, HotkeyType.GUIOrOtherControls, false, false, false);
        capi.Input.RegisterHotKey("decreaseStepSpeed", "Decrease Step Speed", GlKeys.Down, HotkeyType.GUIOrOtherControls, false, false, false);
        capi.Input.RegisterHotKey("toggleStepUp", "Toggle Step Up", GlKeys.Insert, HotkeyType.GUIOrOtherControls);
        capi.Input.RegisterHotKey("reloadConfig", "Reload Config", GlKeys.Home, HotkeyType.GUIOrOtherControls);

        capi.Input.SetHotKeyHandler("increaseStepHeight", OnIncreaseStepHeight);
        capi.Input.SetHotKeyHandler("decreaseStepHeight", OnDecreaseStepHeight);
        capi.Input.SetHotKeyHandler("increaseStepSpeed", OnIncreaseElevateFactor);
        capi.Input.SetHotKeyHandler("decreaseStepSpeed", OnDecreaseElevateFactor);
        capi.Input.SetHotKeyHandler("toggleStepUp", OnToggleStepUp);
        capi.Input.SetHotKeyHandler("reloadConfig", OnReloadConfig);

        capi.Event.KeyUp += (KeyEvent ke) =>
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

    private bool OnIncreaseElevateFactor(KeyCombination comb)
    {
        if (Math.Abs(currentElevateFactor - AbsoluteMaxElevateFactor) < 0.01f)
        {
            if (!hasShownMaxMessage)
            {
                capi.ShowChatMessage($"Step speed cannot exceed {AbsoluteMaxElevateFactor:0.0} blocks.");
                hasShownMaxMessage = true;
            }
            return false;
        }
        hasShownMaxMessage = false;
        float previousElevateFactor = currentElevateFactor;
        currentElevateFactor += Math.Max(StepUpAdvancedConfig.Current.StepSpeedIncrement, 0.01f);
        currentElevateFactor = Math.Min(currentElevateFactor, AbsoluteMaxElevateFactor);
        if (currentElevateFactor > previousElevateFactor)
        {
            StepUpAdvancedConfig.Current.StepSpeed = currentElevateFactor;
            StepUpAdvancedConfig.Save(capi);
            ApplyElevateFactorToPlayer(16);
            capi.ShowChatMessage($"Step speed increased to {currentElevateFactor:0.0} blocks.");
        }
        return true;
    }

    private bool OnDecreaseElevateFactor(KeyCombination comb)
    {
        if (Math.Abs(currentElevateFactor - MinElevateFactor) < 0.01f)
        {
            if (!hasShownMinMessage)
            {
                capi.ShowChatMessage($"Step speed cannot be less than {MinElevateFactor} blocks.");
                hasShownMinMessage = true;
            }
            return false;
        }
        hasShownMinMessage = false;
        float previousElevateFactor = currentElevateFactor;
        currentElevateFactor -= Math.Max(StepUpAdvancedConfig.Current.StepSpeedIncrement, 0.01f);
        currentElevateFactor = Math.Max(currentElevateFactor, MinElevateFactor);
        if (currentElevateFactor < previousElevateFactor)
        {
            StepUpAdvancedConfig.Current.StepSpeed = currentElevateFactor;
            StepUpAdvancedConfig.Save(capi);
            ApplyElevateFactorToPlayer(16);
            capi.ShowChatMessage($"Step speed decreased to {currentElevateFactor:0.0} blocks.");
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
        ApplyElevateFactorToPlayer(16);
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
        currentElevateFactor = StepUpAdvancedConfig.Current?.StepSpeed ?? currentElevateFactor;
        stepUpEnabled = StepUpAdvancedConfig.Current?.StepUpEnabled ?? stepUpEnabled;
        ApplyStepHeightToPlayer();
        ApplyElevateFactorToPlayer(16);
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

    private void ApplyElevateFactorToPlayer(float dt)
    {
        var player = capi.World?.Player;
        if (player == null || player.Entity == null)
        {
            capi.World.Logger.Warning("Player object is null. Cannot apply elevate factor.");
            return;
        }
        var physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null)
        {
            capi.World.Logger.Warning("Physics behavior is missing on player entity. Cannot apply elevate factor.");
            return;
        }
        var type = physicsBehavior.GetType();
        var elevateFactorField = type.GetField("elevateFactor", BindingFlags.NonPublic | BindingFlags.Instance);
        if (elevateFactorField == null)
        {
            capi.World.Logger.Warning("[StepUp Advanced] elevateFactor field not found.");
            return;
        }
        double customElevateFactor = stepUpEnabled
            ? StepUpAdvancedConfig.Current.StepSpeed
            : 0.05;

        elevateFactorField.SetValue(physicsBehavior, customElevateFactor);
        double currentElevateFactor = (double)elevateFactorField.GetValue(physicsBehavior);
        capi.World.Logger.Event($"[Debug] Applied elevateFactor: {customElevateFactor}");
    }
}