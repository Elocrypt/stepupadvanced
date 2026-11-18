using System.Collections.Generic;
using FluentAssertions;
using StepUpAdvanced.Configuration;
using Xunit;

namespace StepUpAdvanced.Tests.Configuration;

/// <summary>
/// Load-time validation of both config files. Beyond the individual clamps,
/// two properties matter: defaults pass untouched, and the normalizer is
/// idempotent (a corrected file isn't rewritten on every load).
/// </summary>
public class ConfigNormalizerTests
{
    [Fact]
    public void Defaults_PassUntouched()
    {
        var messages = new List<string>();

        ConfigNormalizer.NormalizeServer(new StepUpServerOptions(), messages).Should().BeFalse();
        ConfigNormalizer.NormalizeClient(new StepUpClientOptions(), messages).Should().BeFalse();
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Server_UnsetCaps_FallBackToDefaults()
    {
        var s = new StepUpServerOptions
        {
            ServerMinStepHeight = 0f,
            ServerMaxStepHeight = -1f,
            ServerMinStepSpeed = float.NaN,
            ServerMaxStepSpeed = 0f,
        };

        ConfigNormalizer.NormalizeServer(s, new List<string>()).Should().BeTrue();

        var d = new StepUpServerOptions();
        s.ServerMinStepHeight.Should().Be(d.ServerMinStepHeight);
        s.ServerMaxStepHeight.Should().Be(d.ServerMaxStepHeight);
        s.ServerMinStepSpeed.Should().Be(d.ServerMinStepSpeed);
        s.ServerMaxStepSpeed.Should().Be(d.ServerMaxStepSpeed);
    }

    [Fact]
    public void Server_CapsClampedToHardLimits()
    {
        var s = new StepUpServerOptions
        {
            ServerMinStepHeight = 0.3f,
            ServerMaxStepHeight = 99f,
            ServerMinStepSpeed = 0.2f,
            ServerMaxStepSpeed = 50f,
        };

        ConfigNormalizer.NormalizeServer(s, new List<string>());

        s.ServerMinStepHeight.Should().Be(ConfigNormalizer.ServerCapMinHeight);
        s.ServerMaxStepHeight.Should().Be(ConfigNormalizer.ServerCapMaxHeight);
        s.ServerMinStepSpeed.Should().Be(ConfigNormalizer.ServerCapMinSpeed);
        s.ServerMaxStepSpeed.Should().Be(ConfigNormalizer.ServerCapMaxSpeed);
    }

    [Fact]
    public void Server_ReversedRanges_AreSwapped()
    {
        var s = new StepUpServerOptions
        {
            ServerMinStepHeight = 2.0f,
            ServerMaxStepHeight = 1.0f,
            ServerMinStepSpeed = 1.5f,
            ServerMaxStepSpeed = 0.9f,
        };

        ConfigNormalizer.NormalizeServer(s, new List<string>()).Should().BeTrue();

        s.ServerMinStepHeight.Should().Be(1.0f);
        s.ServerMaxStepHeight.Should().Be(2.0f);
        s.ServerMinStepSpeed.Should().Be(0.9f);
        s.ServerMaxStepSpeed.Should().Be(1.5f);
    }

    [Fact]
    public void Server_NullBlacklist_BecomesEmpty()
    {
        var s = new StepUpServerOptions { BlockBlacklist = null! };

        ConfigNormalizer.NormalizeServer(s, new List<string>()).Should().BeTrue();

        s.BlockBlacklist.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Client_ValuesBelowFloors_AreRaised()
    {
        var c = new StepUpClientOptions
        {
            StepHeight = 0.2f,
            StepSpeed = 0.1f,
            DefaultHeight = 0.3f,
            StepHeightIncrement = 0.01f,
            StepSpeedIncrement = 0f,
        };
        var messages = new List<string>();

        ConfigNormalizer.NormalizeClient(c, messages).Should().BeTrue();

        c.StepHeight.Should().Be(0.6f);
        c.StepSpeed.Should().Be(0.7f);
        c.DefaultHeight.Should().Be(0.6f);
        c.StepHeightIncrement.Should().Be(ConfigNormalizer.MinIncrement);
        c.StepSpeedIncrement.Should().Be(ConfigNormalizer.MinIncrement);
        messages.Should().HaveCount(5);
    }

    /// <summary>
    /// The client file is the player's own. A high step height must survive
    /// load untouched; server caps are applied at runtime, never written here.
    /// </summary>
    [Fact]
    public void Client_HighValues_AreNotCappedAtLoad()
    {
        var c = new StepUpClientOptions { StepHeight = 5f, StepSpeed = 4f, DefaultHeight = 2f };

        ConfigNormalizer.NormalizeClient(c, new List<string>()).Should().BeFalse();

        c.StepHeight.Should().Be(5f);
        c.StepSpeed.Should().Be(4f);
        c.DefaultHeight.Should().Be(2f);
    }

    [Fact]
    public void Client_ProbeSettings_ClampedToRange()
    {
        var c = new StepUpClientOptions
        {
            ForwardProbeDistance = 0,
            ForwardProbeSpan = 9,
            CeilingHeadroomPad = 1f,
        };

        ConfigNormalizer.NormalizeClient(c, new List<string>()).Should().BeTrue();

        c.ForwardProbeDistance.Should().Be(1);
        c.ForwardProbeSpan.Should().Be(ConfigNormalizer.MaxForwardProbeSpan);
        c.CeilingHeadroomPad.Should().Be(ConfigNormalizer.MaxCeilingHeadroomPad);
    }

    [Fact]
    public void Client_NaN_IsTreatedAsBelowFloor()
    {
        var c = new StepUpClientOptions { StepHeight = float.NaN };

        ConfigNormalizer.NormalizeClient(c, new List<string>()).Should().BeTrue();

        c.StepHeight.Should().Be(0.6f);
    }

    [Fact]
    public void BothSides_AreIdempotent()
    {
        var s = new StepUpServerOptions { ServerMinStepHeight = 3f, ServerMaxStepHeight = 0f, ServerMaxStepSpeed = 99f };
        var c = new StepUpClientOptions { StepHeight = 0.1f, ForwardProbeSpan = 0, CeilingHeadroomPad = -1f };
        ConfigNormalizer.NormalizeServer(s, new List<string>());
        ConfigNormalizer.NormalizeClient(c, new List<string>());

        var messages = new List<string>();
        ConfigNormalizer.NormalizeServer(s, messages).Should().BeFalse();
        ConfigNormalizer.NormalizeClient(c, messages).Should().BeFalse();
        messages.Should().BeEmpty();
    }
}
