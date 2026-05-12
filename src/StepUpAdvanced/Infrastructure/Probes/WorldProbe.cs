using System;
using System.Collections.Generic;
using StepUpAdvanced.Domain.Probes;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace StepUpAdvanced.Infrastructure.Probes;

/// <summary>
/// Hot-path probe methods that read from the world. Owns scratch state
/// (a reusable <see cref="BlockPos"/>, a <see cref="BlockPos"/>[5] for
/// forward-column fan-out) so per-tick allocations are eliminated. Math
/// is delegated to the framework-free <see cref="CeilingProbeMath"/>
/// (Phase 5); this class is the world-query adapter.
/// </summary>
/// <remarks>
/// <para>
/// Pre-Phase-6 the probe methods lived on <c>StepUpAdvancedModSystem</c>
/// and allocated freely — every <c>playerPos.EastCopy().NorthCopy()</c>
/// minted a new <see cref="BlockPos"/>, every <c>BuildForwardColumns</c>
/// call allocated a fresh array, and the blacklist check ran a
/// <c>List&lt;string&gt;.Contains</c> per cell. The combined surface was
/// roughly 25–35 small allocations per 50 ms tick.
/// </para>
/// <para>
/// The static <see cref="RingOffsets"/> table replaces the 8 chained
/// <c>EastCopy/WestCopy/NorthCopy/SouthCopy</c> calls. <see cref="scratchPos"/>
/// is reused across all single-cell lookups, including the velocity-aware
/// lookahead. <see cref="columnScratch"/> holds the (up to 5) forward
/// columns for the ceiling guard's fan-out.
/// </para>
/// <para>
/// One instance per <c>StepUpAdvancedModSystem</c>. Not thread-safe — the
/// VS client tick runs on the main thread and the scratch state is
/// stateful between method calls (but not between ticks; each tick's
/// probes are sequential).
/// </para>
/// </remarks>
internal sealed class WorldProbe
{
    /// <summary>The eight-cell ring at distance 1 from the player.</summary>
    private static readonly (int dx, int dz)[] RingOffsets =
    {
        ( 1,  0), (-1,  0), (0,  1), (0, -1),
        ( 1,  1), ( 1, -1), (-1,  1), (-1, -1),
    };

    /// <summary>
    /// Cardinal motion thresholds for the velocity-aware lookahead. At
    /// <c>&gt; 0.05</c> on an axis the probe adds one extra cell two
    /// blocks ahead; at <c>&gt; 0.15</c> a second cell three blocks ahead.
    /// Tuned (pre-Phase-6) to fire on sprint/fall, not idle drift.
    /// </summary>
    private const double VelocityLookaheadThreshold1 = 0.05;
    private const double VelocityLookaheadThreshold2 = 0.15;

    private readonly BlockPos scratchPos = new();
    private readonly BlockPos[] columnScratch = new BlockPos[5];

    /// <summary>
    /// Merged blacklist cache, rebuilt on demand from the two source
    /// lists. Public so the owning ModSystem can call <see cref="BlacklistCache.MarkDirty"/>
    /// at mutation sites.
    /// </summary>
    public BlacklistCache Blacklist { get; } = new();

    public WorldProbe()
    {
        for (int i = 0; i < columnScratch.Length; i++)
        {
            columnScratch[i] = new BlockPos();
        }
    }

