using FluentAssertions;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Configuration.Migrations;
using Xunit;

namespace StepUpAdvanced.Tests.Configuration.Migrations;

/// <summary>
/// Locks in the orchestration semantics of <see cref="MigrationRunner"/>:
/// version-gating, idempotence, and the "any migration changed something →
/// returns true" contract.
/// </summary>
/// <remarks>
/// The runner is tested against the real registered migrations (currently
/// just <see cref="MigrationToV2"/>). When new migrations are added, the
/// existing tests should continue to pass without modification — they
/// verify orchestration semantics, not migration-specific behavior. The
/// per-migration tests (e.g. <c>MigrationToV2Tests</c>) cover the actual
/// transformation logic.
/// </remarks>
public class MigrationRunnerTests
{
    /// <summary>
    /// Sanity check: a fresh-defaults config at v0 needs the v2 migration's
    /// forward-probe defaults applied. The runner reports a change and the
    /// affected fields end up at their defaults.
    /// </summary>
    [Fact]
    public void RunsApplicableMigrations_WhenFromVersionBelowTarget()
    {
        var cfg = new StepUpOptions
        {
            // Simulate a pre-v2 config where these were unset / zero.
            ForwardProbeSpan = 0,
            ForwardProbeDistance = 0,
        };

        bool changed = MigrationRunner.Run(cfg, fromVersion: 0);

        changed.Should().BeTrue();
        cfg.ForwardProbeSpan.Should().Be(1);
        cfg.ForwardProbeDistance.Should().Be(1);
    }

    /// <summary>
    /// Above-target version: nothing runs, nothing changes.
    /// </summary>
    [Fact]
    public void SkipsMigrations_WhenFromVersionAtOrAboveTarget()
    {
        var cfg = new StepUpOptions
        {
            // Pathological values that the v2 migration WOULD fix if it ran.
            ForwardProbeSpan = 0,
            ForwardProbeDistance = 0,
        };

        bool changed = MigrationRunner.Run(cfg, fromVersion: 2);

        changed.Should().BeFalse();
        // Values intentionally left at zero — the runner skipped past v2.
        cfg.ForwardProbeSpan.Should().Be(0);
        cfg.ForwardProbeDistance.Should().Be(0);
    }

    /// <summary>
    /// Idempotence: running the runner twice produces the same result as
    /// running it once. The second run finds nothing to do and returns false.
    /// </summary>
    [Fact]
    public void IsIdempotent_OnRepeatedRuns()
    {
        var cfg = new StepUpOptions
        {
            ForwardProbeSpan = 0,
            ForwardProbeDistance = 0,
        };

        bool firstRun = MigrationRunner.Run(cfg, fromVersion: 0);
        // Second run: starting at v0 again, but the values are already correct.
        // The migration applies but reports no change.
        bool secondRun = MigrationRunner.Run(cfg, fromVersion: 0);

        firstRun.Should().BeTrue();
        secondRun.Should().BeFalse();
        cfg.ForwardProbeSpan.Should().Be(1);
        cfg.ForwardProbeDistance.Should().Be(1);
    }

    /// <summary>
    /// Negative starting version: treated like zero. Configs with corrupt
    /// or missing SchemaVersion (deserialized as -1, default-int) still get
    /// fully migrated.
    /// </summary>
    [Fact]
    public void HandlesNegativeFromVersion_AsZero()
    {
        var cfg = new StepUpOptions
        {
            ForwardProbeSpan = 0,
            ForwardProbeDistance = 0,
        };

        bool changed = MigrationRunner.Run(cfg, fromVersion: -5);

        changed.Should().BeTrue();
        cfg.ForwardProbeSpan.Should().Be(1);
        cfg.ForwardProbeDistance.Should().Be(1);
    }

    /// <summary>
    /// A config that's "at the source version" but already has correct values
    /// (e.g. a manually-edited file that anticipates the new defaults) gets
    /// the migration evaluated, but the migration reports no change. The
    /// runner correctly forwards the migration's <c>false</c> return.
    /// </summary>
    [Fact]
    public void ReturnsFalse_WhenMigrationApplies_ButValuesUnchanged()
    {
        var cfg = new StepUpOptions
        {
            // Already at the v2 defaults, even though SchemaVersion is 0.
            ForwardProbeSpan = 1,
            ForwardProbeDistance = 1,
        };

        bool changed = MigrationRunner.Run(cfg, fromVersion: 0);

        changed.Should().BeFalse();
        cfg.ForwardProbeSpan.Should().Be(1);
        cfg.ForwardProbeDistance.Should().Be(1);
    }

    /// <summary>
    /// The runner does NOT touch <c>SchemaVersion</c> itself — that's the
    /// caller's job (currently <c>ConfigStore.MergeAndMigrate</c>). This
    /// test pins that contract so future refactors don't accidentally move
    /// the responsibility.
    /// </summary>
    [Fact]
    public void DoesNotModify_SchemaVersion()
    {
        var cfg = new StepUpOptions
        {
            SchemaVersion = 0,
            ForwardProbeSpan = 0,
            ForwardProbeDistance = 0,
        };

        MigrationRunner.Run(cfg, fromVersion: 0);

        cfg.SchemaVersion.Should().Be(0);
    }
}
