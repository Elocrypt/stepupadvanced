using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Infrastructure.Input;

namespace StepUpAdvanced.Application;

/// <summary>
/// Owns the client hotkey surface — the six default bindings, their
/// callbacks, and the fire-once-per-press trackers for the toggle and
/// reload keys.
/// </summary>
/// <remarks>
/// The reload path is deliberately split: input concerns (enforcement guard,
/// blocked toast, hold-once gate) live here; the state orchestration
/// (load from disk, invalidate caches, re-apply) is passed in as
/// <see cref="reloadConfig"/> from the composition root.
/// </remarks>
internal sealed class HotkeyHandlers
{
    private readonly ICoreClientAPI capi;
    private readonly StepHeightController stepController;
    private readonly ElevateFactorController elevateController;
    private readonly MessageDebouncer toasts;

    // Performs the actual config reload + re-apply. Invoked only after the
    // enforcement and hold-once guards in OnReloadConfig pass.
    private readonly Action reloadConfig;

    // Hold-once trackers for toggle and reload. Initialized in Register
    // (after the bindings exist); each self-subscribes to capi.Event.KeyUp.
    private KeyHoldTracker? toggleHeld;
    private KeyHoldTracker? reloadHeld;

    public HotkeyHandlers(
        ICoreClientAPI capi,
        StepHeightController stepController,
        ElevateFactorController elevateController,
        MessageDebouncer toasts,
        Action reloadConfig)
    {
        this.capi = capi;
        this.stepController = stepController;
        this.elevateController = elevateController;
        this.toasts = toasts;
        this.reloadConfig = reloadConfig;
    }

    /// <summary>
    /// Registers the six hotkeys and constructs the hold-once trackers.
    /// Called once from <c>StartClientSide</c>.
    /// </summary>
    public void Register()
    {
        var binder = new HotkeyBinder(capi);
        binder.Bind("increaseStepHeight", KeyLabel("sua:key.increase-height"), GlKeys.PageUp,   OnIncreaseStepHeight);
        binder.Bind("decreaseStepHeight", KeyLabel("sua:key.decrease-height"), GlKeys.PageDown, OnDecreaseStepHeight);
        binder.Bind("increaseStepSpeed",  KeyLabel("sua:key.increase-speed"),  GlKeys.Up,       OnIncreaseElevateFactor);
        binder.Bind("decreaseStepSpeed",  KeyLabel("sua:key.decrease-speed"),  GlKeys.Down,     OnDecreaseElevateFactor);
        binder.Bind("toggleStepUp",       KeyLabel("sua:key.toggle"),          GlKeys.Insert,   OnToggleStepUp);
        binder.Bind("reloadConfig",       KeyLabel("sua:key.reload"),          GlKeys.Home,     OnReloadConfig);

        // KeyHoldTracker subscribes to capi.Event.KeyUp internally for its
        // hotkey id and clears the held flag on key release. Constructed
        // after binder.Bind so the hotkey is in the HotKeys dictionary
        // when the tracker first resolves CurrentMapping.
        toggleHeld = new KeyHoldTracker(capi, "toggleStepUp");
        reloadHeld = new KeyHoldTracker(capi, "reloadConfig");
    }

    /// <summary>
    /// Controls-menu label: "StepUp Advanced: Toggle Step Up". The prefix
    /// groups the six binds visually since the menu lists every mod's keys
    /// together. Keys need the "sua:" domain to match the lang files, or
    /// Lang.Get misses and returns the raw key. Lang values carry padding
    /// spaces for chat output, so they're trimmed here.
    /// </summary>
    private static string KeyLabel(string langKey)
        => $"{Lang.Get("sua:modname").Trim()}: {Lang.Get(langKey).Trim()}";

    // Height/speed keys are refused in Basic mode before reaching the
    // controllers, so nothing is written to the config either.
    private bool OnIncreaseStepHeight(KeyCombination comb)
        => !RejectInBasicMode() && stepController.Increase();
    private bool OnDecreaseStepHeight(KeyCombination comb)
        => !RejectInBasicMode() && stepController.Decrease();
    private bool OnIncreaseElevateFactor(KeyCombination comb)
        => !RejectInBasicMode() && elevateController.Increase(stepController.IsEnabled);
    private bool OnDecreaseElevateFactor(KeyCombination comb)
        => !RejectInBasicMode() && elevateController.Decrease(stepController.IsEnabled);

    /// <summary>
    /// Returns <c>true</c> (and toasts once) when Basic mode locks the
    /// height/speed keys.
    /// </summary>
    private bool RejectInBasicMode()
    {
        if (!BasicModeState.Active) return false;

        if (toasts.BasicModeLocked.TryShow())
            ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("basic-mode"))} – {ChatFormatting.Muted(ChatFormatting.L("basic-mode-locked"))}");
        return true;
    }

    private bool OnToggleStepUp(KeyCombination comb)
    {
        if (toggleHeld?.TryFire() != true) return false;

        stepController.Toggle();
        elevateController.ApplyNow(stepController.IsEnabled);
        return true;
    }

    private bool OnReloadConfig(KeyCombination comb)
    {
        // Skipped in Basic mode: the local enforcement flag describes the
        // player's own single-player setup, not this server, and a reload
        // can't raise anything there.
        if (EnforcementState.IsClientReloadBlocked(BasicModeState.Active, StepUpOptions.Current))
        {
            if (toasts.ReloadBlocked.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("server-enforced"))} – {ChatFormatting.Muted(ChatFormatting.L("reload-blocked"))}");
            return false;
        }
        // Re-arm after the enforcement guard so the toast fires again
        // as soon as enforcement permits reload, not only after a successful one.
        toasts.ReloadBlocked.Reset();

        if (reloadHeld?.TryFire() != true)
        {
            return false;
        }

        reloadConfig();
        return true;
    }
}
