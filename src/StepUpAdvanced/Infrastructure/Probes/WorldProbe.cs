using StepUpAdvanced.Domain.Probes;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace StepUpAdvanced.Infrastructure.Probes;

/// <summary>
/// Hot-path probe methods that read from the world. Owns scratch state
/// (a reusable <see cref="BlockPos"/>, a <see cref="BlockPos"/>[5] for
/// forward-column fan-out) so per-tick allocations are eliminated. Math
/// is delegated to the framework-free <see cref="CeilingProbeMath"/>
/// This class is the world-query adapter.
/// </summary>
/// <remarks>
/// <see cref="scratchPos"/> is reused across all per-tick cell lookups;
/// <see cref="columnScratch"/> holds the forward-column fan-out.
/// Not thread-safe — VS client ticks run on the main thread and probes
/// within a tick are sequential.
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
    /// Tuned to fire on sprint/fall velocity, not idle drift.
    /// </summary>
    private const double VelocityLookaheadThreshold1 = 0.05;
    private const double VelocityLookaheadThreshold2 = 0.15;

    /// <summary>
    /// Overworld dimension id. StepUp Advanced probes operate exclusively on
    /// the player's ground locomotion in the overworld, never inside boats or
    /// other mini-dimensions, so a fixed value is correct. <c>BlockPos.Set(int, int, int)</c>
    /// preserves the existing dimension across reuse, so the construction-time
    /// dimension persists through every <see cref="scratchPos"/> mutation.
    /// </summary>
    private const int OverworldDimensionId = 0;

    private readonly BlockPos scratchPos = new(OverworldDimensionId);
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
            columnScratch[i] = new BlockPos(OverworldDimensionId);
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

        // Velocity-aware lookahead: probe 2-3 blocks ahead when moving fast.
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
    /// Returns the height of the lowest collision surface above
    /// <paramref name="origin"/> up to <paramref name="maxCheck"/>, minus a
    /// headroom pad. Sub-cell precise: a top slab obstructs at <c>dy + 0.5</c>,
    /// not <c>dy</c>. Returns <paramref name="maxCheck"/> when nothing is hit.
    /// </summary>
    /// <param name="world">The world accessor used for block lookups.</param>
    /// <param name="origin">Player's current block position (or any base position to scan upward from).</param>
    /// <param name="maxCheck">Upper bound on the scan distance, in blocks.</param>
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
            float minY1 = LowestCollisionY(world, scratchPos);
            if (minY1 >= 0f)
                // First box-bearing cell holds the lowest obstruction; its
                // bottom face sits at dy + Y1 above the origin.
                return CeilingProbeMath.ClearanceToObstruction(dy, minY1, headroomPad);
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

    /// <summary>Sentinel returned when a cell has no collision boxes.</summary>
    private const float NoCollision = -1f;
    /// <summary>
    /// Lowest collision-box bottom face (<c>Y1</c>, cell-relative, 0..1+) at
    /// <paramref name="pos"/>, or <see cref="NoCollision"/> if the cell has none.
    /// </summary>
    /// <remarks>
    /// Uses <c>Block.GetCollisionBoxes(blockAccessor, pos)</c> — the same
    /// placed-state call VS's player collision uses — not the static
    /// <c>Block.CollisionBoxes</c> property, so a top slab reports ~0.5 while a
    /// bottom slab / full block reports 0. That sub-cell value is what stops the
    /// ceiling guard snapping to the cell floor and falsely blocking a climb
    /// under a raised partial block. Per-box null guard mirrors VS's own loop.
    /// </remarks>
    private static float LowestCollisionY(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        var boxes = block?.GetCollisionBoxes(world.BlockAccessor, pos);
        if (boxes == null || boxes.Length == 0) return NoCollision;

        float minY1 = float.MaxValue;
        for (int i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            if (box != null && box.Y1 < minY1) minY1 = box.Y1;
        }
        return minY1 == float.MaxValue ? NoCollision : minY1;
    }
}
