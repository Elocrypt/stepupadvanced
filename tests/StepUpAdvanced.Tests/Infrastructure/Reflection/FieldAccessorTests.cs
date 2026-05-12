using System.Reflection;
using FluentAssertions;
using StepUpAdvanced.Infrastructure.Reflection;
using Xunit;

namespace StepUpAdvanced.Tests.Infrastructure.Reflection;

/// <summary>
/// Pins <see cref="FieldAccessor{TTarget, TValue}"/>'s contract: resolves
/// public/private fields by candidate-list, no-ops gracefully when no
/// candidate matches, performs primitive type conversion when the
/// declared field type differs from the accessor's value type.
/// </summary>
public class FieldAccessorTests
{
    // Sample target with one public field, one private field of a
    // different primitive type, and no shadowing.
    private sealed class Target
    {
        public float PublicFloat = 1.0f;
        private double PrivateDouble = 2.0;

        // Lets the test read back the private field without depending on
        // reflection inside the test body — keeps assertions clean.
        public double GetPrivateDouble() => PrivateDouble;
    }

    [Fact]
    public void Construction_ResolvesPublicField()
    {
        var accessor = new FieldAccessor<Target, float>(typeof(Target), "PublicFloat");

        accessor.IsAvailable.Should().BeTrue();
        accessor.ResolvedFieldName.Should().Be("PublicFloat");
    }

    [Fact]
    public void Construction_ResolvesPrivateField()
    {
        var accessor = new FieldAccessor<Target, double>(typeof(Target), "PrivateDouble");

        accessor.IsAvailable.Should().BeTrue();
        accessor.ResolvedFieldName.Should().Be("PrivateDouble");
    }

    [Fact]
    public void Construction_NoMatchingCandidate_IsAvailableFalse()
    {
        var accessor = new FieldAccessor<Target, float>(typeof(Target), "NonExistentField");

        accessor.IsAvailable.Should().BeFalse();
        accessor.ResolvedFieldName.Should().BeNull();
    }

    /// <summary>
    /// Resolution order is candidate-list order; the first match wins.
    /// Matches the VS-version-fallback pattern in the real call sites
    /// where we try <c>StepHeight</c> then <c>stepHeight</c>.
    /// </summary>
    [Fact]
    public void Construction_FallsThroughCandidatesInOrder()
    {
        var accessor = new FieldAccessor<Target, float>(
            typeof(Target), "NoSuch", "AlsoNoSuch", "PublicFloat");

        accessor.IsAvailable.Should().BeTrue();
        accessor.ResolvedFieldName.Should().Be("PublicFloat");
    }

    [Fact]
    public void TrySet_OnAvailableAccessor_WritesPublicField()
    {
        var target = new Target();
        var accessor = new FieldAccessor<Target, float>(typeof(Target), "PublicFloat");

        accessor.TrySet(target, 42.0f).Should().BeTrue();

        target.PublicFloat.Should().Be(42.0f);
    }

    [Fact]
    public void TrySet_OnAvailableAccessor_WritesPrivateField()
    {
        var target = new Target();
        var accessor = new FieldAccessor<Target, double>(typeof(Target), "PrivateDouble");

        accessor.TrySet(target, 9.0).Should().BeTrue();

        target.GetPrivateDouble().Should().Be(9.0);
    }

    [Fact]
    public void TrySet_OnMissingField_ReturnsFalseWithoutThrowing()
    {
        var accessor = new FieldAccessor<Target, float>(typeof(Target), "NonExistent");

        accessor.TrySet(new Target(), 1.0f).Should().BeFalse();
    }

    /// <summary>
    /// When TValue and the resolved field type differ, the compiled
    /// expression inserts a <see cref="System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression, System.Type)"/>.
    /// Pins that a <c>float</c>-typed accessor can write to a
    /// <c>double</c> field — the conversion happens at the delegate level,
    /// not as a per-call boxing or runtime cast.
    /// </summary>
    [Fact]
    public void TrySet_WithTypeConversion_FloatAccessorWritesDoubleField()
    {
        var target = new Target();
        var accessor = new FieldAccessor<Target, float>(typeof(Target), "PrivateDouble");

        accessor.IsAvailable.Should().BeTrue();
        accessor.TrySet(target, 3.5f).Should().BeTrue();

        target.GetPrivateDouble().Should().Be(3.5);
    }
}
