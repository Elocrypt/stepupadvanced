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
    /// When <c>true</c>, the mod's enhanced step height applies only while
    /// the player is sprinting; at all other times the step height falls back
    /// to VS's vanilla baseline. Client feel-preference — deliberately NOT in
    /// <c>ConfigSyncPacket</c>, so the server never dictates it (same scope as
    /// <see cref="QuietMode"/>). Defaults <c>false</c>, so existing behavior is
    /// preserved until a user opts in; a missing JSON key parses to <c>false</c>,
    /// which is why this needs no schema bump or migration.
    /// </summary>
    /// <remarks>
    /// Gating happens on the HEIGHT axis only — the rise-speed axis lives in
    /// the Harmony-patched <c>TryStepSmooth</c>, which already differentiates
    /// sprint/sneak/walk. Collapsing the step height to the vanilla baseline is
    /// sufficient to make "step up only while sprinting" behave. Evaluated each
    /// tick by <c>StepUpGate.ShouldApplyStepUp</c>.
    /// </remarks>
    [ProtoMember(28), DefaultValue(false)] public bool SprintOnlyStepUp { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, the mod's enhanced step height is suppressed while the
    /// player is sneaking (the step height falls back to VS's vanilla baseline),
    /// letting players sneak for precise edge placement without auto-stepping.
    /// Client feel-preference; same scope, default, and height-only semantics as
    /// <see cref="SprintOnlyStepUp"/>. Evaluated each tick by
    /// <c>StepUpGate.ShouldApplyStepUp</c>; sneak suppression takes precedence
    /// over <see cref="SprintOnlyStepUp"/> if both ever apply on the same tick.
    /// </summary>
    [ProtoMember(29), DefaultValue(false)] public bool DisableStepUpWhileSneaking { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, the mod's enhanced step height is suppressed while the
    /// player is airborne (not on ground and not swimming); the step height
    /// falls back to VS's vanilla baseline. Targets the "step-up clears gaps I
    /// fell just short of" reports: a tall step height makes the brief
    /// ground-contact of a landing/edge-catch snap the player up much further
    /// than vanilla, reading as airborne stepping. Keeping the height at the
    /// baseline throughout the airborne descent makes those edge-catches use
    /// vanilla reach. Client feel-preference; same scope, default, and
    /// height-only semantics as <see cref="SprintOnlyStepUp"/>. Evaluated each
    /// tick by <c>StepUpGate.ShouldApplyStepUp</c>.
    /// </summary>
    /// <remarks>
    /// "Airborne" mirrors vanilla's own step gate (<c>!OnGround &amp;&amp;
    /// !Swimming</c>) — swimming is NOT treated as airborne. Ground-initiated
    /// steps are unaffected: they fire from a grounded tick where the enhanced
    /// height was already written, and the brief lift of the rise itself
    /// happens after the field is read.
    /// </remarks>
    [ProtoMember(30), DefaultValue(false)] public bool DisableStepUpWhileAirborne { get; set; } = false;

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
