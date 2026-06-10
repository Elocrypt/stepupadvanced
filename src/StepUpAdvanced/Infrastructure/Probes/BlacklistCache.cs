using System.Collections.Generic;

namespace StepUpAdvanced.Infrastructure.Probes;

/// <summary>
/// Two-list blacklist union, cached as a <see cref="HashSet{T}"/> with
/// explicit dirty-flag invalidation. The cache is rebuilt lazily on the
/// next <see cref="RebuildIfDirty"/> call after a mutation site invokes
/// <see cref="MarkDirty"/>.
/// </summary>
/// <remarks>
/// The cache is initialized dirty so the first probe call always rebuilds —
/// the caller never has to mark dirty after instantiation.
/// </remarks>
internal sealed class BlacklistCache
{
    private readonly HashSet<string> set = new();
    private bool dirty = true;

    /// <summary>Number of unique codes in the cache (after rebuild).</summary>
    public int Count => set.Count;

    /// <summary>True if no codes are cached. Useful for early-exit at the probe site.</summary>
    public bool IsEmpty => set.Count == 0;

    /// <summary>
    /// Allocation-free membership test. Caller is responsible for having
    /// called <see cref="RebuildIfDirty"/> earlier in the same probe.
    /// </summary>
    public bool Contains(string blockCode) => set.Contains(blockCode);

    /// <summary>
    /// Marks the cache stale; the next <see cref="RebuildIfDirty"/> will
    /// reload from its inputs. Idempotent.
    /// </summary>
    public void MarkDirty() => dirty = true;

    /// <summary>
    /// If dirty, clears and rebuilds the cache from the supplied lists.
    /// No-op if clean. <c>null</c> inputs are treated as empty.
    /// </summary>
    /// <remarks>
    /// The "effective server list" composition (e.g. ignoring the server
    /// list when enforcement is off) is the caller's responsibility — pass
    /// <c>null</c> or an empty collection to exclude that list from the
    /// union. The cache itself doesn't know about enforcement; that means
    /// the caller MUST <see cref="MarkDirty"/> on enforcement transitions
    /// as well as on raw list mutations.
    /// </remarks>
    public void RebuildIfDirty(
        IReadOnlyCollection<string>? serverList,
        IReadOnlyCollection<string>? clientList)
    {
        if (!dirty) return;

        set.Clear();
        if (serverList != null)
        {
            foreach (var code in serverList) set.Add(code);
        }
        if (clientList != null)
        {
            foreach (var code in clientList) set.Add(code);
        }

        dirty = false;
    }
}
