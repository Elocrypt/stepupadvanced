using FluentAssertions;
using StepUpAdvanced.Application;
using StepUpAdvanced.Domain.Physics;
using Xunit;

namespace StepUpAdvanced.Tests.Application;

/// <summary>
/// Pins the two pure compute methods on
/// <see cref="StepHeightController"/>:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><see cref="StepHeightController.ComputeBaseStepHeight"/>
/// — priority composition without ceiling guard.</description></item>
/// <item><description><see cref="StepHeightController.ApplyCeilingGuard"/>
/// — ceiling-guard reduction applied to an already-composed base height.</description></item>
/// </list>
/// <para>
/// The split was deliberate: the ceiling guard's <c>clearance</c> input
/// depends on the post-clamp base height (the world probe is bounded by
/// the requested step), so the call ordering is
/// <c>baseHeight → clearance(baseHeight) → final</c>. Each phase tests
/// in isolation.
/// </para>
/// </remarks>
public class StepHeightControllerTests
{
    private const float DefaultHeight = 0.6f;
    private const float ServerMin = 1.0f;
    private const float ServerMax = 2.0f;

    // ─── ComputeBaseStepHeight: priority order pins ─────────────────────

    /// <summary>
    /// <c>stepUpEnabled = false</c> resets to VS's hard-coded 0.6 baseline
    /// — overrides every other input including enforcement and blacklist
    /// proximity.
    /// </summary>
    [Fact]
    public void Base_Disabled_ReturnsVsBaselineHeight()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: false,
            gateOpen: true,
            nearBlacklistedBlock: false,
            currentStepHeight: 1.5f,
            defaultHeight: DefaultHeight,
            isEnforced: false,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(0.6f);
    }

    [Fact]
    public void Base_Disabled_OverridesBlacklist()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: false,
            gateOpen: true,
            nearBlacklistedBlock: true,
            currentStepHeight: 1.5f,
            defaultHeight: 0.9f, // different from VS baseline
            isEnforced: false,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(0.6f);
    }

    [Fact]
    public void Base_Disabled_OverridesEnforcement()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: false,
            gateOpen: true,
            nearBlacklistedBlock: false,
            currentStepHeight: 0.0f,
            defaultHeight: DefaultHeight,
            isEnforced: true,
            serverMinHeight: ServerMin, // would clamp to 1.0 if not disabled
            serverMaxHeight: ServerMax)
            .Should().Be(0.6f);
    }

    /// <summary>
    /// Blacklist proximity returns the configured <c>DefaultHeight</c>,
    /// regardless of the runtime value or enforcement clamp. This is the
    /// "near sensitive blocks like ladders, fall back to safe height"
    /// path.
    /// </summary>
    [Fact]
    public void Base_BlacklistProximity_ReturnsDefaultHeight()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: true,
            nearBlacklistedBlock: true,
            currentStepHeight: 1.5f,
            defaultHeight: 0.8f,
            isEnforced: false,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(0.8f);
    }

    [Fact]
    public void Base_BlacklistOverridesEnforcement()
    {
        // Even with enforcement that would clamp to ServerMin=1.0,
        // blacklist proximity returns DefaultHeight=0.6.
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: true,
            nearBlacklistedBlock: true,
            currentStepHeight: 1.5f,
            defaultHeight: DefaultHeight,
            isEnforced: true,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(DefaultHeight);
    }

    [Fact]
    public void Base_NotEnforced_BelowClientFloor_ClampsToClientFloor()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: true,
            nearBlacklistedBlock: false,
            currentStepHeight: 0.3f, // below StepHeightClamp.ClientMin 0.6
            defaultHeight: DefaultHeight,
            isEnforced: false,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(StepHeightClamp.ClientMin);
    }

    [Fact]
    public void Base_Enforced_BelowServerMin_ClampsToServerMin()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: true,
            nearBlacklistedBlock: false,
            currentStepHeight: 0.8f, // above client floor but below ServerMin 1.0
            defaultHeight: DefaultHeight,
            isEnforced: true,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(ServerMin);
    }

    [Fact]
    public void Base_Enforced_AboveServerMax_ClampsToServerMax()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: true,
            nearBlacklistedBlock: false,
            currentStepHeight: 5.0f,
            defaultHeight: DefaultHeight,
            isEnforced: true,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(ServerMax);
    }

    [Fact]
    public void Base_InRange_NotEnforced_Passthrough()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: true,
            nearBlacklistedBlock: false,
            currentStepHeight: 1.2f,
            defaultHeight: DefaultHeight,
            isEnforced: false,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(1.2f);
    }

    // ─── ComputeBaseStepHeight: sprint/sneak gate pins ──────────────────

    /// <summary>
    /// A closed gate (sprint-only active while not sprinting, or
    /// disable-while-sneaking active while sneaking) collapses the composed
    /// height to VS's 0.6 baseline even when stepup is enabled and the
    /// runtime height is high.
    /// </summary>
    [Fact]
    public void Base_GateClosed_ReturnsVsBaselineHeight()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: false,
            nearBlacklistedBlock: false,
            currentStepHeight: 1.5f,
            defaultHeight: DefaultHeight,
            isEnforced: false,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(0.6f);
    }

    /// <summary>
    /// The gate sits above blacklist proximity in the priority order: a
    /// closed gate returns the vanilla baseline, not the configured
    /// <c>DefaultHeight</c>. (Moot in practice when DefaultHeight is 0.6,
    /// pinned here with a distinct DefaultHeight to lock the ordering.)
    /// </summary>
    [Fact]
    public void Base_GateClosed_OverridesBlacklist()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: false,
            nearBlacklistedBlock: true,
            currentStepHeight: 1.5f,
            defaultHeight: 0.9f, // distinct from VS baseline
            isEnforced: false,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(0.6f);
    }

    /// <summary>
    /// The gate sits above the enforcement clamp: a closed gate returns the
    /// vanilla baseline even when enforcement would otherwise raise the value
    /// to <c>ServerMin</c>.
    /// </summary>
    [Fact]
    public void Base_GateClosed_OverridesEnforcement()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true,
            gateOpen: false,
            nearBlacklistedBlock: false,
            currentStepHeight: 0.0f,
            defaultHeight: DefaultHeight,
            isEnforced: true,
            serverMinHeight: ServerMin, // would clamp up to 1.0 if the gate were open
            serverMaxHeight: ServerMax)
            .Should().Be(0.6f);
    }

    /// <summary>
    /// Master-disable still beats an open gate: <c>stepUpEnabled = false</c>
    /// returns the baseline regardless of <c>gateOpen</c>.
    /// </summary>
    [Fact]
    public void Base_MasterDisabled_BeatsOpenGate()
    {
        StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: false,
            gateOpen: true,
            nearBlacklistedBlock: false,
            currentStepHeight: 1.5f,
            defaultHeight: DefaultHeight,
            isEnforced: false,
            serverMinHeight: ServerMin,
            serverMaxHeight: ServerMax)
            .Should().Be(0.6f);
    }

    // ─── ApplyCeilingGuard: reduction branches ──────────────────────────

    /// <summary>
    /// <c>baseHeight ≤ 0</c> returns as-is — the player isn't stepping
    /// up, so the ceiling guard has nothing to do. Defensive for any
    /// future code path that produces a zero/negative composed height.
    /// </summary>
    [Fact]
    public void Guard_BaseHeightZero_ReturnsUnchanged()
    {
        StepHeightController.ApplyCeilingGuard(
            baseHeight: 0f, clearance: 0.5f, defaultHeight: DefaultHeight)
            .Should().Be(0f);
    }

    /// <summary>
    /// Clearance ≤ 0.75 with <c>baseHeight ≥ defaultHeight</c> collapses
    /// to <c>defaultHeight</c>. The hard-snap-into-overhang prevention.
    /// </summary>
    [Fact]
    public void Guard_TightClearance_BaseAboveDefault_CollapsesToDefault()
    {
        StepHeightController.ApplyCeilingGuard(
            baseHeight: 1.5f, clearance: 0.5f, defaultHeight: DefaultHeight)
            .Should().Be(DefaultHeight);
    }

    /// <summary>
    /// Clearance ≤ 0.75 with <c>baseHeight &lt; defaultHeight</c> stays
    /// at <c>baseHeight</c> — <c>Math.Min</c> behavior, not a forced
    /// upgrade to default. Pin guards against a bug where a low base
    /// height would get artificially raised.
    /// </summary>
    [Fact]
    public void Guard_TightClearance_BaseBelowDefault_StaysAtBase()
    {
        StepHeightController.ApplyCeilingGuard(
            baseHeight: 0.5f, clearance: 0.5f, defaultHeight: DefaultHeight)
            .Should().Be(0.5f);
    }

    [Fact]
    public void Guard_ClearanceBetweenThresholdAndBase_UsesClearance()
    {
        // baseHeight 1.5, clearance 1.0: above threshold (0.75), below
        // base → ceiling crops to 1.0.
        StepHeightController.ApplyCeilingGuard(
            baseHeight: 1.5f, clearance: 1.0f, defaultHeight: DefaultHeight)
            .Should().Be(1.0f);
    }

    [Fact]
    public void Guard_ClearanceAboveBase_NoChange()
    {
        StepHeightController.ApplyCeilingGuard(
            baseHeight: 1.0f, clearance: 3.0f, defaultHeight: DefaultHeight)
            .Should().Be(1.0f);
    }

    /// <summary>
    /// Clearance exactly equal to the collapse threshold (0.75) hits the
    /// <c>&lt;=</c> branch — collapses to <c>min(baseHeight, defaultHeight)</c>.
    /// Pin the boundary.
    /// </summary>
    [Fact]
    public void Guard_ClearanceExactlyAtThreshold_TriggersCollapse()
    {
        StepHeightController.ApplyCeilingGuard(
            baseHeight: 1.5f, clearance: 0.75f, defaultHeight: DefaultHeight)
            .Should().Be(DefaultHeight);
    }
}
