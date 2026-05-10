using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using ProtoBuf;
using StepUpAdvanced.Configuration;
using StepUpAdvanced.Infrastructure.Network;
using Xunit;

namespace StepUpAdvanced.Tests.Infrastructure.Network;

/// <summary>
/// Locks in the contract of <see cref="ConfigSyncPacketMapper"/>. Three
/// invariants matter:
/// <list type="number">
///   <item><see cref="ConfigSyncPacketMapper.ToPacket"/> faithfully copies
///         the ten enforcement-relevant fields from a
///         <see cref="StepUpOptions"/> to a fresh
///         <see cref="ConfigSyncPacket"/>, and copies nothing else
///         (the wire shape is narrow by construction).</item>
///   <item><see cref="ConfigSyncPacketMapper.Apply"/> merges those ten
///         fields into an existing <see cref="StepUpOptions"/> without
///         touching client-local fields. This is the central reason the
///         type split exists — fields like <c>StepHeight</c>,
///         <c>StepSpeed</c>, and <c>QuietMode</c> must survive a server
///         push. (<c>BlockBlacklist</c> is intentionally NOT in that
///         set: the server owns it, and the client renders it through
///         <c>IsNearBlacklistedBlock</c>.)</item>
///   <item>Neither method mutates its input; both are idempotent.</item>
/// </list>
/// A protobuf roundtrip test additionally guards against accidental
/// <c>ProtoMember</c> renumbering on the wire DTO.
/// </summary>
public class ConfigSyncPacketMapperTests
{
    /// <summary>
    /// Sentinel values chosen so every wire field carries a distinctive,
    /// non-default payload. Defaults would let a buggy mapper silently
    /// pass tests by happening to read the right defaults.
    /// </summary>
    private static StepUpOptions BuildEnforcementSentinels() => new()
    {
        ServerEnforceSettings = true,
        AllowClientChangeStepHeight = false,
        AllowClientChangeStepSpeed = false,
        AllowClientConfigReload = true,
        ServerMinStepHeight = 0.65f,
        ServerMaxStepHeight = 1.55f,
        ServerMinStepSpeed = 0.75f,
        ServerMaxStepSpeed = 1.85f,
        ShowServerEnforcedNotice = false,
        BlockBlacklist = new List<string> { "test:fenceblock", "test:slabblock" },
    };

    [Fact]
    public void ToPacket_CopiesAllTenEnforcementFields()
    {
        var options = BuildEnforcementSentinels();

        var packet = ConfigSyncPacketMapper.ToPacket(options);

        packet.ServerEnforceSettings.Should().BeTrue();
        packet.AllowClientChangeStepHeight.Should().BeFalse();
        packet.AllowClientChangeStepSpeed.Should().BeFalse();
        packet.AllowClientConfigReload.Should().BeTrue();
        packet.ServerMinStepHeight.Should().Be(0.65f);
        packet.ServerMaxStepHeight.Should().Be(1.55f);
        packet.ServerMinStepSpeed.Should().Be(0.75f);
        packet.ServerMaxStepSpeed.Should().Be(1.85f);
        packet.ShowServerEnforcedNotice.Should().BeFalse();
        packet.BlockBlacklist.Should().BeEquivalentTo(new[] { "test:fenceblock", "test:slabblock" });
    }

