using FluentAssertions;
using StepUpAdvanced.Domain.Physics;
using Xunit;

namespace StepUpAdvanced.Tests.Domain.Physics;

/// <summary>
/// Pins the <see cref="StepHeightClamp"/> contract — client floor always
/// applies, server range applies only when enforced, defensive when server
/// config inverts or goes below the client floor.
/// </summary>
public class StepHeightClampTests
{
    [Fact]
    public void Clamp_BelowClientFloor_NotEnforced_ReturnsClientFloor()
    {
        StepHeightClamp.Clamp(requested: 0.3f, isEnforced: false, serverMin: 0f, serverMax: 0f)
            .Should().Be(StepHeightClamp.ClientMin);
    }

    [Fact]
    public void Clamp_AboveClientFloor_NotEnforced_ReturnsRequested()
    {
        StepHeightClamp.Clamp(requested: 1.5f, isEnforced: false, serverMin: 0f, serverMax: 0f)
            .Should().Be(1.5f);
    }

    [Fact]
    public void Clamp_RequestedFarAboveServerMax_Enforced_ReturnsServerMax()
    {
        StepHeightClamp.Clamp(requested: 5.0f, isEnforced: true, serverMin: 0.6f, serverMax: 2.0f)
            .Should().Be(2.0f);
    }

    [Fact]
    public void Clamp_RequestedBelowServerMin_Enforced_ReturnsServerMin()
    {
        StepHeightClamp.Clamp(requested: 0.8f, isEnforced: true, serverMin: 1.0f, serverMax: 2.0f)
            .Should().Be(1.0f);
    }

    [Fact]
    public void Clamp_RequestedInServerRange_Enforced_ReturnsRequested()
    {
        StepHeightClamp.Clamp(requested: 1.2f, isEnforced: true, serverMin: 1.0f, serverMax: 2.0f)
            .Should().Be(1.2f);
    }

    /// <summary>
    /// Defensive case: a misconfigured server reports <c>serverMin</c> below
    /// the client's own floor. The client floor must still win — we never
    /// drop below <see cref="StepHeightClamp.ClientMin"/> regardless of
    /// what the server says.
    /// </summary>
    [Fact]
    public void Clamp_ServerMinBelowClientFloor_Enforced_ClientFloorWins()
    {
        StepHeightClamp.Clamp(requested: 0.3f, isEnforced: true, serverMin: 0.1f, serverMax: 2.0f)
            .Should().Be(StepHeightClamp.ClientMin);
    }

    [Fact]
    public void Clamp_RequestedExactlyAtClientFloor_NotEnforced_ReturnsClientFloor()
    {
        StepHeightClamp.Clamp(requested: StepHeightClamp.ClientMin, isEnforced: false, serverMin: 0f, serverMax: 0f)
            .Should().Be(StepHeightClamp.ClientMin);
    }
}
