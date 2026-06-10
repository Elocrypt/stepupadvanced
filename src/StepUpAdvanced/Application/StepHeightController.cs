using System;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using StepUpAdvanced.Domain.Physics;
using StepUpAdvanced.Domain.Probes;
using StepUpAdvanced.Infrastructure.Input;
using StepUpAdvanced.Infrastructure.Probes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace StepUpAdvanced.Application;

/// <summary>
/// Per-tick owner of the player's step height. Owns
/// <see cref="currentStepHeight"/>, <see cref="stepUpEnabled"/>, the three
/// field-resolution diagnostic flags, the blacklist proximity check, and
/// the ceiling-guard composition. Calls through
/// <see cref="PhysicsFieldWriter"/> for the reflected write.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two static pure functions, not one:</b> the ceiling-guard reduction
/// needs the clearance value, which is bounded by the post-clamp base height.
/// Splitting at that seam keeps each function independently testable.
/// </para>
/// <para>
/// <b><see cref="ApplyForTick"/> runs every 50 ms</b> via a tick listener
/// and is also called push-driven from hotkey actions, toggle, server config
/// receive, and reload. The writer's idempotency check prevents redundant
/// reflected writes when the value hasn't changed.
/// </para>
/// </remarks>
internal sealed class StepHeightController
{
    /// <summary>
    /// VS's hard-coded baseline player step height with no mod active.
    /// Deliberately not <see cref="StepHeightClamp.Default"/> — they
    /// happen to be the same value but represent different concepts.
    /// This is "the game's default behavior we're resetting to when the
    /// mod is toggled off"; <c>StepHeightClamp.Default</c> is "our
    /// fallback when options is null."
    /// </summary>
    private const float VsBaselineStepHeight = 0.6f;

    /// <summary>
    /// Ceiling-clearance threshold (blocks) below which step height collapses
    /// to <see cref="StepUpOptions.DefaultHeight"/>. Tuned to avoid
    /// hard-snapping into a 1-block overhang on stair geometry.
    /// </summary>
    private const float CeilingCollapseThreshold = 0.75f;

    private readonly ICoreClientAPI capi;
    private readonly WorldProbe probe;
    private readonly PhysicsFieldWriter writer;
    private readonly MessageDebouncer toasts;

    private readonly Action persistConfig;

    /// <summary>
    /// Runtime target step height. Mutated by Increase/Decrease and by
    /// <see cref="OnConfigReloaded"/>; consulted on every
    /// <see cref="ApplyForTick"/>.
    /// </summary>
    private float currentStepHeight;

    /// <summary>
    /// Master on/off for the entire mod (height AND speed). Toggled via
    /// <see cref="Toggle"/>; consulted by <see cref="ApplyForTick"/> and
    /// by <c>ElevateFactorController.ApplyNow</c> through
    /// <see cref="IsEnabled"/>.
    /// </summary>
    private bool stepUpEnabled;

    private bool stepHeightWarnedOnce;
    private bool warnedPlayerNullOnce;
    private bool warnedEntityNullOnce;
    private bool warnedPhysNullOnce;

    public StepHeightController(
        ICoreClientAPI capi,
        WorldProbe probe,
        PhysicsFieldWriter writer,
        MessageDebouncer toasts,
        Action persistConfig)
    {
        this.capi = capi;
        this.probe = probe;
        this.writer = writer;
        this.toasts = toasts;
        this.persistConfig = persistConfig;
    }

    /// <summary>Read-only view of the runtime target step height.</summary>
    public float Current => currentStepHeight;

    /// <summary>
    /// Read-only view of the master on/off. Consulted by
    /// <c>ElevateFactorController.ApplyNow</c> on the cross-controller
    /// re-apply path (toggle, receive, reload).
    /// </summary>
    public bool IsEnabled => stepUpEnabled;

