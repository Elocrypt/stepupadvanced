using System.Collections.Generic;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// On-disk shape of <c>StepUpAdvanced_Server.json</c>: the settings a server
/// owns and pushes to clients. Written only by the server side (a dedicated
/// server, or the integrated server in single-player).
/// </summary>
/// <remarks>
/// <para>
/// Every field here is also on <see cref="StepUpOptions"/>, which stays the
/// merged runtime view the rest of the mod reads. <see cref="ConfigSplit"/>
/// moves values between the two. Defaults must match
/// <see cref="StepUpOptions"/>'s; a test enforces that.
/// </para>
/// <para>
/// Any new server-owned field goes here, on <see cref="StepUpOptions"/>, in
/// <see cref="ConfigSplit"/>, and on the wire packet and its mapper.
/// </para>
/// </remarks>
public class StepUpServerOptions
{
    /// <summary>Schema version of this file. Starts at 1 (introduced in 1.6.0).</summary>
    public int SchemaVersion { get; set; } = ConfigStore.ServerSchema;

    public bool ServerEnforceSettings { get; set; } = true;
    public bool AllowClientChangeStepHeight { get; set; } = true;
    public bool AllowClientChangeStepSpeed { get; set; } = true;
    public bool AllowClientConfigReload { get; set; } = false;

    public float ServerMinStepHeight { get; set; } = 0.6f;
    public float ServerMaxStepHeight { get; set; } = 1.2f;
    public float ServerMinStepSpeed { get; set; } = 0.7f;
    public float ServerMaxStepSpeed { get; set; } = 1.3f;

    public bool ShowServerEnforcedNotice { get; set; } = true;

    /// <summary>Server-wide block blacklist, pushed to every client.</summary>
    public List<string> BlockBlacklist { get; set; } = new List<string>();

    /// <summary>With enforcement on, step-up works only while sprinting, for everyone.</summary>
    public bool ServerRequireSprintOnly { get; set; } = false;

    /// <summary>With enforcement on, no enhanced step height while airborne, for everyone.</summary>
    public bool ServerRequireNoAirborneStepUp { get; set; } = false;
}
