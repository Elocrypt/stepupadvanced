using System.Collections.Generic;
using ProtoBuf;

namespace StepUpAdvanced.Infrastructure.Network;

/// <summary>
/// Narrow wire DTO sent server → client when the server's enforcement
/// state needs to be communicated. Carries only the fields the client
/// must honor; everything else (the client's own <c>StepHeight</c>,
/// blacklist, probe tunables, <c>QuietMode</c>, etc.) stays local.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate type:</b> through Phase 3a the wire DTO was
/// <c>StepUpOptions</c>, which doubled as the on-disk persistence shape.
/// That coupling meant every server broadcast force-overwrote every
/// client-only field; introducing a new client-only setting (such as
/// <c>QuietMode</c> at <c>ProtoMember(27)</c>) silently broke as soon
/// as the server pushed an update. Decoupling persistence from wire
/// shape closes that hole and makes the client's enforcement contract
/// explicit at the type level.
/// </para>
/// <para>
/// <b>ProtoMember numbering:</b> independent of <c>StepUpOptions</c>.
/// The two contracts evolve separately. Within this packet, never reuse
/// numbers — adding a field gets the next free number. Removing a field
/// requires a coordinated client/server release; modinfo version-gating
/// is the only mechanism enforcing client/server pairing for this mod.
/// </para>
/// <para>
/// <b>Wire compatibility:</b> the Phase 3b release breaks the wire format
/// vs prior versions (server/client of mismatched major versions cannot
/// decode each other's packets). This is acceptable because
/// <c>modinfo.json</c> already enforces matching versions.
/// </para>
/// <para>
/// <b>Record class:</b> declared as <c>record class</c> for
/// auto-generated value equality, which gives test assertions clean
/// roundtrip checks (<c>actual.Should().Be(expected)</c>). Properties
/// remain mutable (<c>get; set;</c>) so protobuf-net's reflective
/// deserializer can write into them.
/// </para>
/// </remarks>
[ProtoContract]
internal sealed record class ConfigSyncPacket
{
    /// <summary>
    /// Master enforcement switch. When <c>false</c>, the client should
    /// behave as if no server caps exist (full local control).
    /// </summary>
    [ProtoMember(1)] public bool ServerEnforceSettings { get; set; }

    /// <summary>
    /// Permission for the client to adjust step height while enforcement
    /// is active. Read by the increase/decrease-height hotkey handlers.
    /// </summary>
    [ProtoMember(2)] public bool AllowClientChangeStepHeight { get; set; }

    /// <summary>
    /// Permission for the client to adjust step speed while enforcement
    /// is active. Read by the increase/decrease-speed hotkey handlers.
    /// </summary>
    [ProtoMember(3)] public bool AllowClientChangeStepSpeed { get; set; }

    /// <summary>
    /// Permission for the client to invoke <c>/sua reload</c> while
    /// enforcement is active.
    /// </summary>
    [ProtoMember(4)] public bool AllowClientConfigReload { get; set; }

    [ProtoMember(5)] public float ServerMinStepHeight { get; set; }
    [ProtoMember(6)] public float ServerMaxStepHeight { get; set; }
    [ProtoMember(7)] public float ServerMinStepSpeed { get; set; }
    [ProtoMember(8)] public float ServerMaxStepSpeed { get; set; }

    /// <summary>
    /// Whether the client should display the "server enforcement
    /// active" toast on enforcement transitions. Server-side admin
    /// preference; <c>true</c> by default.
    /// </summary>
    [ProtoMember(9)] public bool ShowServerEnforcedNotice { get; set; }

    /// <summary>
    /// Server-managed list of block codes that suppress step-up when
    /// the player is adjacent to one. The client merges this with its
    /// own <c>BlockBlacklistOptions.BlockCodes</c> at probe time
    /// (see <c>IsNearBlacklistedBlock</c>); both lists must be present
    /// for the merged check to behave correctly. Pre-3b this was
    /// piggy-backing on <c>StepUpOptions.BlockBlacklist</c>'s wholesale
    /// replace; the explicit field here makes the dependency visible
    /// at the wire-contract level.
    /// </summary>
    [ProtoMember(10)] public List<string>? BlockBlacklist { get; set; }
}