    /// <summary>
    /// Inverse of the field-coverage check: build options where every
    /// client-only field has a distinctive non-default value, build a
    /// packet, and apply it back to a fresh options instance. The
    /// resulting instance must show no trace of those client-only
    /// values — they are not on the wire.
    /// </summary>
    [Fact]
    public void ToPacket_DoesNotCarryClientOnlyFields()
    {
        var source = new StepUpOptions
        {
            // Enforcement fields (so they don't all look like defaults
            // and we can be sure roundtrip works at all).
            ServerEnforceSettings = true,
            ServerMinStepHeight = 0.8f,
            ServerMaxStepHeight = 1.4f,
            // Client-only fields — these must not survive ToPacket→Apply
            // into a fresh options instance. (BlockBlacklist is omitted
            // here because it IS a wire field as of Phase 3b — its
            // propagation is asserted in the coverage test above.)
            StepHeight = 1.85f,
            StepSpeed = 1.95f,
            DefaultHeight = 0.45f,
            DefaultSpeed = 0.55f,
            StepHeightIncrement = 0.25f,
            StepSpeedIncrement = 0.25f,
            EnableHarmonyTweaks = false,
            SpeedOnlyMode = true,
            CeilingGuardEnabled = false,
            ForwardProbeCeiling = true,
            RequireForwardSupport = true,
            ForwardProbeDistance = 3,
            ForwardProbeSpan = 2,
            CeilingHeadroomPad = 0.35f,
            QuietMode = true,
        };
        var packet = ConfigSyncPacketMapper.ToPacket(source);

        var fresh = new StepUpOptions(); // all defaults
        ConfigSyncPacketMapper.Apply(fresh, packet);

        // Enforcement fields propagated.
        fresh.ServerEnforceSettings.Should().BeTrue();
        fresh.ServerMinStepHeight.Should().Be(0.8f);
        fresh.ServerMaxStepHeight.Should().Be(1.4f);

        // Client-only fields stayed at the destination's defaults; the
        // wire never carried them.
        var defaults = new StepUpOptions();
        fresh.StepHeight.Should().Be(defaults.StepHeight);
        fresh.StepSpeed.Should().Be(defaults.StepSpeed);
        fresh.DefaultHeight.Should().Be(defaults.DefaultHeight);
        fresh.DefaultSpeed.Should().Be(defaults.DefaultSpeed);
        fresh.StepHeightIncrement.Should().Be(defaults.StepHeightIncrement);
        fresh.StepSpeedIncrement.Should().Be(defaults.StepSpeedIncrement);
        fresh.EnableHarmonyTweaks.Should().Be(defaults.EnableHarmonyTweaks);
        fresh.SpeedOnlyMode.Should().Be(defaults.SpeedOnlyMode);
        fresh.CeilingGuardEnabled.Should().Be(defaults.CeilingGuardEnabled);
        fresh.ForwardProbeCeiling.Should().Be(defaults.ForwardProbeCeiling);
        fresh.RequireForwardSupport.Should().Be(defaults.RequireForwardSupport);
        fresh.ForwardProbeDistance.Should().Be(defaults.ForwardProbeDistance);
        fresh.ForwardProbeSpan.Should().Be(defaults.ForwardProbeSpan);
        fresh.CeilingHeadroomPad.Should().Be(defaults.CeilingHeadroomPad);
        fresh.QuietMode.Should().Be(defaults.QuietMode);
    }

    /// <summary>
    /// The mapper must not mutate the source options when building a
    /// packet — server-side broadcast happens at any time, including
    /// while other code is reading <c>StepUpOptions.Current</c>.
    /// </summary>
    [Fact]
    public void ToPacket_DoesNotMutateSource()
    {
        var options = BuildEnforcementSentinels();
        // Snapshot relevant fields before.
        bool enforce = options.ServerEnforceSettings;
        bool changeH = options.AllowClientChangeStepHeight;
        bool reload = options.AllowClientConfigReload;
        float minH = options.ServerMinStepHeight;
        float maxH = options.ServerMaxStepHeight;

        _ = ConfigSyncPacketMapper.ToPacket(options);

        options.ServerEnforceSettings.Should().Be(enforce);
        options.AllowClientChangeStepHeight.Should().Be(changeH);
        options.AllowClientConfigReload.Should().Be(reload);
        options.ServerMinStepHeight.Should().Be(minH);
        options.ServerMaxStepHeight.Should().Be(maxH);
    }