    /// <summary>
    /// Reads the persisted step height + stepup flag from
    /// <see cref="StepUpOptions.Current"/>, clamps the height through
    /// <see cref="StepHeightClamp.Clamp"/>, and stores both. If the clamp
    /// produced a different value, the corrected value is written back to
    /// options and a debounced save is queued. Returns <c>true</c> when
    /// the load needed correction so the caller can fold this into a
    /// single shared "normalized" log line.
    /// </summary>
    public bool InitializeFromConfig()
    {
        // Capture once so subsequent property reads are flow-narrowed non-nullable.
        var opts = StepUpOptions.Current;
        if (opts == null) return false;

        stepUpEnabled = opts.StepUpEnabled;

        float loaded = opts.StepHeight;
        float clamped = ClampHeight(loaded);
        currentStepHeight = clamped;

        if (clamped == loaded) return false;

        opts.StepHeight = clamped;
        persistConfig();
        return true;
    }

    /// <summary>
    /// Per-tick worker. Resolves player and physics behavior, computes
    /// the desired step height (priority composition + optional ceiling
    /// guard), and pushes through the writer. Skips entirely when
    /// <see cref="StepUpOptions.SpeedOnlyMode"/> is on (XSkills mode).
    /// </summary>
    public void ApplyForTick()
    {
        // Capture+guard the options once; the subsequent cfg.X accesses
        // below are then flow-narrowed non-nullable.
        var cfg = StepUpOptions.Current;
        if (cfg == null) return;
        if (cfg.SpeedOnlyMode) return;

        // Locals typed as nullable to match VS's nullable-returning
        // accessors (capi.World?.Player and GetBehavior<T>() both return
        // null-possible). The per-null guards below promote them to
        // non-null for the rest of the method.
        IClientPlayer? player = capi.World?.Player;
        if (player == null)
        {
            if (!warnedPlayerNullOnce)
            {
                ModLog.Warning(capi, "Player object is null. Cannot apply step height.");
                warnedPlayerNullOnce = true;
            }
            return;
        }
        warnedPlayerNullOnce = false;

        // Entity and Pos can be transiently null across a teleport, dimension
        // change, or on the first ticks after joining. Guard before any Entity
        // deref — GetBehavior, Pos, and Motion are all downstream of this check.
        if (player.Entity?.Pos == null)
        {
            if (!warnedEntityNullOnce)
            {
                ModLog.Warning(capi, "Player entity (or its position) is null this tick. Skipping step height.");
                warnedEntityNullOnce = true;
            }
            return;
        }
        warnedEntityNullOnce = false;

        EntityBehaviorControlledPhysics? physicsBehavior = player.Entity.GetBehavior<EntityBehaviorControlledPhysics>();
        if (physicsBehavior == null)
        {
            if (!warnedPhysNullOnce)
            {
                ModLog.Warning(capi, "Physics behavior is missing on player entity. Cannot apply step height.");
                warnedPhysNullOnce = true;
            }
            return;
        }
        warnedPhysNullOnce = false;

        bool isEnforced = IsEnforced;

        // Sprint-only / disable-while-sneaking / disable-while-airborne gate
        // (height axis). Closed gate collapses the composed height to the
        // vanilla baseline this tick. Read live control + ground state off the
        // local player; Controls is non-null on a player EntityAgent and Entity
        // was guarded above.
        var controls = player.Entity.Controls;
        bool gateOpen = StepUpGate.ShouldApplyStepUp(
            cfg.SprintOnlyStepUp,
            cfg.DisableStepUpWhileSneaking,
            cfg.DisableStepUpWhileAirborne,
            controls.Sprint,
            controls.Sneak,
            player.Entity.OnGround,
            player.Entity.Swimming);

        bool nearBlacklistedBlock = IsNearBlacklistedBlock(player);
        float baseHeight = ComputeBaseStepHeight(
            stepUpEnabled,
            gateOpen,
            nearBlacklistedBlock,
            currentStepHeight,
            cfg.DefaultHeight,
            isEnforced,
            cfg.ServerMinStepHeight,
            cfg.ServerMaxStepHeight);

        // Ceiling guard: clearance is bounded by baseHeight, so it must be
        // computed after the base height is finalized.
        float finalHeight = baseHeight;
        if (cfg.CeilingGuardEnabled && baseHeight > 0f)
        {
            float clearance = DistanceToCeiling(player, baseHeight);
            finalHeight = ApplyCeilingGuard(baseHeight, clearance, cfg.DefaultHeight);
        }

        if (writer.WriteStepHeight(physicsBehavior, finalHeight)) return;

        if (!stepHeightWarnedOnce)
        {
            ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Err(ChatFormatting.L("error.stepheight-field-missing"))}");
            stepHeightWarnedOnce = true;
        }
    }

    /// <summary>
    /// Pure compute: priority composition without ceiling guard. Tested
    /// by <c>StepHeightControllerTests</c>. Priority order, top to
    /// bottom:
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><description><paramref name="stepUpEnabled"/> = <c>false</c>
    /// → return <see cref="VsBaselineStepHeight"/> (0.6) regardless of
    /// every other input. Toggling off resets to VS's default step
    /// height.</description></item>
    /// <item><description><paramref name="gateOpen"/> = <c>false</c> →
    /// return <see cref="VsBaselineStepHeight"/> (0.6). The sprint-only /
    /// disable-while-sneaking gate (<c>StepUpGate.ShouldApplyStepUp</c>) is
    /// closed this tick, so fall back to vanilla step behavior. Overrides
    /// blacklist proximity and the enforcement clamp — when the gate is
    /// closed the mod simply isn't raising the step height at
    /// all.</description></item>
    /// <item><description><paramref name="nearBlacklistedBlock"/> =
    /// <c>true</c> → return <paramref name="defaultHeight"/>. Overrides
    /// the enforcement clamp; user gets the configured "safe" height
    /// near sensitive blocks like ladders.</description></item>
    /// <item><description>Otherwise → clamp
    /// <paramref name="currentStepHeight"/> through
    /// <see cref="StepHeightClamp.Clamp"/> (client floor always; server
    /// min/max only when enforced).</description></item>
    /// </list>
    /// </remarks>
    internal static float ComputeBaseStepHeight(
        bool stepUpEnabled,
        bool gateOpen,
        bool nearBlacklistedBlock,
        float currentStepHeight,
        float defaultHeight,
        bool isEnforced,
        float serverMinHeight,
        float serverMaxHeight)
    {
        if (!stepUpEnabled) return VsBaselineStepHeight;
        if (!gateOpen) return VsBaselineStepHeight;
        if (nearBlacklistedBlock) return defaultHeight;
        return StepHeightClamp.Clamp(currentStepHeight, isEnforced, serverMinHeight, serverMaxHeight);
    }

    /// <summary>
    /// Pure compute: ceiling-guard reduction. Tested by
    /// <c>StepHeightControllerTests</c>. Branches:
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><paramref name="baseHeight"/> ≤ 0 → return as-is
    /// (no guard applies; the player isn't stepping up).</description></item>
    /// <item><description><paramref name="clearance"/> ≤
    /// <see cref="CeilingCollapseThreshold"/> (0.75 blocks) → collapse to
    /// <c>min(baseHeight, defaultHeight)</c>. Tight overhead: don't try
    /// to be clever, fall back to the configured default.</description></item>
    /// <item><description><paramref name="clearance"/> &lt;
    /// <paramref name="baseHeight"/> → use <paramref name="clearance"/>.
    /// Some room but not enough for the full requested step.</description></item>
    /// <item><description>Otherwise → return <paramref name="baseHeight"/>
    /// unchanged. Plenty of overhead.</description></item>
    /// </list>
    /// </remarks>
    internal static float ApplyCeilingGuard(float baseHeight, float clearance, float defaultHeight)
    {
        if (baseHeight <= 0f) return baseHeight;
        if (clearance <= CeilingCollapseThreshold) return Math.Min(baseHeight, defaultHeight);
        if (clearance < baseHeight) return clearance;
        return baseHeight;
    }

    /// <summary>
    /// Hotkey body for the "increase height" action (PageUp by default).
    /// Returns <c>true</c> if the press was consumed; <c>false</c> otherwise.
    /// </summary>
    public bool Increase()
    {
        // Capture once + guard once. Same pattern as InitializeFromConfig
        // and ApplyForTick: subsequent opts.X reads are flow-narrowed
        // non-nullable and don't trigger CS8602.
        var opts = StepUpOptions.Current;
        if (opts == null) return false;

        if (opts.SpeedOnlyMode)
        {
            if (toasts.HeightSpeedOnlyMode.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("speed-only-mode"))} – {ChatFormatting.Muted(ChatFormatting.L("height-controls-disabled"))}");
            return false;
        }
        toasts.HeightSpeedOnlyMode.Reset();

        if (IsEnforced && !opts.AllowClientChangeStepHeight)
        {
            if (toasts.HeightEnforced.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("server-enforced"))} – {ChatFormatting.Muted(ChatFormatting.L("height-change-blocked"))}");
            return false;
        }
        toasts.HeightEnforced.Reset();

        if (IsEnforced && Math.Abs(currentStepHeight - opts.ServerMaxStepHeight) < 0.01f)
        {
            if (toasts.HeightAtMax.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("max-height"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{opts.ServerMaxStepHeight:0.0} blocks")}");
            return false;
        }
        toasts.HeightAtMax.Reset();

        float previous = currentStepHeight;
        currentStepHeight += Math.Max(opts.StepHeightIncrement, 0.1f);
        currentStepHeight = ClampHeight(currentStepHeight);

        if (currentStepHeight > previous)
        {
            opts.StepHeight = currentStepHeight;
            persistConfig();
            ApplyForTick();
            ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Bold(ChatFormatting.L("height"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{currentStepHeight:0.0} blocks")}");
        }
        return true;
    }

    /// <summary>
    /// Hotkey body for the "decrease height" action (PageDown by default).
    /// Suppresses the redundant generic "Height » X" toast when the press
    /// lands at the client floor.
    /// </summary>
    public bool Decrease()
    {
        var opts = StepUpOptions.Current;
        if (opts == null) return false;

        if (opts.SpeedOnlyMode)
        {
            if (toasts.HeightSpeedOnlyMode.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("speed-only-mode"))} – {ChatFormatting.Muted(ChatFormatting.L("height-controls-disabled"))}");
            return false;
        }
        toasts.HeightSpeedOnlyMode.Reset();

        if (IsEnforced && !opts.AllowClientChangeStepHeight)
        {
            if (toasts.HeightEnforced.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("server-enforced"))} – {ChatFormatting.Muted(ChatFormatting.L("height-change-blocked"))}");
            return false;
        }
        toasts.HeightEnforced.Reset();

        if (IsEnforced && Math.Abs(currentStepHeight - opts.ServerMinStepHeight) < 0.01f)
        {
            if (toasts.HeightAtMin.TryShow())
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("min-height"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{Math.Max(StepHeightClamp.ClientMin, opts.ServerMinStepHeight):0.0} blocks")}");
            return false;
        }
        toasts.HeightAtMin.Reset();

        float previous = currentStepHeight;
        currentStepHeight -= Math.Max(opts.StepHeightIncrement, 0.1f);
        currentStepHeight = ClampHeight(currentStepHeight);

        if (currentStepHeight < previous)
        {
            // True when this descent reaches the client hard floor: drives
            // the at-min toast and suppresses the redundant "Height » 0.6" update.
            bool atFloor = currentStepHeight <= StepHeightClamp.ClientMin;
            if (atFloor && toasts.HeightAtMin.TryShow())
            {
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Warn(ChatFormatting.L("min-height"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{Math.Max(StepHeightClamp.ClientMin, opts.ServerMinStepHeight):0.0} blocks")}");
            }
            opts.StepHeight = currentStepHeight;
            persistConfig();
            ApplyForTick();
            if (!atFloor)
            {
                ChatFormatting.Client(capi, $"{ChatFormatting.Tag} {ChatFormatting.Bold(ChatFormatting.L("height"))} {ChatFormatting.Arrow} {ChatFormatting.Val($"{currentStepHeight:0.0} blocks")}");
            }
        }
        return true;
    }

    /// <summary>
    /// Flips <see cref="stepUpEnabled"/>, persists, and emits the enable/disable toast.
    /// The caller re-applies the speed axis after calling this.
    /// </summary>
    public void Toggle()
    {
        stepUpEnabled = !stepUpEnabled;
        StepUpOptions.Current.StepUpEnabled = stepUpEnabled;
        persistConfig();
        ApplyForTick();
        ChatFormatting.Client(capi, stepUpEnabled
            ? $"{ChatFormatting.Tag} {ChatFormatting.Ok(ChatFormatting.L("stepup-enabled"))}"
            : $"{ChatFormatting.Tag} {ChatFormatting.Err(ChatFormatting.L("stepup-disabled"))}");
    }

    /// <summary>
    /// Refreshes <see cref="currentStepHeight"/> and
    /// <see cref="stepUpEnabled"/> from
    /// <see cref="StepUpOptions.Current"/>, then re-applies. Called from
    /// the ModSystem's Home-key handler after the file is reloaded and
    /// the writer's cache is invalidated.
    /// </summary>
    public void OnConfigReloaded()
    {
        currentStepHeight = StepUpOptions.Current?.StepHeight ?? currentStepHeight;
        stepUpEnabled = StepUpOptions.Current?.StepUpEnabled ?? stepUpEnabled;
        ApplyForTick();
    }

    /// <summary>
    /// Checks whether the player is near a blacklisted block. The server list
    /// is only included when enforcement is active.
    /// </summary>
    private bool IsNearBlacklistedBlock(IClientPlayer player)
    {
        // Server list is excluded when enforcement is off.
        var serverList = IsEnforced ? StepUpOptions.Current?.BlockBlacklist : null;
        var clientList = BlockBlacklistOptions.Current?.BlockCodes;

        return probe.NearBlacklistedBlock(
            player.Entity.World,
            player.Entity.Pos.AsBlockPos,
            player.Entity.Pos.Motion,
            serverList,
            clientList);
    }

    /// <summary>
    /// Combines the "here" clearance check with the forward-column check.
    /// World queries live on <see cref="WorldProbe"/>; math on
    /// <see cref="CeilingProbeMath"/>; this method holds the policy.
    /// </summary>
    private float DistanceToCeiling(IClientPlayer player, float requestedStep)
    {
        var world = player.Entity.World;
        var basePos = player.Entity.Pos.AsBlockPos;
        var cfg = StepUpOptions.Current;

        float hereClear = probe.DistanceToCeilingAt(
            world, basePos, requestedStep, startDy: 1, headroomPad: cfg.CeilingHeadroomPad);

        if (!cfg.ForwardProbeCeiling || cfg.ForwardProbeDistance <= 0)
            return hereClear;

        double yaw = player.Entity.Pos.Yaw;
        bool supportedAhead = probe.HasLandingSupport(world, basePos, yaw, cfg.ForwardProbeDistance, requestedStep);
        float tinySafe = Math.Max(0.25f, cfg.DefaultHeight);
        if (cfg.RequireForwardSupport && !supportedAhead) return Math.Min(hereClear, tinySafe);

        float entHeight = player.Entity.CollisionBox.Y2 - player.Entity.CollisionBox.Y1;
        var (yFrom, yTopInclusive) = CeilingProbeMath.LandingClearanceRange(
            basePos.Y, requestedStep, entHeight, cfg.CeilingHeadroomPad);
        if (yTopInclusive < yFrom) return hereClear;

        int columnCount = probe.BuildForwardColumns(basePos, yaw, cfg.ForwardProbeDistance, cfg.ForwardProbeSpan);
        bool blockedAll = true;
        for (int i = 0; i < columnCount; i++)
        {
            var col = probe.GetColumn(i);
            if (!probe.ColumnHasSolid(world, col, yFrom, yTopInclusive + 1))
            {
                blockedAll = false;
                break;
            }
        }
        return blockedAll ? Math.Min(hereClear, tinySafe) : hereClear;
    }

    private bool IsEnforced
        => EnforcementState.IsEnforced(EnumAppSide.Client, capi.IsSinglePlayer, StepUpOptions.Current);

    private float ClampHeight(float height)
        => StepHeightClamp.Clamp(
            height,
            IsEnforced,
            StepUpOptions.Current.ServerMinStepHeight,
            StepUpOptions.Current.ServerMaxStepHeight);
}
