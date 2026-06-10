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
/// <para>
/// Phase 8 Step 8.10 extracts this from <c>StepUpAdvancedModSystem</c>.
/// The default keybinds (Insert/Home/PageUp/PageDown/Up/Down) are
/// user-fixed and unchanged by the move.
/// </para>
/// <para>
/// The increment/decrement/toggle callbacks are thin adapters over the two
/// controllers (which own the actual policy), so the controllers are taken
/// as non-null constructor dependencies — they exist by the time
/// <c>StartClientSide</c> wires this up.
/// </para>
/// <para>
/// The reload path is deliberately split. The INPUT concerns live here: the
/// server-enforcement guard, the one-shot "reload blocked" toast, and the
/// hold-once gate. The state ORCHESTRATION (re-load options from disk,
/// invalidate caches, re-apply through the controllers, emit the success
/// line) is owned by the composition root and passed in as
/// <see cref="reloadConfig"/>. That keeps this class cohesive — input maps
/// to action — instead of absorbing the ConfigStore / probe / writer /
/// controller dependency cluster the reload touches.
/// </para>
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

        // The controllers own the policy: stepController.Toggle flips
        // stepUpEnabled, persists, re-applies, and emits the toast.
        // elevateController.ApplyNow then re-pushes the speed against
        // the new IsEnabled state.
        stepController.Toggle();
        elevateController.ApplyNow(stepController.IsEnabled);
        return true;
    }

    private bool OnReloadConfig(KeyCombination comb)
    {
        // Per Phase 3b, EnforcementState.IsEnforced is just a flag read
        // on the loaded options. Read it directly here.
        if (StepUpOptions.Current?.ServerEnforceSettings == true && !StepUpOptions.Current.AllowClientConfigReload)
        {
            if (toasts.ReloadBlocked.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("server-enforced"))} – {ChatFormatting.Muted(ChatFormatting.L("reload-blocked"))}");
            return false;
        }
        // Reset position deliberately mirrors the pre-Phase-4
        // hasShownMaxMessage = false at the same point: after the
        // enforcement guard passes, before the hold-guard. So the
        // ReloadBlocked toast re-arms as soon as enforcement permits
        // reload again, not only after a successful reload.
        toasts.ReloadBlocked.Reset();

        if (reloadHeld?.TryFire() != true)
        {
            return false;
        }

        // State orchestration is owned by the composition root.
        reloadConfig();
        return true;
    }
}
