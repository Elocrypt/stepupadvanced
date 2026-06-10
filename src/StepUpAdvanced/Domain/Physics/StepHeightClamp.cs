namespace StepUpAdvanced.Domain.Physics;

/// <summary>
/// Pure-function clamp for step-height values. Owns the canonical client
/// floor and the default fallback for step height; the clamp itself is
/// framework-free and takes all server-side inputs as parameters so it
/// can be unit-tested without any VS API surface.
/// </summary>
/// <remarks>
/// Asymmetric by design: the client floor (<see cref="ClientMin"/>) applies
/// always; the server min/max only apply when enforcement is active.
/// Defensive against <c>serverMin &lt; ClientMin</c> — the higher of the two
/// always wins so the client never drops below its own floor.
/// </remarks>
internal static class StepHeightClamp
{
    /// <summary>
    /// Hard lower bound for client-side step height, in blocks. Applies in
    /// every context — single-player, enforced multiplayer, or anywhere
    /// else. The "0.6 blocks" value matches VS's default player step height
    /// pre-mod, so clamping to this floor effectively means "the mod isn't
    /// raising the step height at all" rather than "the mod is sinking the
    /// player below normal."
    /// </summary>
    public const float ClientMin = 0.6f;

    /// <summary>
    /// Fallback step-height value used when <c>StepUpOptions.Current</c> is
    /// null (early in init, or after a load failure). Matches
    /// <see cref="ClientMin"/> deliberately — the safest fallback is the
    /// most conservative one.
    /// </summary>
    public const float Default = 0.6f;

    /// <summary>
    /// Applies the client floor unconditionally and the server cap range
    /// only when enforcement is active.
    /// </summary>
    /// <param name="requested">The value the client wants to use.</param>
    /// <param name="isEnforced">Whether server enforcement is currently active for this side.</param>
    /// <param name="serverMin">Server-configured minimum. Ignored when <paramref name="isEnforced"/> is <c>false</c>.</param>
    /// <param name="serverMax">Server-configured maximum. Ignored when <paramref name="isEnforced"/> is <c>false</c>.</param>
    /// <returns>The clamped step height.</returns>
    public static float Clamp(float requested, bool isEnforced, float serverMin, float serverMax)
    {
        // Client floor first — applies always. Hand-coded rather than
        // GameMath.Clamp because we only want a lower bound here.
        float result = requested < ClientMin ? ClientMin : requested;

        if (!isEnforced) return result;

        // Server range, with defensive behavior when serverMin < ClientMin
        // (we never drop below ClientMin regardless of server config).
        float effectiveMin = serverMin < ClientMin ? ClientMin : serverMin;

        if (result < effectiveMin) return effectiveMin;
        if (result > serverMax) return serverMax;
        return result;
    }
}