    /// <summary>
    /// The central client-side guarantee: applying a server packet must
    /// not clobber the client's local settings. This test sets every
    /// client-only field on an existing options instance, applies a
    /// packet, and checks that none of the client-only fields changed.
    /// </summary>
    [Fact]
    public void Apply_PreservesAllClientOnlyFields()
    {
        var current = new StepUpOptions
        {
            // Client-only payload that must survive the merge.
            // (BlockBlacklist intentionally omitted — it IS overwritten
            // by Apply now; that behavior is asserted in the dedicated
            // BlockBlacklist tests further down.)
            StepUpEnabled = false,
            StepHeight = 1.7f,
            StepSpeed = 1.6f,
            DefaultHeight = 0.5f,
            DefaultSpeed = 0.65f,
            StepHeightIncrement = 0.15f,
            StepSpeedIncrement = 0.2f,
            EnableHarmonyTweaks = false,
            SpeedOnlyMode = true,
            CeilingGuardEnabled = false,
            ForwardProbeCeiling = true,
            RequireForwardSupport = true,
            ForwardProbeDistance = 4,
            ForwardProbeSpan = 2,
            CeilingHeadroomPad = 0.3f,
            QuietMode = true,
        };
        // Server packet flipping every enforcement field.
        var packet = ConfigSyncPacketMapper.ToPacket(BuildEnforcementSentinels());

        ConfigSyncPacketMapper.Apply(current, packet);

        current.StepUpEnabled.Should().BeFalse();
        current.StepHeight.Should().Be(1.7f);
        current.StepSpeed.Should().Be(1.6f);
        current.DefaultHeight.Should().Be(0.5f);
        current.DefaultSpeed.Should().Be(0.65f);
        current.StepHeightIncrement.Should().Be(0.15f);
        current.StepSpeedIncrement.Should().Be(0.2f);
        current.EnableHarmonyTweaks.Should().BeFalse();
        current.SpeedOnlyMode.Should().BeTrue();
        current.CeilingGuardEnabled.Should().BeFalse();
        current.ForwardProbeCeiling.Should().BeTrue();
        current.RequireForwardSupport.Should().BeTrue();
        current.ForwardProbeDistance.Should().Be(4);
        current.ForwardProbeSpan.Should().Be(2);
        current.CeilingHeadroomPad.Should().Be(0.3f);
        current.QuietMode.Should().BeTrue();
    }

    /// <summary>
    /// Mirror of the preservation test: every wire field must be written
    /// to <c>current</c>. The pre-3b handler reasserted
    /// <c>AllowClientChange*</c> redundantly after the wholesale-replace
    /// (no-ops because the replace had already set them); the narrow
    /// mapper applies all ten fields uniformly with no redundancy.
    /// </summary>
    [Fact]
    public void Apply_OverwritesAllTenEnforcementFields()
    {
        // Start with the inverse of the sentinel values so every assert
        // below detects a missed write.
        var current = new StepUpOptions
        {
            ServerEnforceSettings = false,
            AllowClientChangeStepHeight = true,
            AllowClientChangeStepSpeed = true,
            AllowClientConfigReload = false,
            ServerMinStepHeight = 0.6f,
            ServerMaxStepHeight = 1.2f,
            ServerMinStepSpeed = 0.7f,
            ServerMaxStepSpeed = 1.3f,
            ShowServerEnforcedNotice = true,
            BlockBlacklist = new List<string> { "should-be-replaced" },
        };
        var packet = ConfigSyncPacketMapper.ToPacket(BuildEnforcementSentinels());

        ConfigSyncPacketMapper.Apply(current, packet);

        current.ServerEnforceSettings.Should().BeTrue();
        current.AllowClientChangeStepHeight.Should().BeFalse();
        current.AllowClientChangeStepSpeed.Should().BeFalse();
        current.AllowClientConfigReload.Should().BeTrue();
        current.ServerMinStepHeight.Should().Be(0.65f);
        current.ServerMaxStepHeight.Should().Be(1.55f);
        current.ServerMinStepSpeed.Should().Be(0.75f);
        current.ServerMaxStepSpeed.Should().Be(1.85f);
        current.ShowServerEnforcedNotice.Should().BeFalse();
        current.BlockBlacklist.Should().BeEquivalentTo(new[] { "test:fenceblock", "test:slabblock" });
    }

