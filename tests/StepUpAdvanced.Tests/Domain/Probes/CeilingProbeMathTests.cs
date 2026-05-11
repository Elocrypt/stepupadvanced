using System;
using FluentAssertions;
using StepUpAdvanced.Domain.Probes;
using Xunit;

namespace StepUpAdvanced.Tests.Domain.Probes;

/// <summary>
/// Pins the 2D math the ceiling/forward probe relies on.
/// </summary>
public class CeilingProbeMathTests
{
    // VS yaw convention at the cardinal angles (per the pre-Phase-5
    // ForwardBlock formula): yaw=0 → sin=0, cos=1 → (0, +z)
    //                       yaw=π/2 → sin=1, cos=0 → (+x, 0)
    //                       yaw=π → sin=0, cos=-1 → (0, -z)
    //                       yaw=3π/2 → sin=-1, cos=0 → (-x, 0)

    [Fact]
    public void ForwardOffset_YawZero_PointsAlongPositiveZ()
    {
        CeilingProbeMath.ForwardOffset(yawRad: 0.0, distance: 3)
            .Should().Be((0, 3));
    }

    [Fact]
    public void ForwardOffset_YawHalfPi_PointsAlongPositiveX()
    {
        CeilingProbeMath.ForwardOffset(yawRad: Math.PI / 2, distance: 2)
            .Should().Be((2, 0));
    }

    [Fact]
    public void ForwardOffset_YawPi_PointsAlongNegativeZ()
    {
        CeilingProbeMath.ForwardOffset(yawRad: Math.PI, distance: 1)
            .Should().Be((0, -1));
    }

    /// <summary>
    /// Off-cardinal yaw snaps to the nearest cardinal direction. At 45°,
    /// both sin and cos are ~0.707, which rounds to 1 each — so the
    /// "forward" collapses to a diagonal cell (+x, +z) at distance 1.
    /// This is the documented behavior the rest of the probe code relies on.
    /// </summary>
    [Fact]
    public void ForwardOffset_YawFortyFiveDegrees_SnapsToDiagonal()
    {
        CeilingProbeMath.ForwardOffset(yawRad: Math.PI / 4, distance: 1)
            .Should().Be((1, 1));
    }

    /// <summary>
    /// Perpendicular is forward rotated 90°. Property test: the dot product
    /// of forward and perpendicular unit offsets is zero.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(Math.PI / 2)]
    [InlineData(Math.PI)]
    [InlineData(3 * Math.PI / 2)]
    public void PerpendicularOffset_IsOrthogonalToForwardAtCardinalYaws(double yaw)
    {
        var (fx, fz) = CeilingProbeMath.ForwardOffset(yaw, 1);
        var (px, pz) = CeilingProbeMath.PerpendicularOffset(yaw);

        int dot = fx * px + fz * pz;
        dot.Should().Be(0);
    }

    [Fact]
    public void ForwardSpanOffsets_SpanZero_ReturnsCenterOnly()
    {
        var offsets = CeilingProbeMath.ForwardSpanOffsets(yawRad: 0.0, distance: 3, span: 0);
        offsets.Should().HaveCount(1);
    }

    [Fact]
    public void ForwardSpanOffsets_SpanOne_ReturnsThreeColumns()
    {
        var offsets = CeilingProbeMath.ForwardSpanOffsets(yawRad: 0.0, distance: 3, span: 1);
        offsets.Should().HaveCount(3);
    }

    [Fact]
    public void ForwardSpanOffsets_SpanTwo_ReturnsFiveColumns()
    {
        var offsets = CeilingProbeMath.ForwardSpanOffsets(yawRad: 0.0, distance: 3, span: 2);
        offsets.Should().HaveCount(5);
    }

    [Fact]
    public void MaxRiseClamp_BelowHardCap_ReturnsFloor()
    {
        CeilingProbeMath.MaxRiseClamp(requestedStep: 1.7f, hardCap: 5).Should().Be(1);
    }

    [Fact]
    public void MaxRiseClamp_AboveHardCap_ReturnsHardCap()
    {
        CeilingProbeMath.MaxRiseClamp(requestedStep: 4.9f, hardCap: 2).Should().Be(2);
    }

    [Fact]
    public void MaxRiseClamp_ExactInteger_ReturnsThatValueUnchanged()
    {
        CeilingProbeMath.MaxRiseClamp(requestedStep: 3.0f, hardCap: 5).Should().Be(3);
    }

    [Fact]
    public void LandingClearanceRange_TypicalCase_ComputesYBounds()
    {
        // Player at baseY=100, stepping up 1.5 blocks → lands feet at y=101.
        // Entity height 1.8, headroom pad 0.2 → clearance Y2 = 101 + floor(1.6) = 102.
        // So yFrom=102, yTopInclusive=102.
        var (yFrom, yTop) = CeilingProbeMath.LandingClearanceRange(
            baseY: 100, requestedStep: 1.5f, entityHeight: 1.8f, headroomPad: 0.2f);

        yFrom.Should().Be(102);
        yTop.Should().Be(102);
    }

    /// <summary>
    /// Edge case: entity is so short (or pad so generous) that
    /// <c>entityHeight - headroomPad &lt; 1</c>, in which case
    /// <c>yTopInclusive &lt; yFrom</c> and the probe should treat the
    /// range as empty.
    /// </summary>
    [Fact]
    public void LandingClearanceRange_PadExceedsEntityHeight_ReturnsEmptyRange()
    {
        var (yFrom, yTop) = CeilingProbeMath.LandingClearanceRange(
            baseY: 100, requestedStep: 1.0f, entityHeight: 0.5f, headroomPad: 0.6f);

        yTop.Should().BeLessThan(yFrom);
    }
}
