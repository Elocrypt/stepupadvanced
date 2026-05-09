using StepUpAdvanced.Configuration;
using Vintagestory.API.Common;

namespace StepUpAdvanced.Core;

/// <summary>
/// Pure helper for deciding whether server enforcement is currently in effect.
/// Framework-free: takes only the inputs it needs, returns a bool, no side effects,
/// no static-field coupling. Easy to unit test.
/// </summary>
/// <remarks>
/// The original predicate in <c>StepUpAdvancedModSystem.IsEnforced</c> coupled
/// three things together: the static <c>capi</c>/<c>sapi</c> fields, the
/// <c>ServerEnforceSettings</c> flag, and (implicitly) single-player detection.
/// This helper makes the rule explicit:
/// <list type="bullet">
///   <item>The <c>ServerEnforceSettings</c> flag must be set.</item>
///   <item>AND we're either running on the server, or on a multiplayer client.</item>
/// </list>
/// Single-player clients always return <c>false</c> regardless of the flag —
/// there's no server to enforce against.
/// </remarks>
internal static class EnforcementState
{
    /// <summary>
    /// True when server enforcement is currently active for the calling side.
    /// </summary>
    /// <param name="side">The side this code is running on.</param>
    /// <param name="isSinglePlayer">
    ///   True if this is a single-player client. Ignored when <paramref name="side"/>
    ///   is <see cref="EnumAppSide.Server"/>. Callers on the server should pass
    ///   <c>false</c> (servers are never single-player by definition).
    /// </param>
    /// <param name="config">
    ///   The active config snapshot. <c>null</c> is treated as not-enforced —
    ///   we never enforce when we don't know what to enforce.
    /// </param>
    public static bool IsEnforced(EnumAppSide side, bool isSinglePlayer, StepUpOptions? config)
    {
        if (config?.ServerEnforceSettings != true) return false;
        return side == EnumAppSide.Server || !isSinglePlayer;
    }
}
