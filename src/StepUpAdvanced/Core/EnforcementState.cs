using StepUpAdvanced.Configuration;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Core;

/// <summary>
/// Pure helper for deciding whether server enforcement is currently in effect.
/// Framework-free: takes only the inputs it needs, returns a bool, no side effects,
/// no static-field coupling. Easy to unit test.
/// </summary>
/// <remarks>
/// <para>
/// After the Phase 3b hotfix-of-the-hotfix, the predicate is simply: is the
/// <c>ServerEnforceSettings</c> flag on? The previous "single-player clients
/// always return false" rule was reversed — a single-player player IS both
/// the client and the server admin, and may legitimately opt in to enforcing
/// caps and the server blacklist on themselves. The flag means what it says.
/// </para>
/// <para>
/// The <paramref name="side"/> and <paramref name="isSinglePlayer"/>
/// parameters are retained even though the body no longer reads them.
/// Existing call sites in <c>StepUpAdvancedModSystem.IsEnforced</c> and the
/// unit tests pass them in unmodified, and the signature gives future
/// asymmetric rules a place to land without churning every call site again.
/// </para>
/// </remarks>
internal static class EnforcementState
{
    /// <summary>
    /// True when server enforcement is currently active.
    /// </summary>
    /// <param name="side">
    ///   Retained for API stability and future asymmetric rules; currently unused.
    /// </param>
    /// <param name="isSinglePlayer">
    ///   Retained for API stability and future asymmetric rules; currently unused.
    /// </param>
    /// <param name="config">
    ///   The active config snapshot. <c>null</c> is treated as not-enforced —
    ///   we never enforce when we don't know what to enforce.
    /// </param>
    public static bool IsEnforced(EnumAppSide side, bool isSinglePlayer, StepUpOptions? config)
    {
        return config?.ServerEnforceSettings == true;
    }
}
