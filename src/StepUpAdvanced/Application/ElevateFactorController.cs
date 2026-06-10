using System;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Domain.Physics;
using StepUpAdvanced.Infrastructure.Input;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace StepUpAdvanced.Application;

/// <summary>
/// Push-driven owner of the player's step-speed (elevate factor). Owns
/// <see cref="currentElevateFactor"/>, the elevate-factor warned-once
/// diagnostic flag, and the two hotkey-handler bodies for the speed
/// axis. Calls through <see cref="PhysicsFieldWriter"/> for the actual
/// reflected write.
/// </summary>
/// <remarks>
/// <para>
/// <b>Push-driven, not tick-driven.</b> The elevate factor changes
/// infrequently and is pushed on events that change it: user actions,
/// toggle, server config receive, and config reload. The writer's
/// idempotency check prevents redundant reflected writes.
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
    private readonly PhysicsFieldWriter writer;
    private readonly MessageDebouncer toasts;

    private readonly Action persistConfig;

    /// <summary>
    /// Runtime target step speed. Mutated by user actions and by
    /// <see cref="OnConfigReloaded"/>; consulted on every
    /// <see cref="ApplyNow"/>. Initialized in
    /// <see cref="InitializeFromConfig"/> at StartClientSide.
    /// </summary>
    private float currentElevateFactor;

    /// <summary>
    /// Once-warning gate for the rare case where the VS API renamed the
    /// <c>elevateFactor</c> field and the writer can't resolve it on the
    /// runtime physics type.
    /// </summary>
    private bool elevateWarnedOnce;

    public ElevateFactorController(
        ICoreClientAPI capi,
        PhysicsFieldWriter writer,
        MessageDebouncer toasts,
        Action persistConfig)
    {
        this.capi = capi;
        this.writer = writer;
        this.toasts = toasts;
        this.persistConfig = persistConfig;
    }

    /// <summary>
    /// Read-only view of the current target speed. Exposed for diagnostic
    /// callers; <see cref="ApplyNow"/> is the only path that pushes to
    /// physics.
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
    /// Pushes the current desired elevate factor to the player's physics
    /// behavior via <see cref="PhysicsFieldWriter"/>. Emits the
    /// "field-missing" warning at most once if the writer can't resolve
    /// the field on the runtime physics type.
    /// </summary>
    public void ApplyNow(bool stepUpEnabled)
    {
        var player = capi.World?.Player;
        if (player?.Entity == null) return;

        var physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null) return;

        double desired = ComputeDesiredElevateFactor(
            stepUpEnabled,
            isEnforced: IsEnforced,
            currentElevateFactor: currentElevateFactor,
            optionsStepSpeed: StepUpOptions.Current.StepSpeed,
            defaultSpeed: StepUpOptions.Current.DefaultSpeed,
            serverMinSpeed: StepUpOptions.Current.ServerMinStepSpeed,
            serverMaxSpeed: StepUpOptions.Current.ServerMaxStepSpeed);

        if (writer.WriteElevateFactor(physicsBehavior, desired)) return;

        if (!elevateWarnedOnce)
        {
            ModLog.Warning(capi, "Failed to resolve elevateFactor field on physics behavior; speed adjustments disabled.");
            elevateWarnedOnce = true;
        }
    }

    /// <summary>
    /// Pure compute for the desired elevate-factor write value. Priority:
    /// if step-up is disabled, return <c>defaultSpeed × 0.05</c>; otherwise
    /// clamp the logical speed (enforced uses the server value, otherwise
    /// the runtime value) and multiply by 0.05 (VS's per-tick convention).
    /// </summary>
    internal static double ComputeDesiredElevateFactor(
        bool stepUpEnabled,
        bool isEnforced,
        float currentElevateFactor,
        float optionsStepSpeed,
        float defaultSpeed,
        float serverMinSpeed,
        float serverMaxSpeed)
    {
        float logicalSpeed = isEnforced ? optionsStepSpeed : currentElevateFactor;
        logicalSpeed = ElevateFactorMath.Clamp(logicalSpeed, isEnforced, serverMinSpeed, serverMaxSpeed);
        return (stepUpEnabled ? logicalSpeed : defaultSpeed) * 0.05;
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
    /// from <c>OnReloadConfig</c> after the file is reloaded and the
    /// writer's cache is invalidated.
    /// </summary>
    public void OnConfigReloaded(bool stepUpEnabled)
    {
        currentElevateFactor = StepUpOptions.Current?.StepSpeed ?? currentElevateFactor;
        ApplyNow(stepUpEnabled);
    }

    private bool IsEnforced
        => EnforcementState.IsEnforced(EnumAppSide.Client, capi.IsSinglePlayer, StepUpOptions.Current);

    private float ClampSpeed(float speed)
        => ElevateFactorMath.Clamp(
            speed,
            IsEnforced,
            StepUpOptions.Current.ServerMinStepSpeed,
            StepUpOptions.Current.ServerMaxStepSpeed);
}
