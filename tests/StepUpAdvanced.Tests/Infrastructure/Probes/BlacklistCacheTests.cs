using System.Collections.Generic;
using FluentAssertions;
using StepUpAdvanced.Infrastructure.Probes;
using Xunit;

namespace StepUpAdvanced.Tests.Infrastructure.Probes;

/// <summary>
/// Pins the <see cref="BlacklistCache"/> contract: dirty-flag invalidation,
/// union-by-rebuild from two source lists, dedup across them, and
/// no-rebuild-when-clean.
/// </summary>
public class BlacklistCacheTests
{
    [Fact]
    public void FreshCache_IsDirtyAndEmpty()
    {
        var cache = new BlacklistCache();

        cache.IsEmpty.Should().BeTrue();
        cache.Count.Should().Be(0);

        // First RebuildIfDirty fires unconditionally, so we can assert
        // a fresh cache populates from the inputs immediately.
        cache.RebuildIfDirty(new[] { "game:stone" }, null);
        cache.Count.Should().Be(1);
        cache.Contains("game:stone").Should().BeTrue();
    }

    [Fact]
    public void Rebuild_UnionsServerAndClientLists()
    {
        var cache = new BlacklistCache();
        cache.RebuildIfDirty(
            serverList: new[] { "game:stone", "game:gravel" },
            clientList: new[] { "game:dirt" });

        cache.Count.Should().Be(3);
        cache.Contains("game:stone").Should().BeTrue();
        cache.Contains("game:gravel").Should().BeTrue();
        cache.Contains("game:dirt").Should().BeTrue();
    }

    [Fact]
    public void Rebuild_DeduplicatesAcrossLists()
    {
        var cache = new BlacklistCache();
        cache.RebuildIfDirty(
            serverList: new[] { "game:stone" },
            clientList: new[] { "game:stone", "game:dirt" });

        cache.Count.Should().Be(2);
    }

    /// <summary>
    /// Without <see cref="BlacklistCache.MarkDirty"/>, a subsequent
    /// <see cref="BlacklistCache.RebuildIfDirty"/> with different inputs is
    /// a no-op — preserves the prior cache.
    /// </summary>
    [Fact]
    public void RebuildIfDirty_WhenClean_IsNoOp()
    {
        var cache = new BlacklistCache();
        cache.RebuildIfDirty(new[] { "game:stone" }, null);
        cache.Count.Should().Be(1);

        // Without MarkDirty, the new lists are ignored.
        cache.RebuildIfDirty(new[] { "game:gravel", "game:dirt" }, null);
        cache.Count.Should().Be(1);
        cache.Contains("game:stone").Should().BeTrue();
        cache.Contains("game:gravel").Should().BeFalse();
    }

    [Fact]
    public void MarkDirty_AllowsRebuildToObserveNewInputs()
    {
        var cache = new BlacklistCache();
        cache.RebuildIfDirty(new[] { "game:stone" }, null);
        cache.MarkDirty();

        cache.RebuildIfDirty(new[] { "game:gravel" }, null);
        cache.Count.Should().Be(1);
        cache.Contains("game:stone").Should().BeFalse();
        cache.Contains("game:gravel").Should().BeTrue();
    }

    [Fact]
    public void Rebuild_NullInputs_ProducesEmptyCache()
    {
        var cache = new BlacklistCache();
        cache.RebuildIfDirty(null, null);

        cache.IsEmpty.Should().BeTrue();
        cache.Contains("anything").Should().BeFalse();
    }

    [Fact]
    public void Rebuild_AfterEmpty_ToPopulated_ObservedAfterMarkDirty()
    {
        var cache = new BlacklistCache();
        cache.RebuildIfDirty(null, null);
        cache.IsEmpty.Should().BeTrue();

        cache.MarkDirty();
        cache.RebuildIfDirty(null, new[] { "game:torch" });
        cache.Contains("game:torch").Should().BeTrue();
    }

    [Fact]
    public void Rebuild_AcceptsHashSetBackedInputs()
    {
        // Pins the IReadOnlyCollection polymorphism: callers can pass
        // either List or HashSet without API change.
        var cache = new BlacklistCache();
        var server = new HashSet<string> { "game:stone" };
        var client = new List<string> { "game:dirt" };

        cache.RebuildIfDirty(server, client);
        cache.Count.Should().Be(2);
    }
}
