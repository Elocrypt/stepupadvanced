using System.Collections.Generic;

namespace StepUpAdvanced.Domain.Blocks;

/// <summary>
/// Pure-function lookup for the two-list blacklist check at probe time.
/// Polymorphic over <see cref="IReadOnlyCollection{T}"/> so callers can
/// pass either the current <see cref="List{T}"/>-backed lists or a
/// future <see cref="HashSet{T}"/> without an API change.
/// </summary>
/// <remarks>
/// <para>
/// Phase 5 preserves the current strict-equality semantics (the per-tick
/// probe in <c>IsNearBlacklistedBlock</c> previously did
/// <c>list.Contains(code)</c> directly). Phase 6 swaps the call site's
/// backing collection to a cached HashSet that rebuilds on
/// blacklist-change events — the matcher's public surface is unchanged.
/// </para>
/// <para>
/// Wildcard / prefix / glob support is explicitly out of scope here. If
/// a future phase wants it, this is the right shelf to add it on (the
/// <see cref="Matches"/> signature already takes a pattern collection,
/// not just a single string).
/// </para>
/// </remarks>
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
