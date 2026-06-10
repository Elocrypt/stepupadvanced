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
/// <b>ProtoMember numbering:</b> independent of <c>StepUpOptions</c>.
/// Never reuse numbers — adding a field gets the next free number.
/// Declared as <c>record class</c> for value equality, which gives test
/// assertions clean round-trip checks. Properties stay mutable so
/// protobuf-net's reflective deserializer can write into them.
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
    /// Server-managed blacklist pushed to the client on join. Merged with
    /// <c>BlockBlacklistOptions.BlockCodes</c> at probe time.
    /// </summary>
    [ProtoMember(10)] public List<string>? BlockBlacklist { get; set; }
}