    /// <summary>
    /// "Enforcement turned off" transition — the bug class Phase 3a's
    /// always-broadcast fix targeted, now end-to-end with the narrow
    /// mapper. Apply must flip <c>ServerEnforceSettings</c> to false
    /// while still propagating the caps (which the client may use as
    /// hints even when enforcement is off).
    /// </summary>
    [Fact]
    public void Apply_EnforcementToggledOff_FlagFlipsCapsStillCarried()
    {
        var current = new StepUpOptions
        {
            ServerEnforceSettings = true,
            ServerMinStepHeight = 0.8f,
            ServerMaxStepHeight = 1.4f,
        };
        var packet = ConfigSyncPacketMapper.ToPacket(new StepUpOptions
        {
            ServerEnforceSettings = false,
            ServerMinStepHeight = 0.7f,
            ServerMaxStepHeight = 1.6f,
        });

        ConfigSyncPacketMapper.Apply(current, packet);

        current.ServerEnforceSettings.Should().BeFalse();
        current.ServerMinStepHeight.Should().Be(0.7f);
        current.ServerMaxStepHeight.Should().Be(1.6f);
    }

    /// <summary>
    /// Apply must not mutate the packet — packets may be reused across
    /// the broadcast loop in <c>ConfigSyncChannel.BroadcastToAll</c>
    /// (one allocation per batch, then sent to N players).
    /// </summary>
    [Fact]
    public void Apply_DoesNotMutatePacket()
    {
        var packet = ConfigSyncPacketMapper.ToPacket(BuildEnforcementSentinels());
        // Record's value-equality: snapshot via 'with' clone.
        var snapshot = packet with { };

        var current = new StepUpOptions();
        ConfigSyncPacketMapper.Apply(current, packet);

        packet.Should().Be(snapshot);
    }

    /// <summary>
    /// Idempotence: applying the same packet twice produces the same
    /// final state as applying it once. This matters because the server
    /// can send identical packets on rapid-fire reload commands or
    /// file-watcher debounce edges.
    /// </summary>
    [Fact]
    public void Apply_Idempotent()
    {
        var packet = ConfigSyncPacketMapper.ToPacket(BuildEnforcementSentinels());
        var once = new StepUpOptions();
        var twice = new StepUpOptions();

        ConfigSyncPacketMapper.Apply(once, packet);
        ConfigSyncPacketMapper.Apply(twice, packet);
        ConfigSyncPacketMapper.Apply(twice, packet);

        // Compare every enforcement-relevant field; not using record
        // equality because StepUpOptions is a class, not a record.
        twice.ServerEnforceSettings.Should().Be(once.ServerEnforceSettings);
        twice.AllowClientChangeStepHeight.Should().Be(once.AllowClientChangeStepHeight);
        twice.AllowClientChangeStepSpeed.Should().Be(once.AllowClientChangeStepSpeed);
        twice.AllowClientConfigReload.Should().Be(once.AllowClientConfigReload);
        twice.ServerMinStepHeight.Should().Be(once.ServerMinStepHeight);
        twice.ServerMaxStepHeight.Should().Be(once.ServerMaxStepHeight);
        twice.ServerMinStepSpeed.Should().Be(once.ServerMinStepSpeed);
        twice.ServerMaxStepSpeed.Should().Be(once.ServerMaxStepSpeed);
        twice.ShowServerEnforcedNotice.Should().Be(once.ShowServerEnforcedNotice);
        twice.BlockBlacklist.Should().BeEquivalentTo(once.BlockBlacklist);
    }

