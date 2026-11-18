using System;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Domain.Physics;
using StepUpAdvanced.Infrastructure.Input;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Application;

/// <summary>
/// Push-driven owner of the player's step speed. Owns
/// <see cref="currentElevateFactor"/> and the two hotkey-handler bodies for
/// the speed axis, and publishes the effective speed to
/// <see cref="StepSpeedState"/> for the Harmony getters.
/// </summary>
/// <remarks>
/// <para>
/// <b>Push-driven, not tick-driven.</b> The effective speed only changes on
/// user actions, toggle, server config receive, config reload, and leaving
/// Basic mode, so it is re-resolved on those events. Every one of them ends
/// in <see cref="ApplyNow"/>.
/// </para>
/// <para>
/// <b><c>stepUpEnabled</c> is a method parameter</b> rather than internal
/// state — the toggle is owned by <see cref="StepHeightController"/>, and
/// taking it per call avoids mirroring state across two controllers.
/// </para>
/// </remarks>
internal sealed class ElevateFactorController
{
    private readonly ICoreClientAPI capi;
    private readonly MessageDebouncer toasts;

    private readonly Action persistConfig;

    /// <summary>
    /// Runtime target step speed. Mutated by user actions and by
    /// <see cref="OnConfigReloaded"/>; consulted on every
    /// <see cref="ApplyNow"/>. Initialized in
    /// <see cref="InitializeFromConfig"/> at StartClientSide.
    /// </summary>
    private float currentElevateFactor;

    public ElevateFactorController(
        ICoreClientAPI capi,
        MessageDebouncer toasts,
        Action persistConfig)
    {
        this.capi = capi;
        this.toasts = toasts;
        this.persistConfig = persistConfig;
    }

    /// <summary>
    /// Read-only view of the player's configured speed. Exposed for diagnostic
    /// callers; <see cref="ApplyNow"/> is the only path that changes what
    /// physics uses.
    /// </summary>
    public float Current => currentElevateFactor;

    /// <summary>
    /// Reads the persisted step speed from <see cref="StepUpOptions.Current"/>,
    /// clamps it through <see cref="ElevateFactorMath.Clamp"/>, and stores
    /// the clamped value. If the clamp produced a different value than was
    /// loaded, the corrected value is written back to options and a
    /// debounced save is queued. Returns <c>true</c> when the load needed
    /// correction so the caller can fold this into a single "normalized"
    /// log line covering both axes.
    /// </summary>
    public bool InitializeFromConfig()
    {
        // Capture once + guard once: subsequent uses below are flow-narrowed
        // non-nullable, which silences the CS8602 on the write-back assignment
        // without scattering null checks across the method.
        var opts = StepUpOptions.Current;
        if (opts == null) return false;

        float loaded = opts.StepSpeed;
        float clamped = ClampSpeed(loaded);
        currentElevateFactor = clamped;

        if (clamped == loaded) return false;

        opts.StepSpeed = clamped;
        persistConfig();
        return true;
    }

    /// <summary>
    /// Resolves the effective speed from the current inputs and publishes it
    /// to <see cref="StepSpeedState"/>. Clamping here, not only in the hotkey
    /// paths, is what makes a server's speed caps apply from the moment its
    /// config arrives, whatever the player's file says.
    /// </summary>
    public void ApplyNow(bool stepUpEnabled)
    {
        var opts = StepUpOptions.Current;
        if (opts == null) return;

        StepSpeedState.Publish(ElevateFactorMath.ResolveEffective(
            BasicModeState.Active,
            stepUpEnabled,
            currentElevateFactor,
            IsEnforced,
            opts.ServerMinStepSpeed,
            opts.ServerMaxStepSpeed));
    }

    /// <summary>
    /// Hotkey body for the "increase speed" action (Up arrow by default).
    /// Returns <c>true</c> if the press was consumed (toast emitted or value
    /// changed), <c>false</c> otherwise. 
    /// </summary>
    public bool Increase(bool stepUpEnabled)
    {
        if (IsEnforced && !StepUpOptions.Current.AllowClientChangeStepSpeed)
        {
            if (toasts.SpeedEnforced.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("server-enforced"))} – {ChatFormatting.Muted(ChatFormatting.L("speed-change-blocked"))}");
            return false;
        }
        toasts.SpeedEnforced.Reset();

