namespace StepUpAdvanced.Configuration;

/// <summary>
/// On-disk shape of <c>StepUpAdvanced_Client.json</c>: the player's own
/// settings. Written only by the client side.
/// </summary>
/// <remarks>
/// Every field here is also on <see cref="StepUpOptions"/>, the merged
/// runtime view. <see cref="ConfigSplit"/> moves values between the two.
/// Defaults must match <see cref="StepUpOptions"/>'s; a test enforces that.
/// The client's own block blacklist stays in its separate file
/// (<see cref="BlockBlacklistStore"/>).
/// </remarks>
public class StepUpClientOptions
{
    /// <summary>Schema version of this file. Starts at 1 (introduced in 1.6.0).</summary>
    public int SchemaVersion { get; set; } = ConfigStore.ClientSchema;

    public bool StepUpEnabled { get; set; } = true;

    public float StepHeight { get; set; } = 1.2f;
    public float StepSpeed { get; set; } = 1.3f;

    /// <summary>Fallback height near blacklisted blocks and under low ceilings.</summary>
    public float DefaultHeight { get; set; } = 0.6f;

    public float StepHeightIncrement { get; set; } = 0.1f;
    public float StepSpeedIncrement { get; set; } = 0.1f;

    public bool EnableHarmonyTweaks { get; set; } = true;
    public bool SpeedOnlyMode { get; set; } = false;

    public bool CeilingGuardEnabled { get; set; } = true;
    public bool ForwardProbeCeiling { get; set; } = false;
    public bool RequireForwardSupport { get; set; } = false;
    public int ForwardProbeDistance { get; set; } = 1;
    public int ForwardProbeSpan { get; set; } = 1;
    public float CeilingHeadroomPad { get; set; } = 0.05f;

    public bool QuietMode { get; set; } = false;

    public bool SprintOnlyStepUp { get; set; } = false;
    public bool DisableStepUpWhileSneaking { get; set; } = false;
    public bool DisableStepUpWhileAirborne { get; set; } = false;
}
