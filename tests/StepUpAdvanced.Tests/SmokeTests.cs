using Xunit;
using FluentAssertions;

namespace StepUpAdvanced.Tests;

/// <summary>
/// Phase 0 smoke tests. Exists solely to prove the test project builds and the
/// test runner discovers tests. Real domain tests land in Phase 5.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void TestProject_Builds_And_Runner_Discovers_Tests()
    {
        true.Should().BeTrue();
    }
}
