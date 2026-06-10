using System.Collections.Generic;
using System.ComponentModel;
using ProtoBuf;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Persistent + wire-protocol options for StepUp Advanced.
/// </summary>
/// <remarks>
/// Used as both the on-disk JSON config and the network wire format.
/// <b>Wire-protocol stability:</b> never reuse <c>ProtoMember</c> numbers —
/// new fields get new numbers; removing a field requires a migration.
/// Adding a field with a sensible default does not require a schema bump.
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
    /// When <c>true</c>, the enhanced step height applies only while sprinting;
    /// the vanilla baseline is used otherwise. Client-side preference — not
    /// server-enforced, no schema bump required (missing key defaults to <c>false</c>).
    /// </summary>
    [ProtoMember(28), DefaultValue(false)] public bool SprintOnlyStepUp { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, the enhanced step height is suppressed while sneaking,
    /// for precise edge placement without auto-stepping. Client-side preference.
    /// Sneak suppression takes precedence over <see cref="SprintOnlyStepUp"/>.
    /// </summary>
    [ProtoMember(29), DefaultValue(false)] public bool DisableStepUpWhileSneaking { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, the enhanced step height is suppressed while airborne
    /// (off the ground and not swimming), so a tall step height can't extend your
    /// jump reach onto ledges you fell just short of. Client-side preference.
    /// </summary>
    /// <remarks>
    /// Mirrors vanilla's own step gate: swimming is not treated as airborne.
    /// Ground-initiated steps are unaffected — the enhanced height is already
    /// written during the grounded tick before any rise begins.
    /// </remarks>
    [ProtoMember(30), DefaultValue(false)] public bool DisableStepUpWhileAirborne { get; set; } = false;

    /// <summary>
    /// Ambient global access to the active options. Reads happen everywhere;
    /// writes happen only inside <see cref="ConfigStore"/> (hence the
    /// <c>internal</c> setter).
    /// </summary>

    public static StepUpOptions Current { get; internal set; } = new();
}
