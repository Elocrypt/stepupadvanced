namespace StepUpAdvanced.Domain.Physics;

/// <summary>
/// Pure-function clamp for step-speed (elevate-factor) values. Symmetric
/// counterpart to <see cref="StepHeightClamp"/> — same shape, same
/// asymmetry between client floor (always applies) and server range
/// (only when enforced).
/// </summary>
/// <remarks>
/// See <see cref="StepHeightClamp"/> for the design rationale; the only
/// differences here are the numeric values (0.7 vs 0.6 floor) and the
/// units (dimensionless multiplier vs blocks). There's no "absolute max"
/// constant for the same reason as on the height side — dead pre-Phase-5,
/// no concrete use case to wire up now.
/// </remarks>
internal static class ElevateFactorMath
{
    /// <summary>
    /// Hard lower bound for client-side step speed. The 0.7 value
    /// approximates VS's default elevate factor — clamping to this floor
    /// effectively means "the mod isn't slowing the step animation."
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
}
