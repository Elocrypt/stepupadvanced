namespace StepUpAdvanced.Domain.Physics;

/// <summary>
/// Fixed profile and resolution rules for Basic mode: the locked mode the
/// client runs in while connected to a server that does not have
/// StepUp Advanced installed.
/// </summary>
/// <remarks>
/// <para>
/// The mod loads on servers without it (<c>requiredOnServer: false</c>), but
/// a server that never installed it never agreed to raised step height or
/// speed. Basic mode gives those players plain auto step-up and nothing
/// adjustable: every value that could RAISE either axis is a constant here,
/// never read from the client's config file, so editing the JSON does
/// nothing. Settings that can only LOWER step-up (the toggle, the
/// sprint/sneak/airborne gates, Ceiling Guard, the client blacklist) keep
/// working because they give no advantage.
/// </para>
/// <para>
/// Framework-free so the "config cannot leak into Basic mode" guarantee is
/// pinned under test.
/// </para>
/// </remarks>
internal static class BasicMode
{
    /// <summary>Step height in Basic mode: one full block, not a block plus a slab.</summary>
    public const float StepHeight = 1.2f;

    /// <summary>
    /// Step speed in Basic mode: the vanilla rise rate. Applied through
    /// <see cref="ElevateFactorMath.ResolveEffective"/>.
    /// </summary>
    public const float StepSpeed = 1.0f;

    /// <summary>
    /// Stands in for <c>StepUpOptions.DefaultHeight</c> (used near blacklisted
    /// blocks and under low ceilings). That option is user-editable, so in
    /// Basic mode it is replaced by VS's baseline step height.
    /// </summary>
    public const float FallbackHeight = 0.6f;

    /// <summary>
    /// The height-axis inputs <c>StepHeightController</c> composes each tick.
    /// In Basic mode all three are fixed; enforcement is forced off because
    /// the local <c>ServerEnforceSettings</c> flag describes the player's own
    /// single-player setup, not the server they are connected to.
    /// </summary>
    public static HeightInputs ResolveHeightInputs(
        bool basic,
        float configuredHeight,
        float configuredDefaultHeight,
        bool configuredEnforced)
        => basic
            ? new HeightInputs(StepHeight, FallbackHeight, IsEnforced: false)
            : new HeightInputs(configuredHeight, configuredDefaultHeight, configuredEnforced);
}

/// <summary>Resolved height-axis inputs for one tick.</summary>
/// <param name="TargetHeight">Requested step height before gates, blacklist, and clamp.</param>
/// <param name="DefaultHeight">Height used near blacklisted blocks and under low ceilings.</param>
/// <param name="IsEnforced">Whether server min/max and the server blacklist apply.</param>
internal readonly record struct HeightInputs(float TargetHeight, float DefaultHeight, bool IsEnforced);
