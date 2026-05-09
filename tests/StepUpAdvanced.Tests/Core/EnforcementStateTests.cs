using FluentAssertions;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Core;
using Vintagestory.API.Common;
using Xunit;

namespace StepUpAdvanced.Tests.Core;

/// <summary>
/// Locks in the behavior of <see cref="EnforcementState.IsEnforced"/> across
/// every meaningful combination of (side, isSinglePlayer, config-flag).
///
/// Server side is logically never single-player, so the isSinglePlayer
/// argument is irrelevant when side=Server. We still test both values for
/// server side to confirm that.
/// </summary>
public class EnforcementStateTests
{
    /// <summary>
    /// Null config means "we don't know what to enforce" — never enforce.
    /// This is the safest default and matches the call-sites that use
    /// <c>config?.ServerEnforceSettings == true</c> guards elsewhere.
    /// </summary>
    [Theory]
    [InlineData(EnumAppSide.Server, false)]
    [InlineData(EnumAppSide.Server, true)]
    [InlineData(EnumAppSide.Client, false)]
    [InlineData(EnumAppSide.Client, true)]
    public void NullConfig_NeverEnforces(EnumAppSide side, bool isSinglePlayer)
    {
        EnforcementState.IsEnforced(side, isSinglePlayer, config: null)
            .Should().BeFalse();
    }

    /// <summary>
    /// Even with the config loaded, if <c>ServerEnforceSettings</c> is off,
    /// nothing enforces.
    /// </summary>
    [Theory]
    [InlineData(EnumAppSide.Server, false)]
    [InlineData(EnumAppSide.Server, true)]
    [InlineData(EnumAppSide.Client, false)]
    [InlineData(EnumAppSide.Client, true)]
    public void EnforceFlagOff_NeverEnforces(EnumAppSide side, bool isSinglePlayer)
    {
        var cfg = new StepUpOptions { ServerEnforceSettings = false };

        EnforcementState.IsEnforced(side, isSinglePlayer, cfg)
            .Should().BeFalse();
    }

    /// <summary>
    /// Server side with the flag on always enforces, regardless of
    /// isSinglePlayer (servers don't have that concept).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServerSide_WithFlagOn_AlwaysEnforces(bool isSinglePlayer)
    {
        var cfg = new StepUpOptions { ServerEnforceSettings = true };

        EnforcementState.IsEnforced(EnumAppSide.Server, isSinglePlayer, cfg)
            .Should().BeTrue();
    }

    /// <summary>
    /// Multiplayer client + flag-on = enforced. The whole point of the feature.
    /// </summary>
    [Fact]
    public void MultiplayerClient_WithFlagOn_Enforces()
    {
        var cfg = new StepUpOptions { ServerEnforceSettings = true };

        EnforcementState.IsEnforced(EnumAppSide.Client, isSinglePlayer: false, cfg)
            .Should().BeTrue();
    }

    /// <summary>
    /// Single-player client never enforces, even with the flag set. This
    /// preserves the original ModSystem.IsEnforced behavior, where
    /// <c>capi.IsSinglePlayer</c> short-circuited the predicate. Single-player
    /// has no remote authority to enforce anything against.
    /// </summary>
    [Fact]
    public void SinglePlayerClient_WithFlagOn_DoesNotEnforce()
    {
        var cfg = new StepUpOptions { ServerEnforceSettings = true };

        EnforcementState.IsEnforced(EnumAppSide.Client, isSinglePlayer: true, cfg)
            .Should().BeFalse();
    }
}
