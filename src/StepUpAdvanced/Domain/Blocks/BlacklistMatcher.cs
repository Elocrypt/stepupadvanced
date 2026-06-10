using System.Collections.Generic;

namespace StepUpAdvanced.Domain.Blocks;

/// <summary>
/// Pure-function lookup for the two-list blacklist check at probe time.
/// Polymorphic over <see cref="IReadOnlyCollection{T}"/> so callers can
/// pass either the current <see cref="List{T}"/>-backed lists or a
/// future <see cref="HashSet{T}"/> without an API change.
/// </summary>

internal static class BlacklistMatcher
{
    /// <summary>
    /// True when <paramref name="blockCode"/> is present in
    /// <paramref name="patterns"/>. Empty / null collections return false.
    /// </summary>
    public static bool Matches(string blockCode, IReadOnlyCollection<string>? patterns)
    {
        if (patterns == null || patterns.Count == 0) return false;
        // Contains is O(n) on List, O(1) on HashSet — both implement
        // ICollection<T>.Contains via the IReadOnlyCollection passthrough.
        if (patterns is ICollection<string> coll) return coll.Contains(blockCode);
        // Defensive fallback for IReadOnlyCollection implementations that
        // aren't also ICollection (rare). Linear scan.
        foreach (var p in patterns)
        {
            if (p == blockCode) return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="blockCode"/> is present in either the
    /// server-side list or the client-side list. Single helper for the
    /// per-tick probe so the call site stays one line.
    /// </summary>
    public static bool MatchesAny(
        string blockCode,
        IReadOnlyCollection<string>? serverList,
        IReadOnlyCollection<string>? clientList)
    {
        return Matches(blockCode, serverList) || Matches(blockCode, clientList);
    }
}
