using FluentAssertions;
using StepUpAdvanced.Domain.Physics;
using Xunit;

namespace StepUpAdvanced.Tests.Domain.Physics;

/// <summary>
/// Pins <see cref="StepUpGate.ShouldApplyStepUp"/> — the shared gate for the
/// height path. Covers the all-off passthrough, each flag in isolation
/// (sprint-only, disable-while-sneaking, disable-while-airborne), and the
/// precedence between them.
/// </summary>
public class StepUpGateTests
{
    // Convenience defaults: no flags set, grounded, not swimming. Each test
    // overrides only the inputs it exercises.
    private static bool Gate(
        bool sprintOnly = false,
        bool disableWhileSneaking = false,
        bool disableWhileAirborne = false,
        bool sprint = false,
        bool sneak = false,
        bool onGround = true,
        bool swimming = false)
        => StepUpGate.ShouldApplyStepUp(
            sprintOnly, disableWhileSneaking, disableWhileAirborne,
            sprint, sneak, onGround, swimming);

    // ─── All flags off: unconditional passthrough (behavior preserved) ──

    [Theory]
    [InlineData(true, false, true)]   // sprinting, grounded
    [InlineData(false, true, true)]   // sneaking, grounded
    [InlineData(false, false, false)] // idle, airborne
    [InlineData(true, false, false)]  // sprinting, airborne
    public void AllFlagsOff_AlwaysOpen(bool sprint, bool sneak, bool onGround)
    {
        Gate(sprint: sprint, sneak: sneak, onGround: onGround).Should().BeTrue();
    }

    // ─── Sprint-only in isolation ───────────────────────────────────────

    [Fact]
    public void SprintOnly_Sprinting_Open()
        => Gate(sprintOnly: true, sprint: true).Should().BeTrue();

    [Fact]
    public void SprintOnly_NotSprinting_Closed()
        => Gate(sprintOnly: true, sprint: false).Should().BeFalse();

    // ─── Disable-while-sneaking in isolation ────────────────────────────

    [Fact]
    public void DisableWhileSneaking_Sneaking_Closed()
        => Gate(disableWhileSneaking: true, sneak: true).Should().BeFalse();

    [Fact]
    public void DisableWhileSneaking_NotSneaking_Open()
        => Gate(disableWhileSneaking: true, sneak: false).Should().BeTrue();

    // ─── Disable-while-airborne in isolation ────────────────────────────

    [Fact]
    public void DisableWhileAirborne_Airborne_Closed()
        => Gate(disableWhileAirborne: true, onGround: false, swimming: false).Should().BeFalse();

    [Fact]
    public void DisableWhileAirborne_Grounded_Open()
        => Gate(disableWhileAirborne: true, onGround: true).Should().BeTrue();

    /// <summary>
    /// Swimming is not "airborne" — it mirrors vanilla's <c>!OnGround &amp;&amp;
    /// !Swimming</c> step gate, so the airborne flag leaves the gate open while
    /// swimming even though the player isn't on the ground.
    /// </summary>
    [Fact]
    public void DisableWhileAirborne_Swimming_Open()
        => Gate(disableWhileAirborne: true, onGround: false, swimming: true).Should().BeTrue();

    [Fact]
    public void AirborneFlagOff_AirborneDoesNotClose()
        => Gate(disableWhileAirborne: false, onGround: false, swimming: false).Should().BeTrue();

    // ─── Precedence / combinations ──────────────────────────────────────

    /// <summary>Both control flags on, sprinting (not sneaking): gate open.</summary>
    [Fact]
    public void SprintAndSneakFlags_Sprinting_Open()
        => Gate(sprintOnly: true, disableWhileSneaking: true, sprint: true, sneak: false).Should().BeTrue();

    /// <summary>Both control flags on, idle: sprint-only closes the gate.</summary>
    [Fact]
    public void SprintAndSneakFlags_Idle_ClosedBySprintOnly()
        => Gate(sprintOnly: true, disableWhileSneaking: true, sprint: false, sneak: false).Should().BeFalse();

    /// <summary>
    /// Defensive precedence pin: even in the unnatural state where both sprint
    /// and sneak read true, sneak suppression wins and the gate is closed.
    /// </summary>
    [Fact]
    public void SneakSuppression_WinsOverSprint()
        => Gate(sprintOnly: true, disableWhileSneaking: true, sprint: true, sneak: true).Should().BeFalse();

    /// <summary>
    /// Airborne closes the gate even while sprinting when both sprint-only and
    /// disable-while-airborne are on (e.g. sprint-jumping toward a ledge).
    /// </summary>
    [Fact]
    public void SprintOnlyAndAirborne_SprintingAirborne_ClosedByAirborne()
        => Gate(sprintOnly: true, disableWhileAirborne: true, sprint: true, onGround: false, swimming: false)
            .Should().BeFalse();

    /// <summary>All three flags on, grounded and sprinting: gate open.</summary>
    [Fact]
    public void AllFlagsOn_GroundedSprinting_Open()
        => Gate(sprintOnly: true, disableWhileSneaking: true, disableWhileAirborne: true,
                sprint: true, sneak: false, onGround: true).Should().BeTrue();
}
