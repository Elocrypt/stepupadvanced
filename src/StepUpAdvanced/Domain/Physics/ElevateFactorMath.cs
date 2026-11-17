namespace StepUpAdvanced.Domain.Physics;

/// <summary>
/// Pure-function clamp for step-speed (elevate-factor) values. Symmetric
/// counterpart to <see cref="StepHeightClamp"/> — same shape, same
/// asymmetry between client floor (always applies) and server range
/// (only when enforced).
/// </summary>

internal static class ElevateFactorMath
{
    /// <summary>
    /// The multiplier that leaves VS's rise speed unchanged. Used when step-up
    /// is toggled off and as the starting value before any config is applied.
    /// </summary>
    public const float Vanilla = 1.0f;

    /// <summary>
    /// Hard lower bound for the client-side step-speed multiplier
    /// (1.0 = vanilla rise speed, so 0.7 is the slowest a player can set).
    /// </summary>
    public const float ClientMin = 0.7f;

    /// <summary>
    /// Fallback step-speed value used when <c>StepUpOptions.Current</c>
    /// is null. Matches <see cref="ClientMin"/> deliberately.
    /// </summary>
    public const float Default = 0.7f;

    /// <summary>
    /// Applies the client floor unconditionally and the server cap range
    /// only when enforcement is active.
    /// </summary>
    /// <param name="requested">The value the client wants to use.</param>
    /// <param name="isEnforced">Whether server enforcement is currently active for this side.</param>
    /// <param name="serverMin">Server-configured minimum. Ignored when <paramref name="isEnforced"/> is <c>false</c>.</param>
    /// <param name="serverMax">Server-configured maximum. Ignored when <paramref name="isEnforced"/> is <c>false</c>.</param>
    /// <returns>The clamped step speed.</returns>
    public static float Clamp(float requested, bool isEnforced, float serverMin, float serverMax)
    {
        float result = requested < ClientMin ? ClientMin : requested;

        if (!isEnforced) return result;

        float effectiveMin = serverMin < ClientMin ? ClientMin : serverMin;

        if (result < effectiveMin) return effectiveMin;
        if (result > serverMax) return serverMax;
        return result;
    }

    /// <summary>
    /// The rise-speed multiplier that should actually be in effect. Priority,
    /// top to bottom: Basic mode pins <see cref="BasicMode.StepSpeed"/>; step-up
    /// toggled off is <see cref="Vanilla"/>, matching the height axis dropping
    /// to vanilla; otherwise the requested speed through <see cref="Clamp"/>,
    /// so enforced server caps always apply.
    /// </summary>
    /// <param name="basic">Basic mode is active (server doesn't run the mod).</param>
    /// <param name="stepUpEnabled">The player's step-up toggle.</param>
    /// <param name="requested">The player's configured speed.</param>
    /// <param name="isEnforced">Whether server enforcement is active.</param>
    /// <param name="serverMin">Server minimum; ignored when not enforced.</param>
    /// <param name="serverMax">Server maximum; ignored when not enforced.</param>
    public static float ResolveEffective(
        bool basic,
        bool stepUpEnabled,
        float requested,
        bool isEnforced,
        float serverMin,
        float serverMax)
    {
        if (basic) return BasicMode.StepSpeed;
        if (!stepUpEnabled) return Vanilla;
        return Clamp(requested, isEnforced, serverMin, serverMax);
    }
}
