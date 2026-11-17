using FluentAssertions;
using StepUpAdvanced.Application;
using StepUpAdvanced.Domain.Physics;
using Xunit;

namespace StepUpAdvanced.Tests.Domain.Physics;

/// <summary>
/// Pins <see cref="BasicMode"/>: the fixed profile, that config values never
/// reach Basic mode resolution, and that Basic inputs compose through
/// <see cref="StepHeightController"/>'s pure height functions as expected.
/// </summary>
public class BasicModeTests
{
    [Fact]
    public void Profile_is_one_block_at_vanilla_speed()
    {
        BasicMode.StepHeight.Should().Be(1.2f);
        BasicMode.StepSpeed.Should().Be(1.0f);
        BasicMode.FallbackHeight.Should().Be(0.6f);
    }

    [Theory]
    [InlineData(0.6f, 0.6f, false)]
    [InlineData(5f, 3f, true)]
    [InlineData(1.2f, 1.2f, true)]
    public void Basic_height_inputs_ignore_config(float height, float defaultHeight, bool enforced)
    {
        var inputs = BasicMode.ResolveHeightInputs(basic: true, height, defaultHeight, enforced);

        inputs.TargetHeight.Should().Be(BasicMode.StepHeight);
        inputs.DefaultHeight.Should().Be(BasicMode.FallbackHeight);
        inputs.IsEnforced.Should().BeFalse();
    }

    [Theory]
    [InlineData(1.5f, 0.8f, true)]
    [InlineData(2.0f, 0.6f, false)]
    public void Full_height_inputs_pass_config_through(float height, float defaultHeight, bool enforced)
    {
        var inputs = BasicMode.ResolveHeightInputs(basic: false, height, defaultHeight, enforced);

        inputs.Should().Be(new HeightInputs(height, defaultHeight, enforced));
    }

    // The checks below run Basic inputs through the real height composition,
    // using hostile config values (huge default height, stale enforcement)
    // to show none of them reach the final step height.

    [Fact]
    public void Basic_height_composes_to_one_block()
    {
        var inputs = BasicMode.ResolveHeightInputs(basic: true, configuredHeight: 5f, configuredDefaultHeight: 3f, configuredEnforced: true);

        float height = StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true, gateOpen: true, nearBlacklistedBlock: false,
            inputs.TargetHeight, inputs.DefaultHeight, inputs.IsEnforced,
            serverMinHeight: 0.6f, serverMaxHeight: 10f);

        height.Should().Be(BasicMode.StepHeight);
    }

    [Fact]
    public void Basic_near_blacklisted_block_uses_vanilla_not_config_default()
    {
        var inputs = BasicMode.ResolveHeightInputs(basic: true, configuredHeight: 5f, configuredDefaultHeight: 3f, configuredEnforced: false);

        float height = StepHeightController.ComputeBaseStepHeight(
            stepUpEnabled: true, gateOpen: true, nearBlacklistedBlock: true,
            inputs.TargetHeight, inputs.DefaultHeight, inputs.IsEnforced,
            serverMinHeight: 0.6f, serverMaxHeight: 1.2f);

        height.Should().Be(BasicMode.FallbackHeight);
    }

    [Fact]
    public void Basic_low_ceiling_collapses_to_vanilla_not_config_default()
    {
        var inputs = BasicMode.ResolveHeightInputs(basic: true, configuredHeight: 5f, configuredDefaultHeight: 3f, configuredEnforced: false);

        float height = StepHeightController.ApplyCeilingGuard(
            baseHeight: BasicMode.StepHeight, clearance: 0.5f, inputs.DefaultHeight);

        height.Should().Be(BasicMode.FallbackHeight);
    }
}
