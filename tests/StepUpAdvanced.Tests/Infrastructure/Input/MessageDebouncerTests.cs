using FluentAssertions;
using StepUpAdvanced.Infrastructure.Input;
using Xunit;

namespace StepUpAdvanced.Tests.Infrastructure.Input;

/// <summary>
/// Pins the <see cref="OnceFlag"/> contract — the show-once-until-reset
/// primitive that backs every flag on <see cref="MessageDebouncer"/>.
/// </summary>
public class OnceFlagTests
{
    [Fact]
    public void TryShow_FirstCall_ReturnsTrueAndMarksShown()
    {
        var flag = new OnceFlag();

        flag.TryShow().Should().BeTrue();
        flag.IsShown.Should().BeTrue();
    }

    [Fact]
    public void TryShow_SecondCall_ReturnsFalseAndStaysShown()
    {
        var flag = new OnceFlag();
        flag.TryShow();

        flag.TryShow().Should().BeFalse();
        flag.IsShown.Should().BeTrue();
    }

    [Fact]
    public void Reset_AfterTryShow_RearmsForNextTryShow()
    {
        var flag = new OnceFlag();
        flag.TryShow();
        flag.Reset();

        flag.IsShown.Should().BeFalse();
        flag.TryShow().Should().BeTrue();
    }

    [Fact]
    public void Reset_OnFreshFlag_IsIdempotent()
    {
        var flag = new OnceFlag();

        flag.Reset();

        flag.IsShown.Should().BeFalse();
        flag.TryShow().Should().BeTrue();
    }

    [Fact]
    public void IsShown_OnFreshFlag_IsFalse()
    {
        var flag = new OnceFlag();

        flag.IsShown.Should().BeFalse();
    }

    [Fact]
    public void Cycle_ShowResetShow_BehavesAsExpected()
    {
        var flag = new OnceFlag();

        flag.TryShow().Should().BeTrue();
        flag.Reset();
        flag.TryShow().Should().BeTrue();
        flag.TryShow().Should().BeFalse();
    }
}

/// <summary>
/// Pins the structural guarantee that <see cref="MessageDebouncer"/>'s
/// nine flags are independent. The whole point of Phase 4 was to undo
/// the cross-contamination caused by sharing flags across distinct
/// toasts, so an explicit "changing one doesn't affect the others" test
/// is worth having.
/// </summary>
public class MessageDebouncerTests
{
    [Fact]
    public void Construction_AllFlagsStartUnshown()
    {
        var d = new MessageDebouncer();

        d.HeightAtMax.IsShown.Should().BeFalse();
        d.HeightAtMin.IsShown.Should().BeFalse();
        d.SpeedAtMax.IsShown.Should().BeFalse();
        d.SpeedAtMin.IsShown.Should().BeFalse();
        d.HeightEnforced.IsShown.Should().BeFalse();
        d.SpeedEnforced.IsShown.Should().BeFalse();
        d.HeightSpeedOnlyMode.IsShown.Should().BeFalse();
        d.ReloadBlocked.IsShown.Should().BeFalse();
        d.ServerEnforcement.IsShown.Should().BeFalse();
    }

    /// <summary>
    /// The pre-Phase-4 cross-axis bug: showing the height-at-max toast
    /// suppressed the next speed-at-max toast. Pin that the new flags
    /// are independent.
    /// </summary>
    [Fact]
    public void HeightAtMax_DoesNotSuppress_SpeedAtMax()
    {
        var d = new MessageDebouncer();
        d.HeightAtMax.TryShow();

        d.SpeedAtMax.IsShown.Should().BeFalse();
        d.SpeedAtMax.TryShow().Should().BeTrue();
    }

    /// <summary>
    /// The pre-Phase-4 reload-blocked sharing bug: hitting the height
    /// cap suppressed the reload-blocked toast because both flipped
    /// <c>hasShownMaxMessage</c>.
    /// </summary>
    [Fact]
    public void HeightAtMax_DoesNotSuppress_ReloadBlocked()
    {
        var d = new MessageDebouncer();
        d.HeightAtMax.TryShow();

        d.ReloadBlocked.TryShow().Should().BeTrue();
    }

    /// <summary>
    /// The pre-Phase-4 enforcement-blocked split-by-direction bug:
    /// <c>MaxE</c> and <c>MinE</c> were each shared across both axes,
    /// so e.g. hitting the height-enforced-blocked toast suppressed
    /// the next speed-enforced-blocked toast. Pin independence per axis.
    /// </summary>
    [Fact]
    public void HeightEnforced_DoesNotSuppress_SpeedEnforced()
    {
        var d = new MessageDebouncer();
        d.HeightEnforced.TryShow();

        d.SpeedEnforced.TryShow().Should().BeTrue();
    }

    [Fact]
    public void Reset_OnOneFlag_DoesNotAffectOthers()
    {
        var d = new MessageDebouncer();
        d.HeightAtMax.TryShow();
        d.SpeedAtMax.TryShow();

        d.HeightAtMax.Reset();

        d.HeightAtMax.IsShown.Should().BeFalse();
        d.SpeedAtMax.IsShown.Should().BeTrue();
    }
}
