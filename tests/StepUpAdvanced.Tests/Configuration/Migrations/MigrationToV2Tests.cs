using FluentAssertions;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Configuration.Migrations;
using Xunit;

namespace StepUpAdvanced.Tests.Configuration.Migrations;

/// <summary>
/// Tests <see cref="MigrationToV2"/> in isolation, independent of the runner.
/// Pins the exact transformation semantics: zero / negative forward-probe
/// values get coerced to 1; positive values are left alone.
/// </summary>
public class MigrationToV2Tests
{
    /// <summary>Target version is 2 — applies to configs at v0 and v1.</summary>
    [Fact]
    public void TargetVersionIs2()
    {
        new MigrationToV2().TargetVersion.Should().Be(2);
    }

    /// <summary>Both probe fields at zero: both fixed, returns true.</summary>
    [Fact]
    public void FixesBothFields_WhenBothAtZero()
    {
        var cfg = new StepUpOptions { ForwardProbeSpan = 0, ForwardProbeDistance = 0 };

        bool changed = new MigrationToV2().Apply(cfg);

        changed.Should().BeTrue();
        cfg.ForwardProbeSpan.Should().Be(1);
        cfg.ForwardProbeDistance.Should().Be(1);
    }

    /// <summary>Negative values also count as "needs fixing".</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void FixesNegativeValues(int negative)
    {
        var cfg = new StepUpOptions { ForwardProbeSpan = negative, ForwardProbeDistance = negative };

        bool changed = new MigrationToV2().Apply(cfg);

        changed.Should().BeTrue();
        cfg.ForwardProbeSpan.Should().Be(1);
        cfg.ForwardProbeDistance.Should().Be(1);
    }

    /// <summary>Only one field needs fixing: the other is left alone.</summary>
    [Fact]
    public void LeavesPositiveValuesAlone_WhenOnlyOneFieldNeedsFix()
    {
        // Span is fine, Distance needs migration.
        var cfg = new StepUpOptions { ForwardProbeSpan = 2, ForwardProbeDistance = 0 };

        bool changed = new MigrationToV2().Apply(cfg);

        changed.Should().BeTrue();
        cfg.ForwardProbeSpan.Should().Be(2);  // Untouched.
        cfg.ForwardProbeDistance.Should().Be(1);
    }

    /// <summary>
    /// Already-migrated config: both fields positive. Migration is a no-op
    /// and reports no change. Critical for idempotence — the runner relies
    /// on this to behave correctly when re-run on already-migrated data.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 1)]
    public void IsNoOp_WhenValuesAlreadyPositive(int span, int distance)
    {
        var cfg = new StepUpOptions { ForwardProbeSpan = span, ForwardProbeDistance = distance };

        bool changed = new MigrationToV2().Apply(cfg);

        changed.Should().BeFalse();
        cfg.ForwardProbeSpan.Should().Be(span);
        cfg.ForwardProbeDistance.Should().Be(distance);
    }

    /// <summary>
    /// The migration touches ONLY the two forward-probe fields. Other fields
    /// are left exactly as they came in — pinning that no accidental
    /// modifications creep into the migration over time.
    /// </summary>
    [Fact]
    public void DoesNotTouch_OtherFields()
    {
        var cfg = new StepUpOptions
        {
            ForwardProbeSpan = 0,
            ForwardProbeDistance = 0,

            // Arbitrary values that the migration must preserve.
            StepHeight = 1.5f,
            StepSpeed = 1.4f,
            ServerEnforceSettings = false,
            QuietMode = true,
            CeilingHeadroomPad = 0.2f,
        };

        new MigrationToV2().Apply(cfg);

        cfg.StepHeight.Should().Be(1.5f);
        cfg.StepSpeed.Should().Be(1.4f);
        cfg.ServerEnforceSettings.Should().BeFalse();
        cfg.QuietMode.Should().BeTrue();
        cfg.CeilingHeadroomPad.Should().Be(0.2f);
    }
}
