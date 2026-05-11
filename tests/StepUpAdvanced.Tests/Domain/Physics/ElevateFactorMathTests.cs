using FluentAssertions;
using StepUpAdvanced.Domain.Physics;
using Xunit;

namespace StepUpAdvanced.Tests.Domain.Physics;

/// <summary>
/// Symmetric counterpart to <see cref="StepHeightClampTests"/>. Same shape,
/// values shifted (0.7 floor instead of 0.6).
/// </summary>
public class ElevateFactorMathTests
{
    [Fact]
    public void Clamp_BelowClientFloor_NotEnforced_ReturnsClientFloor()
    {
        ElevateFactorMath.Clamp(requested: 0.5f, isEnforced: false, serverMin: 0f, serverMax: 0f)
            .Should().Be(ElevateFactorMath.ClientMin);
    }

    [Fact]
    public void Clamp_AboveClientFloor_NotEnforced_ReturnsRequested()
    {
        ElevateFactorMath.Clamp(requested: 1.4f, isEnforced: false, serverMin: 0f, serverMax: 0f)
            .Should().Be(1.4f);
    }

    [Fact]
    public void Clamp_RequestedAboveServerMax_Enforced_ReturnsServerMax()
    {
        ElevateFactorMath.Clamp(requested: 5.0f, isEnforced: true, serverMin: 0.7f, serverMax: 2.0f)
            .Should().Be(2.0f);
    }

    [Fact]
    public void Clamp_RequestedBelowServerMin_Enforced_ReturnsServerMin()
    {
        ElevateFactorMath.Clamp(requested: 0.9f, isEnforced: true, serverMin: 1.1f, serverMax: 2.0f)
            .Should().Be(1.1f);
    }

    [Fact]
    public void Clamp_RequestedInServerRange_Enforced_ReturnsRequested()
    {
        ElevateFactorMath.Clamp(requested: 1.3f, isEnforced: true, serverMin: 1.0f, serverMax: 2.0f)
            .Should().Be(1.3f);
    }

    [Fact]
    public void Clamp_ServerMinBelowClientFloor_Enforced_ClientFloorWins()
    {
        ElevateFactorMath.Clamp(requested: 0.5f, isEnforced: true, serverMin: 0.3f, serverMax: 2.0f)
            .Should().Be(ElevateFactorMath.ClientMin);
    }

    [Fact]
    public void Clamp_RequestedExactlyAtClientFloor_NotEnforced_ReturnsClientFloor()
    {
        ElevateFactorMath.Clamp(requested: ElevateFactorMath.ClientMin, isEnforced: false, serverMin: 0f, serverMax: 0f)
            .Should().Be(ElevateFactorMath.ClientMin);
    }
}