        if (IsEnforced && Math.Abs(currentElevateFactor - StepUpOptions.Current.ServerMaxStepSpeed) < 0.01f)
        {
            if (toasts.SpeedAtMax.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("max-speed"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{StepUpOptions.Current.ServerMaxStepSpeed:0.0}")}");
            return false;
        }
        toasts.SpeedAtMax.Reset();

        float previous = currentElevateFactor;
        currentElevateFactor += Math.Max(StepUpOptions.Current.StepSpeedIncrement, 0.01f);
        currentElevateFactor = ClampSpeed(currentElevateFactor);

        if (currentElevateFactor > previous)
        {
            StepUpOptions.Current.StepSpeed = currentElevateFactor;
            persistConfig();
            ApplyNow(stepUpEnabled);
            ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Bold(ChatFormatting.L("speed"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{currentElevateFactor:0.0}")}");
        }
        return true;
    }

    /// <summary>
    /// Hotkey body for the "decrease speed" action (Down arrow by default).
    /// Suppresses the redundant generic "Speed » X" toast when the press
    /// lands at the client floor — avoids two toasts on the same keypress.
    /// </summary>
    public bool Decrease(bool stepUpEnabled)
    {
        if (IsEnforced && !StepUpOptions.Current.AllowClientChangeStepSpeed)
        {
            if (toasts.SpeedEnforced.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("server-enforced"))} – {ChatFormatting.Muted(ChatFormatting.L("speed-change-blocked"))}");
            return false;
        }
        toasts.SpeedEnforced.Reset();

        if (IsEnforced && Math.Abs(currentElevateFactor - StepUpOptions.Current.ServerMinStepSpeed) < 0.01f)
        {
            if (toasts.SpeedAtMin.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("min-speed"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{StepUpOptions.Current.ServerMinStepSpeed:0.0}")}");
            return false;
        }
        toasts.SpeedAtMin.Reset();

        float previous = currentElevateFactor;
        currentElevateFactor -= Math.Max(StepUpOptions.Current.StepSpeedIncrement, 0.01f);
        currentElevateFactor = ClampSpeed(currentElevateFactor);

        if (currentElevateFactor < previous)
        {
            // True when this descent lands at (or below) the client hard floor.
            // Drives the at-min toast and suppresses the redundant "Speed » 0.7" update.
            bool atFloor = currentElevateFactor <= ElevateFactorMath.ClientMin;
            if (atFloor && toasts.SpeedAtMin.TryShow())
            {
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("min-speed"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{ElevateFactorMath.ClientMin:0.0}")}");
            }
            StepUpOptions.Current.StepSpeed = currentElevateFactor;
            persistConfig();
            ApplyNow(stepUpEnabled);
            if (!atFloor)
            {
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Bold(ChatFormatting.L("speed"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{currentElevateFactor:0.0}")}");
            }
        }
        return true;
    }

    /// <summary>
    /// Refreshes <see cref="currentElevateFactor"/> from
    /// <c>StepUpOptions.Current.StepSpeed</c> and re-applies. Called
    /// from <c>OnReloadConfig</c> after the file is reloaded.
    /// </summary>
    public void OnConfigReloaded(bool stepUpEnabled)
    {
        currentElevateFactor = StepUpOptions.Current?.StepSpeed ?? currentElevateFactor;
        ApplyNow(stepUpEnabled);
    }

    // Server caps only count once a server has actually sent them. Before
    // that (Basic mode) the server-owned fields hold defaults, not this
    // server's limits, and clamping the player's settings to them at startup
    // would save those defaults into the player's own file.
    private bool IsEnforced
        => !BasicModeState.Active
           && EnforcementState.IsEnforced(EnumAppSide.Client, capi.IsSinglePlayer, StepUpOptions.Current);

    private float ClampSpeed(float speed)
        => ElevateFactorMath.Clamp(
            speed,
            IsEnforced,
            StepUpOptions.Current.ServerMinStepSpeed,
            StepUpOptions.Current.ServerMaxStepSpeed);
}
