using System.Collections.Generic;
using FluentAssertions;
using StepUpAdvanced.Domain.Blocks;
using Xunit;

namespace StepUpAdvanced.Tests.Domain.Blocks;

/// <summary>
/// Pins the two-list blacklist lookup. Strict-equality semantics — wildcard
/// support is deferred to a later phase if/when there's a use case.
/// </summary>
public class BlacklistMatcherTests
{
    [Fact]
    public void Matches_NullCollection_ReturnsFalse()
    {
        BlacklistMatcher.Matches("game:stone", null).Should().BeFalse();
    }

    [Fact]
    public void Matches_EmptyCollection_ReturnsFalse()
    {
        BlacklistMatcher.Matches("game:stone", new List<string>()).Should().BeFalse();
    }

    [Fact]
    public void Matches_CodeInList_ReturnsTrue()
    {
        BlacklistMatcher.Matches("game:stone", new List<string> { "game:dirt", "game:stone" })
            .Should().BeTrue();
    }

    [Fact]
    public void Matches_CodeNotInList_ReturnsFalse()
    {
        BlacklistMatcher.Matches("game:stone", new List<string> { "game:dirt", "game:gravel" })
            .Should().BeFalse();
    }

    [Fact]
    public void Matches_HashSetBacked_ReturnsCorrectly()
    {
        // Pins that the collection-polymorphic API works with HashSet too,
        // so Phase 6's HashSet caching layer can drop in without API churn.
        var set = new HashSet<string> { "game:dirt", "game:stone" };
        BlacklistMatcher.Matches("game:stone", set).Should().BeTrue();
        BlacklistMatcher.Matches("game:granite", set).Should().BeFalse();
    }

    [Fact]
    public void MatchesAny_CodeInServerListOnly_ReturnsTrue()
    {
        BlacklistMatcher.MatchesAny(
            "game:stone",
            serverList: new List<string> { "game:stone" },
            clientList: new List<string> { "game:dirt" })
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAny_CodeInClientListOnly_ReturnsTrue()
    {
        BlacklistMatcher.MatchesAny(
            "game:stone",
            serverList: new List<string> { "game:dirt" },
            clientList: new List<string> { "game:stone" })
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAny_CodeInBothLists_ReturnsTrue()
    {
        BlacklistMatcher.MatchesAny(
            "game:stone",
            serverList: new List<string> { "game:stone" },
            clientList: new List<string> { "game:stone" })
            .Should().BeTrue();
    }

    [Fact]
    public void MatchesAny_CodeInNeitherList_ReturnsFalse()
    {
        BlacklistMatcher.MatchesAny(
            "game:stone",
            serverList: new List<string> { "game:dirt" },
            clientList: new List<string> { "game:gravel" })
            .Should().BeFalse();
    }

    [Fact]
    public void MatchesAny_BothListsNull_ReturnsFalse()
    {
        BlacklistMatcher.MatchesAny("game:stone", serverList: null, clientList: null)
            .Should().BeFalse();
    }
}
