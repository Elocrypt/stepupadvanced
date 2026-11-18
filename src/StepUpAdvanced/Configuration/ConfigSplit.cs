using System.Collections.Generic;

namespace StepUpAdvanced.Configuration;

/// <summary>
/// Moves values between the merged runtime view (<see cref="StepUpOptions"/>)
/// and the two on-disk files (<see cref="StepUpServerOptions"/>,
/// <see cref="StepUpClientOptions"/>).
/// </summary>
/// <remarks>
/// <para>
/// This is the single place that decides which side owns a field. The two
/// <c>Apply*</c> methods write only their own side's fields, which is what
/// lets the client and the integrated server share one in-memory
/// <see cref="StepUpOptions.Current"/> in single-player without overwriting
/// each other. Tests check that every <see cref="StepUpOptions"/> property
/// belongs to exactly one side.
/// </para>
/// <para>Pure and framework-free. Never mutates its source; lists are copied.</para>
/// </remarks>
internal static class ConfigSplit
{
    /// <summary>Builds the server file's contents from the runtime view.</summary>
    public static StepUpServerOptions ToServer(StepUpOptions o) => new()
    {
        ServerEnforceSettings = o.ServerEnforceSettings,
        AllowClientChangeStepHeight = o.AllowClientChangeStepHeight,
        AllowClientChangeStepSpeed = o.AllowClientChangeStepSpeed,
        AllowClientConfigReload = o.AllowClientConfigReload,
        ServerMinStepHeight = o.ServerMinStepHeight,
        ServerMaxStepHeight = o.ServerMaxStepHeight,
        ServerMinStepSpeed = o.ServerMinStepSpeed,
        ServerMaxStepSpeed = o.ServerMaxStepSpeed,
        ShowServerEnforcedNotice = o.ShowServerEnforcedNotice,
        BlockBlacklist = new List<string>(o.BlockBlacklist ?? new List<string>()),
        ServerRequireSprintOnly = o.ServerRequireSprintOnly,
        ServerRequireNoAirborneStepUp = o.ServerRequireNoAirborneStepUp,
    };

    /// <summary>Builds the client file's contents from the runtime view.</summary>
    public static StepUpClientOptions ToClient(StepUpOptions o) => new()
    {
        StepUpEnabled = o.StepUpEnabled,
        StepHeight = o.StepHeight,
        StepSpeed = o.StepSpeed,
        DefaultHeight = o.DefaultHeight,
        StepHeightIncrement = o.StepHeightIncrement,
        StepSpeedIncrement = o.StepSpeedIncrement,
        EnableHarmonyTweaks = o.EnableHarmonyTweaks,
        SpeedOnlyMode = o.SpeedOnlyMode,
        CeilingGuardEnabled = o.CeilingGuardEnabled,
        ForwardProbeCeiling = o.ForwardProbeCeiling,
        RequireForwardSupport = o.RequireForwardSupport,
        ForwardProbeDistance = o.ForwardProbeDistance,
        ForwardProbeSpan = o.ForwardProbeSpan,
        CeilingHeadroomPad = o.CeilingHeadroomPad,
        QuietMode = o.QuietMode,
        SprintOnlyStepUp = o.SprintOnlyStepUp,
        DisableStepUpWhileSneaking = o.DisableStepUpWhileSneaking,
        DisableStepUpWhileAirborne = o.DisableStepUpWhileAirborne,
    };

    /// <summary>Writes the server file's fields onto <paramref name="target"/>; client fields are untouched.</summary>
    public static void ApplyServer(StepUpOptions target, StepUpServerOptions s)
    {
        target.ServerEnforceSettings = s.ServerEnforceSettings;
        target.AllowClientChangeStepHeight = s.AllowClientChangeStepHeight;
        target.AllowClientChangeStepSpeed = s.AllowClientChangeStepSpeed;
        target.AllowClientConfigReload = s.AllowClientConfigReload;
        target.ServerMinStepHeight = s.ServerMinStepHeight;
        target.ServerMaxStepHeight = s.ServerMaxStepHeight;
        target.ServerMinStepSpeed = s.ServerMinStepSpeed;
        target.ServerMaxStepSpeed = s.ServerMaxStepSpeed;
        target.ShowServerEnforcedNotice = s.ShowServerEnforcedNotice;
        target.BlockBlacklist = new List<string>(s.BlockBlacklist ?? new List<string>());
        target.ServerRequireSprintOnly = s.ServerRequireSprintOnly;
        target.ServerRequireNoAirborneStepUp = s.ServerRequireNoAirborneStepUp;
    }

    /// <summary>Writes the client file's fields onto <paramref name="target"/>; server fields are untouched.</summary>
    public static void ApplyClient(StepUpOptions target, StepUpClientOptions c)
    {
        target.StepUpEnabled = c.StepUpEnabled;
        target.StepHeight = c.StepHeight;
        target.StepSpeed = c.StepSpeed;
        target.DefaultHeight = c.DefaultHeight;
        target.StepHeightIncrement = c.StepHeightIncrement;
        target.StepSpeedIncrement = c.StepSpeedIncrement;
        target.EnableHarmonyTweaks = c.EnableHarmonyTweaks;
        target.SpeedOnlyMode = c.SpeedOnlyMode;
        target.CeilingGuardEnabled = c.CeilingGuardEnabled;
        target.ForwardProbeCeiling = c.ForwardProbeCeiling;
        target.RequireForwardSupport = c.RequireForwardSupport;
        target.ForwardProbeDistance = c.ForwardProbeDistance;
        target.ForwardProbeSpan = c.ForwardProbeSpan;
        target.CeilingHeadroomPad = c.CeilingHeadroomPad;
        target.QuietMode = c.QuietMode;
        target.SprintOnlyStepUp = c.SprintOnlyStepUp;
        target.DisableStepUpWhileSneaking = c.DisableStepUpWhileSneaking;
        target.DisableStepUpWhileAirborne = c.DisableStepUpWhileAirborne;
    }
}
