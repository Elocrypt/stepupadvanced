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
        binder.Bind("increaseStepHeight", Lang.Get("key.increase-height"), GlKeys.PageUp,   OnIncreaseStepHeight);
        binder.Bind("decreaseStepHeight", Lang.Get("key.decrease-height"), GlKeys.PageDown, OnDecreaseStepHeight);
        binder.Bind("increaseStepSpeed",  Lang.Get("key.increase-speed"),  GlKeys.Up,       OnIncreaseElevateFactor);
        binder.Bind("decreaseStepSpeed",  Lang.Get("key.decrease-speed"),  GlKeys.Down,     OnDecreaseElevateFactor);
        binder.Bind("toggleStepUp",       Lang.Get("key.toggle"),          GlKeys.Insert,   OnToggleStepUp);
        binder.Bind("reloadConfig",       Lang.Get("key.reload"),          GlKeys.Home,     OnReloadConfig);

        // KeyHoldTracker subscribes to capi.Event.KeyUp internally for its
        // hotkey id and clears the held flag on key release. Constructed
        // after binder.Bind so the hotkey is in the HotKeys dictionary
        // when the tracker first resolves CurrentMapping.
        toggleHeld = new KeyHoldTracker(capi, "toggleStepUp");
        reloadHeld = new KeyHoldTracker(capi, "reloadConfig");
    }

    private bool OnIncreaseStepHeight(KeyCombination comb)
        => stepController.Increase();
    private bool OnDecreaseStepHeight(KeyCombination comb)
        => stepController.Decrease();
    private bool OnIncreaseElevateFactor(KeyCombination comb)
        => elevateController.Increase(stepController.IsEnabled);
    private bool OnDecreaseElevateFactor(KeyCombination comb)
        => elevateController.Decrease(stepController.IsEnabled);

    private bool OnToggleStepUp(KeyCombination comb)
    {
        if (toggleHeld?.TryFire() != true) return false;

        stepController.Toggle();
        elevateController.ApplyNow(stepController.IsEnabled);
        return true;
    }

    private bool OnReloadConfig(KeyCombination comb)
    {

        if (StepUpOptions.Current?.ServerEnforceSettings == true && !StepUpOptions.Current.AllowClientConfigReload)
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