    /// <summary>
    /// True if any cell in the 8-cell ring around the player — or in the
    /// velocity-aware lookahead extensions — contains a blacklisted block.
    /// Rebuilds the blacklist cache first if dirty.
    /// </summary>
    /// <param name="world">The world accessor.</param>
    /// <param name="playerPos">Player's current block position.</param>
    /// <param name="motion">Player's current motion vector (X/Z used).</param>
    /// <param name="serverList">
    ///   Effective server-side blacklist. The caller passes <c>null</c>
    ///   (or empty) when enforcement is off, so the cache excludes server
    ///   entries.
    /// </param>
    /// <param name="clientList">Client-side blacklist; always consulted.</param>
    public bool NearBlacklistedBlock(
        IWorldAccessor world,
        BlockPos playerPos,
        Vec3d motion,
        IReadOnlyCollection<string>? serverList,
        IReadOnlyCollection<string>? clientList)
    {
        Blacklist.RebuildIfDirty(serverList, clientList);
        if (Blacklist.IsEmpty) return false;

        // 8-cell ring at distance 1.
        for (int i = 0; i < RingOffsets.Length; i++)
        {
            var (dx, dz) = RingOffsets[i];
            scratchPos.Set(playerPos.X + dx, playerPos.Y, playerPos.Z + dz);
            if (CellIsBlacklisted(world, scratchPos)) return true;
        }

        // Velocity-aware lookahead. Same thresholds and shape as pre-Phase-6.
        double absX = Math.Abs(motion.X);
        if (absX > VelocityLookaheadThreshold1)
        {
            int dx = motion.X > 0 ? 1 : -1;
            scratchPos.Set(playerPos.X + 2 * dx, playerPos.Y, playerPos.Z);
            if (CellIsBlacklisted(world, scratchPos)) return true;
            if (absX > VelocityLookaheadThreshold2)
            {
                scratchPos.Set(playerPos.X + 3 * dx, playerPos.Y, playerPos.Z);
                if (CellIsBlacklisted(world, scratchPos)) return true;
            }
        }

        double absZ = Math.Abs(motion.Z);
        if (absZ > VelocityLookaheadThreshold1)
        {
            int dz = motion.Z > 0 ? 1 : -1;
            scratchPos.Set(playerPos.X, playerPos.Y, playerPos.Z + 2 * dz);
            if (CellIsBlacklisted(world, scratchPos)) return true;
            if (absZ > VelocityLookaheadThreshold2)
            {
                scratchPos.Set(playerPos.X, playerPos.Y, playerPos.Z + 3 * dz);
                if (CellIsBlacklisted(world, scratchPos)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the first solid-block Y above <paramref name="origin"/>
    /// up to <paramref name="maxCheck"/>, minus a cosmetic headroom pad.
    /// Returns <paramref name="maxCheck"/> when nothing is hit.
    /// </summary>
    /// <param name="startDy">Lowest dy to begin checking. Clamped to ≥ 1.</param>
    /// <param name="headroomPad">Subtracted from the hit distance.</param>
    public float DistanceToCeilingAt(
        IWorldAccessor world,
        BlockPos origin,
        float maxCheck,
        int startDy,
        float headroomPad)
    {
        if (startDy < 1) startDy = 1;
        int steps = (int)Math.Ceiling(maxCheck) + 1;

        for (int dy = startDy; dy <= steps; dy++)
        {
            scratchPos.Set(origin.X, origin.Y + dy, origin.Z);
            if (IsSolidBlock(world, scratchPos))
                return Math.Max(0f, dy - headroomPad);
        }
        return maxCheck;
    }

    /// <summary>
    /// True if there's any solid block in the forward column at
    /// <paramref name="basePos"/>, between the player's feet Y and the
    /// max-rise cap. Below ~1 block of rise the check is unnecessary
    /// (the foot is still inside the current cell during the animation).
    /// </summary>
    public bool HasLandingSupport(
        IWorldAccessor world,
        BlockPos basePos,
        double yawRad,
        int dist,
        float requestedStep)
    {
        if (requestedStep < 0.95f) return true;

        var (sx, sz) = CeilingProbeMath.ForwardOffset(yawRad, dist);
        int fwdX = basePos.X + sx;
        int fwdZ = basePos.Z + sz;

        int maxRise = CeilingProbeMath.MaxRiseClamp(requestedStep, hardCap: 2);
        if (maxRise <= 0) return true;

        int yTopSupport = basePos.Y + maxRise - 1;
        for (int y = yTopSupport; y >= basePos.Y; y--)
        {
            scratchPos.Set(fwdX, y, fwdZ);
            var b = world.BlockAccessor.GetBlock(scratchPos);
            if (b?.CollisionBoxes != null && b.CollisionBoxes.Length > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the forward-column fan-out into the reused
    /// <see cref="columnScratch"/> buffer and returns the count of valid
    /// columns. Caller iterates <c>columns[0..count]</c> via
    /// <see cref="GetColumn"/>.
    /// </summary>
    /// <remarks>
    /// Math (forward direction, perpendicular axis, span widths) is
    /// delegated to <see cref="CeilingProbeMath"/>; this method is the
    /// adapter that materializes the offset tuples into the scratch
    /// <see cref="BlockPos"/> buffer.
    /// </remarks>
    public int BuildForwardColumns(BlockPos basePos, double yawRad, int dist, int span)
    {
        var (fx, fz) = CeilingProbeMath.ForwardOffset(yawRad, dist);
        int cx = basePos.X + fx;
        int cz = basePos.Z + fz;

        columnScratch[0].Set(cx, basePos.Y, cz);
        if (span <= 0) return 1;

        var (px, pz) = CeilingProbeMath.PerpendicularOffset(yawRad);
        columnScratch[1].Set(cx + px, basePos.Y, cz + pz);
        columnScratch[2].Set(cx - px, basePos.Y, cz - pz);
        if (span < 2) return 3;

        columnScratch[3].Set(cx + 2 * px, basePos.Y, cz + 2 * pz);
        columnScratch[4].Set(cx - 2 * px, basePos.Y, cz - 2 * pz);
        return 5;
    }

    /// <summary>
    /// Reads a column from the scratch buffer. The returned
    /// <see cref="BlockPos"/> reference is stable across calls — callers
    /// should NOT cache it past the next probe cycle.
    /// </summary>
    public BlockPos GetColumn(int index) => columnScratch[index];

    /// <summary>
    /// True if any cell in the Y range <c>[yFrom, yToExclusive)</c> at
    /// <paramref name="columnXZ"/> is a solid block. Uses
    /// <see cref="scratchPos"/> — must not interleave with the ring probe
    /// within a single tick (current call structure runs them sequentially).
    /// </summary>
    public bool ColumnHasSolid(
        IWorldAccessor world,
        BlockPos columnXZ,
        int yFrom,
        int yToExclusive)
    {
        scratchPos.Set(columnXZ.X, 0, columnXZ.Z);
        for (int y = yFrom; y < yToExclusive; y++)
        {
            scratchPos.Y = y;
            var block = world.BlockAccessor.GetBlock(scratchPos);
            if (block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0)
                return true;
        }
        return false;
    }

    private bool CellIsBlacklisted(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        var code = block?.Code?.ToString();
        return code != null && Blacklist.Contains(code);
    }

    private static bool IsSolidBlock(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        return block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0;
    }
}
