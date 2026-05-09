using System.Collections.Generic;
using System.ComponentModel;
using ProtoBuf;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Persistent + wire-protocol options for StepUp Advanced.
/// </summary>
/// <remarks>
/// <para>
/// This class is BOTH the on-disk JSON config (loaded/saved by
/// <see cref="ConfigStore"/>) AND the network-sync packet that the server
/// broadcasts to clients when enforcement is active. The dual role is why
/// every property carries a <see cref="ProtoMemberAttribute"/> with an
/// explicit number — those numbers are the wire-protocol contract.
/// </para>
/// <para>
/// <b>Wire-protocol stability:</b> never reuse <c>ProtoMember</c> numbers.
/// Adding a new field gets a new number. Renaming a field is fine (only
/// numbers matter on the wire). Removing a field requires bumping the
/// schema version and adding a migration.
/// </para>
/// <para>
/// <b>Schema version:</b> bumped whenever the field shape requires migration
/// of existing user configs (defaults changing, fields being deprecated).
/// Adding a brand-new field with a sensible default does NOT require a
/// schema bump — the JSON parser simply uses the default for missing keys.
/// </para>
/// <para>
/// Phase 2a renamed this class from <c>StepUpAdvancedConfig</c>. The on-disk
/// JSON file name (<c>StepUpAdvancedConfig.json</c>) and the protobuf wire
/// format are unchanged — the rename is purely a .NET-side identity change.
/// </para>
/// </remarks>
[ProtoContract]
public class StepUpOptions
{
    [ProtoMember(1), DefaultValue(true)] public bool StepUpEnabled { get; set; } = true;
    [ProtoMember(2), DefaultValue(true)] public bool ServerEnforceSettings { get; set; } = true;
    [ProtoMember(3), DefaultValue(true)] public bool AllowClientChangeStepHeight { get; set; } = true;
    [ProtoMember(4), DefaultValue(true)] public bool AllowClientChangeStepSpeed { get; set; } = true;
    [ProtoMember(5), DefaultValue(false)] public bool AllowClientConfigReload { get; set; } = false;

    [ProtoMember(6), DefaultValue(1.2f)] public float StepHeight { get; set; } = 1.2f;
    [ProtoMember(7), DefaultValue(1.3f)] public float StepSpeed { get; set; } = 1.3f;

    [ProtoMember(8), DefaultValue(0.6f)] public float DefaultHeight { get; set; } = 0.6f;
    [ProtoMember(9), DefaultValue(0.7f)] public float DefaultSpeed { get; set; } = 0.7f;

    [ProtoMember(10), DefaultValue(0.1f)] public float StepHeightIncrement { get; set; } = 0.1f;
    [ProtoMember(11), DefaultValue(0.1f)] public float StepSpeedIncrement { get; set; } = 0.1f;

    [ProtoMember(12)] public List<string> BlockBlacklist { get; set; } = new List<string>();

    [ProtoMember(13), DefaultValue(0.6f)] public float ServerMinStepHeight { get; set; } = 0.6f;
    [ProtoMember(14), DefaultValue(1.2f)] public float ServerMaxStepHeight { get; set; } = 1.2f;
    [ProtoMember(15), DefaultValue(0.7f)] public float ServerMinStepSpeed { get; set; } = 0.7f;
    [ProtoMember(16), DefaultValue(1.3f)] public float ServerMaxStepSpeed { get; set; } = 1.3f;

    [ProtoMember(17), DefaultValue(true)] public bool EnableHarmonyTweaks { get; set; } = true;
    [ProtoMember(18), DefaultValue(false)] public bool SpeedOnlyMode { get; set; } = false;
    [ProtoMember(19), DefaultValue(true)] public bool CeilingGuardEnabled { get; set; } = true;
    [ProtoMember(20), DefaultValue(false)] public bool ForwardProbeCeiling { get; set; } = false;
    [ProtoMember(21), DefaultValue(false)] public bool RequireForwardSupport { get; set; } = false;
    [ProtoMember(22), DefaultValue(1)] public int ForwardProbeDistance { get; set; } = 1;
    [ProtoMember(23), DefaultValue(1)] public int ForwardProbeSpan { get; set; } = 1;
    [ProtoMember(24), DefaultValue(0.05f)] public float CeilingHeadroomPad { get; set; } = 0.05f;
    [ProtoMember(25), DefaultValue(true)] public bool ShowServerEnforcedNotice { get; set; } = true;
    [ProtoMember(26)] public int SchemaVersion { get; set; } = ConfigStore.LatestSchema;
    [ProtoMember(27), DefaultValue(false)] public bool QuietMode { get; set; } = false;

    /// <summary>
    /// Ambient global access to the active options. Reads happen everywhere;
    /// writes happen only inside <see cref="ConfigStore"/> (hence the
    /// <c>internal</c> setter).
    /// </summary>
    /// <remarks>
    /// This static remains in place for Phase 2a as a behavior-preserving
    /// move from the old <c>StepUpAdvancedConfig.Current</c>. Phase 7 plans
    /// to move toward dependency injection for the orchestrator layer; the
    /// static-Current pattern stays where read-only access from many call
    /// sites is the dominant use case.
    /// </remarks>
    public static StepUpOptions Current { get; internal set; } = new();
}
