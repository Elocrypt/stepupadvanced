using StepUpAdvanced.Configuration;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Core;

/// <summary>
/// Pure helper for deciding whether server enforcement is currently in effect.
/// Framework-free: takes only the inputs it needs, returns a bool, no side effects,
/// no static-field coupling. Easy to unit test.
/// </summary>
/// <remarks>
/// Single-player players are also the server admin and may legitimately
/// opt in to enforcing caps on themselves, so the flag is honored verbatim.
/// The <c>side</c> and <c>isSinglePlayer</c> parameters are retained for
/// potential future asymmetric rules without churning every call site.
/// </remarks>
internal static class EnforcementState
{
    /// <summary>
    /// True when server enforcement is currently active.
    /// </summary>
    /// <param name="side">Retained for future asymmetric rules; currently unused.</param>
    /// <param name="isSinglePlayer">Retained for future asymmetric rules; currently unused.</param>
    /// <param name="config">
    ///   The active config snapshot. <c>null</c> is treated as not-enforced —
    ///   we never enforce when we don't know what to enforce.
    /// </param>
    public static bool IsEnforced(EnumAppSide side, bool isSinglePlayer, StepUpOptions? config)
    {
        return config?.ServerEnforceSettings == true;
    }
}
