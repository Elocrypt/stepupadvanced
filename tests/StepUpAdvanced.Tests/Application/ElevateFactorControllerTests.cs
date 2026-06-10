using FluentAssertions;
using StepUpAdvanced.Application;
using StepUpAdvanced.Domain.Physics;
using Xunit;

namespace StepUpAdvanced.Tests.Application;

/// <summary>
/// Pins the priority order of
/// <see cref="ElevateFactorController.ComputeDesiredElevateFactor"/>.
/// The function decides what value gets pushed to the physics behavior on
/// every <c>ApplyNow</c>. Priority, top to bottom:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description><c>stepUpEnabled = false</c> → return
/// <c>defaultSpeed × 0.05</c>, ignore every other input.</description></item>
/// <item><description><c>isEnforced = true</c> → logical speed is
/// <c>optionsStepSpeed</c> (server-authoritative).</description></item>
/// <item><description><c>isEnforced = false</c> → logical speed is
/// <c>currentElevateFactor</c> (runtime user choice).</description></item>
/// <item><description>Clamp through <see cref="ElevateFactorMath.Clamp"/>:
/// client floor always; server min/max only when enforced.</description></item>
/// <item><description>Multiply by <c>0.05</c> (VS per-tick convention).</description></item>
/// </list>
/// </remarks>
public class ElevateFactorControllerTests
{
    private const float DefaultSpeed = 0.7f;
    private const float ServerMin = 1.0f;
    private const float ServerMax = 2.0f;

    [Fact]
    public void Disabled_ReturnsDefaultSpeedTimesFactor()
    {
        ElevateFactorController.ComputeDesiredElevateFactor(
            stepUpEnabled: false,
            isEnforced: false,
            currentElevateFactor: 1.5f,
            optionsStepSpeed: 1.8f,
            defaultSpeed: DefaultSpeed,
            serverMinSpeed: ServerMin,
            serverMaxSpeed: ServerMax)
            .Should().BeApproximately(DefaultSpeed * 0.05, 1e-6);
    }

    /// <summary>
    /// The disabled gate is the top of the priority order — enforcement
    /// cannot override it. A stepup-disabled player still gets the
    /// default speed regardless of server config.
    /// </summary>
    [Fact]
    public void Disabled_OverridesEnforcement_StillReturnsDefaultSpeed()
    {
        ElevateFactorController.ComputeDesiredElevateFactor(
            stepUpEnabled: false,
            isEnforced: true,
            currentElevateFactor: 1.5f,
            optionsStepSpeed: 1.8f,
            defaultSpeed: DefaultSpeed,
            serverMinSpeed: ServerMin,
            serverMaxSpeed: ServerMax)
            .Should().BeApproximately(DefaultSpeed * 0.05, 1e-6);
    }

    [Fact]
    public void Enforced_UsesOptionsStepSpeed_NotRuntime()
    {
        // Runtime 1.5; options 1.8. Enforced → 1.8 wins.
        ElevateFactorController.ComputeDesiredElevateFactor(
            stepUpEnabled: true,
            isEnforced: true,
            currentElevateFactor: 1.5f,
            optionsStepSpeed: 1.8f,
            defaultSpeed: DefaultSpeed,
            serverMinSpeed: ServerMin,
            serverMaxSpeed: ServerMax)
            .Should().BeApproximately(1.8f * 0.05, 1e-6);
    }

    [Fact]
    public void NotEnforced_UsesRuntime_NotOptions()
    {
        // Runtime 1.5; options 1.8. Not enforced → 1.5 wins.
        ElevateFactorController.ComputeDesiredElevateFactor(
            stepUpEnabled: true,
            isEnforced: false,
            currentElevateFactor: 1.5f,
            optionsStepSpeed: 1.8f,
            defaultSpeed: DefaultSpeed,
            serverMinSpeed: ServerMin,
            serverMaxSpeed: ServerMax)
            .Should().BeApproximately(1.5f * 0.05, 1e-6);
    }

    [Fact]
    public void Enforced_BelowServerMin_ClampsToServerMin()
    {
        ElevateFactorController.ComputeDesiredElevateFactor(
            stepUpEnabled: true,
            isEnforced: true,
            currentElevateFactor: 0.0f,
            optionsStepSpeed: 0.8f, // below ServerMin 1.0
            defaultSpeed: DefaultSpeed,
            serverMinSpeed: ServerMin,
            serverMaxSpeed: ServerMax)
            .Should().BeApproximately(ServerMin * 0.05, 1e-6);
    }

    [Fact]
    public void Enforced_AboveServerMax_ClampsToServerMax()
    {
        ElevateFactorController.ComputeDesiredElevateFactor(
            stepUpEnabled: true,
            isEnforced: true,
            currentElevateFactor: 0.0f,
            optionsStepSpeed: 5.0f, // far above ServerMax 2.0
            defaultSpeed: DefaultSpeed,
            serverMinSpeed: ServerMin,
            serverMaxSpeed: ServerMax)
            .Should().BeApproximately(ServerMax * 0.05, 1e-6);
    }

    /// <summary>
    /// Defensive against a runtime value that got below the client floor
    /// somehow (manual JSON edit, downgrade migration). The clamp must
    /// pull it back to <see cref="ElevateFactorMath.ClientMin"/> even when
    /// enforcement is off and the server caps shouldn't apply.
    /// </summary>
    [Fact]
    public void NotEnforced_BelowClientFloor_ClampsToClientFloor()
    {
        ElevateFactorController.ComputeDesiredElevateFactor(
            stepUpEnabled: true,
            isEnforced: false,
            currentElevateFactor: 0.3f, // below ClientMin 0.7
            optionsStepSpeed: 0.0f,
            defaultSpeed: DefaultSpeed,
            serverMinSpeed: ServerMin,
            serverMaxSpeed: ServerMax)
            .Should().BeApproximately(ElevateFactorMath.ClientMin * 0.05, 1e-6);
    }

    /// <summary>
    /// Pins the per-tick multiplier (0.05). A clean in-range value flows
    /// through the clamp untouched and is then scaled.
    /// </summary>
    [Fact]
    public void Multiplier_AppliedAfterClamp()
    {
        ElevateFactorController.ComputeDesiredElevateFactor(
            stepUpEnabled: true,
            isEnforced: false,
            currentElevateFactor: 1.5f,
            optionsStepSpeed: 0.0f,
            defaultSpeed: DefaultSpeed,
            serverMinSpeed: ServerMin,
            serverMaxSpeed: ServerMax)
            .Should().BeApproximately(0.075, 1e-6);
    }
}
