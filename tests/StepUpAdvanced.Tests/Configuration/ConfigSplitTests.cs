using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Infrastructure.Network;
using Xunit;

namespace StepUpAdvanced.Tests.Configuration;

/// <summary>
/// Locks in the ownership split between <c>StepUpAdvanced_Server.json</c> and
/// <c>StepUpAdvanced_Client.json</c>. The guarantees that matter:
/// <list type="number">
///   <item>Every setting belongs to exactly one file, so a new property
///         can't silently go unsaved.</item>
///   <item>Applying one side's file never touches the other side's fields.
///         In single-player both sides share <see cref="StepUpOptions.Current"/>,
///         and this is what keeps them from overwriting each other.</item>
///   <item>Defaults match, so a fresh install and a migrated install behave
///         the same.</item>
/// </list>
/// </summary>
public class ConfigSplitTests
{
    private static readonly string[] NotPersisted = { nameof(StepUpOptions.SchemaVersion) };

    private static HashSet<string> PropertyNames<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => p.Name)
            .Except(NotPersisted)
            .ToHashSet();

    [Fact]
    public void EverySetting_BelongsToExactlyOneFile()
    {
        var runtime = PropertyNames<StepUpOptions>();
        var server = PropertyNames<StepUpServerOptions>();
        var client = PropertyNames<StepUpClientOptions>();

        server.Intersect(client).Should().BeEmpty("a setting can't be owned by both sides");
        server.Union(client).Should().BeEquivalentTo(runtime, "every runtime setting must be saved by exactly one side");
    }

    [Fact]
    public void ServerFile_HoldsExactlyTheFieldsThePacketCarries()
    {
        var server = PropertyNames<StepUpServerOptions>();
        var packet = typeof(ConfigSyncPacket).GetProperties().Select(p => p.Name).ToHashSet();

        server.Should().BeEquivalentTo(packet,
            "server-owned settings reach clients only through the packet, so the two sets must match");
    }

    [Fact]
    public void Defaults_MatchRuntimeDefaults()
    {
        var runtime = new StepUpOptions();

        ConfigSplit.ToServer(runtime).Should().BeEquivalentTo(new StepUpServerOptions());
        ConfigSplit.ToClient(runtime).Should().BeEquivalentTo(new StepUpClientOptions());
    }

    [Fact]
    public void ApplyServer_LeavesClientFieldsAlone()
    {
        var current = ClientSentinels();
        var clientBefore = ConfigSplit.ToClient(current);

        ConfigSplit.ApplyServer(current, ServerSentinels());

        ConfigSplit.ToClient(current).Should().BeEquivalentTo(clientBefore);
        ConfigSplit.ToServer(current).Should().BeEquivalentTo(ServerSentinels());
    }

    [Fact]
    public void ApplyClient_LeavesServerFieldsAlone()
    {
        var current = new StepUpOptions();
        ConfigSplit.ApplyServer(current, ServerSentinels());
        var serverBefore = ConfigSplit.ToServer(current);

        ConfigSplit.ApplyClient(current, ConfigSplit.ToClient(ClientSentinels()));

        ConfigSplit.ToServer(current).Should().BeEquivalentTo(serverBefore);
        ConfigSplit.ToClient(current).Should().BeEquivalentTo(ConfigSplit.ToClient(ClientSentinels()));
    }

    /// <summary>
    /// Migration path: a pre-1.6.0 file deserializes into
    /// <see cref="StepUpOptions"/>, then seeds each new file. Nothing may be lost.
    /// </summary>
    [Fact]
    public void LegacyConfig_SplitsWithoutLosingAnything()
    {
        var legacy = ClientSentinels();
        ConfigSplit.ApplyServer(legacy, ServerSentinels());

        var rebuilt = new StepUpOptions();
        ConfigSplit.ApplyServer(rebuilt, ConfigSplit.ToServer(legacy));
        ConfigSplit.ApplyClient(rebuilt, ConfigSplit.ToClient(legacy));

        rebuilt.Should().BeEquivalentTo(legacy, o => o.Excluding(x => x.SchemaVersion));
    }

    [Fact]
    public void ToServer_CopiesBlacklist_NeverAliases()
    {
        var current = new StepUpOptions { BlockBlacklist = new List<string> { "game:a" } };

        var file = ConfigSplit.ToServer(current);
        file.BlockBlacklist.Add("game:b");

        current.BlockBlacklist.Should().Equal("game:a");
    }

    [Fact]
    public void ApplyServer_CopiesBlacklist_AndHandlesNull()
    {
        var file = new StepUpServerOptions { BlockBlacklist = new List<string> { "game:a" } };
        var current = new StepUpOptions();

        ConfigSplit.ApplyServer(current, file);
        current.BlockBlacklist.Add("game:b");
        file.BlockBlacklist.Should().Equal("game:a");

        ConfigSplit.ApplyServer(current, new StepUpServerOptions { BlockBlacklist = null! });
        current.BlockBlacklist.Should().NotBeNull().And.BeEmpty();
    }

    private static StepUpServerOptions ServerSentinels() => new()
    {
        ServerEnforceSettings = false,
        AllowClientChangeStepHeight = false,
        AllowClientChangeStepSpeed = false,
        AllowClientConfigReload = true,
        ServerMinStepHeight = 0.65f,
        ServerMaxStepHeight = 1.55f,
        ServerMinStepSpeed = 0.75f,
        ServerMaxStepSpeed = 1.85f,
        ShowServerEnforcedNotice = false,
        BlockBlacklist = new List<string> { "test:fence", "test:slab" },
        ServerRequireSprintOnly = true,
        ServerRequireNoAirborneStepUp = true,
    };

    private static StepUpOptions ClientSentinels() => new()
    {
        StepUpEnabled = false,
        StepHeight = 1.7f,
        StepSpeed = 1.6f,
        DefaultHeight = 0.9f,
        StepHeightIncrement = 0.15f,
        StepSpeedIncrement = 0.2f,
        EnableHarmonyTweaks = false,
        SpeedOnlyMode = true,
        CeilingGuardEnabled = false,
        ForwardProbeCeiling = true,
        RequireForwardSupport = true,
        ForwardProbeDistance = 3,
        ForwardProbeSpan = 2,
        CeilingHeadroomPad = 0.3f,
        QuietMode = true,
        SprintOnlyStepUp = true,
        DisableStepUpWhileSneaking = true,
        DisableStepUpWhileAirborne = true,
    };
}
