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
/// Phase 7a extracts this from <c>StepUpAdvancedModSystem</c>. Behavior
/// is preserved — the same enforcement gate, the same clamp shape, the
/// same toast suppression pattern. The pure compute is on the static
/// <see cref="ComputeDesiredElevateFactor"/> for test isolation; the
/// instance methods wrap the side effects (player/physics lookup,
/// toast emission, config persistence, writer dispatch).
/// </para>
/// <para>
/// <b>Push-driven, not tick-driven.</b> Unlike step height, which gets
/// re-applied every 50 ms via a tick listener, the elevate factor
/// changes infrequently and is pushed on the events that change it:
/// user actions (<see cref="Increase"/> / <see cref="Decrease"/>),
/// stepup toggle, server config receive, and config reload. The writer's
/// per-field idempotency cache (with <c>1e-6</c> threshold) prevents
/// redundant reflected writes when the value hasn't actually moved.
/// </para>
/// <para>
/// <b>stepUpEnabled as a method parameter</b> rather than internal state:
/// the controller doesn't own the toggle, the step-height controller will
/// (Step 3 of Phase 7a). Passing it per call avoids mirroring state
/// across two controllers and avoids a back-reference to the height
/// controller during Step 2.
/// </para>
/// </remarks>
internal sealed class ElevateFactorController
{
    private readonly ICoreClientAPI capi;
    private readonly PhysicsFieldWriter writer;
    private readonly MessageDebouncer toasts;

    // Closure over ModSystem.QueueConfigSave (the 200 ms debounced write
    // path with idempotency gate). Phase 7b is expected to eliminate the
    // static queue state on ModSystem; this delegate is the interim glue.
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
    /// Pure compute for the desired elevate-factor write value. Pinned by
    /// <c>ElevateFactorControllerTests</c>. The priority order is:
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><description>If <paramref name="stepUpEnabled"/> is <c>false</c>,
    /// return <c><paramref name="defaultSpeed"/> × 0.05</c> regardless of
    /// every other input — disabling stepup resets the player's speed
    /// modifier to the configured default.</description></item>
    /// <item><description>Otherwise, pick the logical speed: when enforced,
    /// the server-authoritative <paramref name="optionsStepSpeed"/>; when
    /// not, the user's runtime <paramref name="currentElevateFactor"/>.</description></item>
    /// <item><description>Clamp through <see cref="ElevateFactorMath.Clamp"/> —
    /// client floor always; server min/max only when enforced.</description></item>
    /// <item><description>Multiply by <c>0.05</c> (the VS physics
    /// convention for converting a per-second factor to per-tick).</description></item>
    /// </list>
    /// </remarks>
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
    /// changed), <c>false</c> otherwise. Behavior mirrors the pre-Phase-7a
    /// <c>OnIncreaseElevateFactor</c> on the ModSystem.
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
    /// Suppresses the redundant generic "Speed » X" toast on the press that
    /// lands at the client floor — see the <c>atFloor</c> pattern from the
    /// Phase 4 polish (CHANGELOG entry on redundant double-toasts).
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
            // True when this descent lands us at (or below) the client
            // hard floor. Drives both the at-min toast and the suppression
            // of the redundant generic "Speed » 0.7" update on the same
            // press — see CHANGELOG "Phase 4 polish".
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