    /// <summary>
    /// Roundtrip through protobuf-net: serialize a packet, deserialize
    /// it, and check that every field came back identical. Catches
    /// accidental <c>ProtoMember</c> renumbering or attribute removal.
    /// </summary>
    [Fact]
    public void ProtobufRoundTrip_PreservesAllFields()
    {
        var original = ConfigSyncPacketMapper.ToPacket(BuildEnforcementSentinels());

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, original);
        stream.Position = 0;
        var revived = Serializer.Deserialize<ConfigSyncPacket>(stream);

        // BeEquivalentTo rather than Be() because record auto-generated
        // Equals uses reference equality for List<string>, and protobuf-net
        // allocates a new list instance on deserialization. The content is
        // what matters for a wire-format test, not the reference identity.
        revived.Should().BeEquivalentTo(original);
    }

    /// <summary>
    /// Default-valued packet roundtrip — protobuf default-value handling
    /// must not erase fields that legitimately equal the default. (In
    /// particular, a <c>false</c> bool isn't considered "missing"; it
    /// must come back as <c>false</c>.)
    /// </summary>
    [Fact]
    public void ProtobufRoundTrip_DefaultValuedPacket()
    {
        var original = new ConfigSyncPacket(); // all defaults

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, original);
        stream.Position = 0;
        var revived = Serializer.Deserialize<ConfigSyncPacket>(stream);

        // BeEquivalentTo for the same reason as above: future-proofing against
        // a default initializer being added to BlockBlacklist.
        revived.Should().BeEquivalentTo(original);
    }

    /// <summary>
    /// Sanity check: a brand-new <see cref="ConfigSyncPacket"/> has all
    /// ten fields and no others. If a future maintainer adds a property
    /// without a <c>ProtoMember</c> attribute, this test is a coarse
    /// reminder to add the attribute (the count check fails).
    /// </summary>
    [Fact]
    public void ConfigSyncPacket_HasExactlyTenProtoMembers()
    {
        var protoMemberCount = typeof(ConfigSyncPacket)
            .GetProperties()
            .SelectMany(p => p.GetCustomAttributes(typeof(ProtoMemberAttribute), inherit: false))
            .Count();

        protoMemberCount.Should().Be(10);
    }

    /// <summary>
    /// A null <see cref="ConfigSyncPacket.BlockBlacklist"/> on the wire
    /// (theoretical — protobuf-net normally hands back an empty list for
    /// a missing repeated field) must still leave
    /// <c>current.BlockBlacklist</c> non-null. The read sites
    /// (<c>IsNearBlacklistedBlock</c>, <c>/sua list</c>) treat the field
    /// as a non-null collection.
    /// </summary>
    [Fact]
    public void Apply_NullPacketBlacklist_GivesFreshEmptyListNotNull()
    {
        var packet = new ConfigSyncPacket { BlockBlacklist = null };
        var current = new StepUpOptions
        {
            BlockBlacklist = new List<string> { "stale" }
        };

        ConfigSyncPacketMapper.Apply(current, packet);

        current.BlockBlacklist.Should().NotBeNull();
        current.BlockBlacklist.Should().BeEmpty();
    }

    /// <summary>
    /// An empty list on the wire must clear out the client's view of the
    /// server blacklist. Otherwise, removing the last entry server-side
    /// via <c>/sua remove</c> would leave a stale entry on the client.
    /// </summary>
    [Fact]
    public void Apply_EmptyPacketBlacklist_ClearsCurrent()
    {
        var packet = new ConfigSyncPacket { BlockBlacklist = new List<string>() };
        var current = new StepUpOptions
        {
            BlockBlacklist = new List<string> { "stale-1", "stale-2" }
        };

        ConfigSyncPacketMapper.Apply(current, packet);

        current.BlockBlacklist.Should().BeEmpty();
    }
}
