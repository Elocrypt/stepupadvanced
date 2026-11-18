using System.Collections.Generic;
using System.ComponentModel;
using ProtoBuf;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Merged runtime view of every StepUp Advanced setting. Everything else in
/// the mod reads from <see cref="Current"/>.
/// </summary>
/// <remarks>
/// <para>
/// Since 1.6.0 this type is not saved directly. Server-owned fields persist in
/// <c>StepUpAdvanced_Server.json</c> (<see cref="StepUpServerOptions"/>) and
/// player-owned fields in <c>StepUpAdvanced_Client.json</c>
/// (<see cref="StepUpClientOptions"/>); <see cref="ConfigSplit"/> decides
/// which is which. It is still deserialized from the pre-1.6.0
/// <c>StepUpAdvancedConfig.json</c> to seed those files, so property names
/// must not change.
/// </para>
/// <para>
/// <b>ProtoMember numbers:</b> never reuse one. 9 was <c>DefaultSpeed</c>
/// (unused since 1.5.0, removed in 1.6.0).
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

    /// <summary>Fallback height near blacklisted blocks and under low ceilings.</summary>
    [ProtoMember(8), DefaultValue(0.6f)] public float DefaultHeight { get; set; } = 0.6f;

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
    /// the vanilla baseline is used otherwise. Player's own preference; a
    /// server can additionally require it (<see cref="ServerRequireSprintOnly"/>).
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
    /// jump reach onto ledges you fell just short of. Player's own preference; a
    /// server can additionally require it (<see cref="ServerRequireNoAirborneStepUp"/>).
    /// </summary>
    /// <remarks>
    /// Mirrors vanilla's own step gate: swimming is not treated as airborne.
    /// Ground-initiated steps are unaffected — the enhanced height is already
    /// written during the grounded tick before any rise begins.
    /// </remarks>
    [ProtoMember(30), DefaultValue(false)] public bool DisableStepUpWhileAirborne { get; set; } = false;

    /// <summary>
    /// Server-owned. When enforcement is on, every player gets sprint-only
    /// step-up regardless of their own <see cref="SprintOnlyStepUp"/>.
    /// The server can add this restriction but never remove a player's own.
    /// </summary>
    [ProtoMember(31), DefaultValue(false)] public bool ServerRequireSprintOnly { get; set; } = false;

    /// <summary>
    /// Server-owned. When enforcement is on, enhanced step height is
    /// suppressed while airborne for every player, regardless of their own
    /// <see cref="DisableStepUpWhileAirborne"/>.
    /// </summary>
    [ProtoMember(32), DefaultValue(false)] public bool ServerRequireNoAirborneStepUp { get; set; } = false;

    /// <summary>
    /// Ambient global access to the active options. Reads happen everywhere.
    /// The instance is never replaced: <see cref="ConfigStore"/> and the
    /// packet mapper write their own fields onto it in place, which is what
    /// lets the client and the integrated server share it in single-player.
    /// </summary>
    public static StepUpOptions Current { get; } = new();

}
